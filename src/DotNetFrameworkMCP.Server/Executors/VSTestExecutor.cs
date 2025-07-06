using System.Text;
using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetFrameworkMCP.Server.Executors;

/// <summary>
/// VSTest.Console.exe-based test executor
/// </summary>
public class VSTestExecutor : BaseTestExecutor
{
    public VSTestExecutor(
        ILogger<VSTestExecutor> logger,
        IOptions<McpServerConfiguration> configuration)
        : base(logger, configuration)
    {
    }

    public override async Task<TestResult> ExecuteTestsAsync(
        string projectPath,
        string? filter,
        bool verbose,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Validate project path
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException($"Test project file not found: {projectPath}");
            }

            var vstestPath = FindVSTestConsoleExecutable();
            if (string.IsNullOrEmpty(vstestPath))
            {
                throw new InvalidOperationException("VSTest.Console.exe not found. Please install Visual Studio or Build Tools.");
            }

            // Find test assembly
            var testAssembly = FindTestAssembly(projectPath);
            if (string.IsNullOrEmpty(testAssembly))
            {
                return new TestResult
                {
                    TotalTests = 0,
                    PassedTests = 0,
                    FailedTests = 0,
                    SkippedTests = 0,
                    Duration = 0,
                    TestDetails = new List<TestDetail>
                    {
                        new TestDetail
                        {
                            Name = "Assembly Not Found",
                            ClassName = "VSTest",
                            Result = "Failed",
                            Duration = 0,
                            ErrorMessage = $"No test assembly found for project {Path.GetFileNameWithoutExtension(projectPath)}",
                            StackTrace = null
                        }
                    },
                    Output = $"No test assemblies found for project {projectPath} in bin directories"
                };
            }

            _logger.LogInformation("Running tests from assembly: {TestAssembly}", testAssembly);

            var args = new StringBuilder($"\"{testAssembly}\"");

            if (!string.IsNullOrEmpty(filter))
            {
                args.Append($" /TestCaseFilter:\"{filter}\"");
            }

            // Always use detailed console output to capture error messages
            args.Append(" /logger:console;verbosity=detailed");

            // Try to find test adapters for better framework support
            var testAdapterPath = FindTestAdapterPath(projectPath);
            if (!string.IsNullOrEmpty(testAdapterPath))
            {
                args.Append($" /TestAdapterPath:\"{testAdapterPath}\"");
                _logger.LogDebug("Using test adapter path: {TestAdapterPath}", testAdapterPath);
            }

            // Add TRX logger for structured output
            var trxFileName = $"TestResults_{Guid.NewGuid():N}.trx";
            var trxFilePath = Path.Combine(Path.GetTempPath(), trxFileName);
            args.Append($" /logger:trx;LogFileName=\"{trxFilePath}\"");

            var result = await RunProcessAsync(vstestPath, args.ToString(), cancellationToken);
            
            // Parse results from TRX file
            var testResult = await ParseTrxFileAsync(trxFilePath, result.Output);
            
            // Clean up TRX file
            try
            {
                if (File.Exists(trxFilePath))
                    File.Delete(trxFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to delete TRX file: {Error}", ex.Message);
            }

            stopwatch.Stop();
            testResult.Duration = stopwatch.Elapsed.TotalSeconds;
            
            return testResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running VSTest for project: {ProjectPath}", projectPath);
            stopwatch.Stop();

            return new TestResult
            {
                TotalTests = 0,
                PassedTests = 0,
                FailedTests = 0,
                SkippedTests = 0,
                Duration = stopwatch.Elapsed.TotalSeconds,
                TestDetails = new List<TestDetail>
                {
                    new TestDetail
                    {
                        Name = "Test Execution Error",
                        ClassName = "VSTest",
                        Result = "Failed",
                        Duration = 0,
                        ErrorMessage = ex.Message,
                        StackTrace = ex.StackTrace
                    }
                },
                Output = $"VSTest execution failed: {ex.Message}\n{ex.StackTrace}"
            };
        }
    }

    private string? FindVSTestConsoleExecutable()
    {
        // Check environment variable first
        var envPath = Environment.GetEnvironmentVariable("VSTEST_CONSOLE_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        // Get preferred VS version from configuration
        var preferredVersion = _configuration.PreferredVSVersion?.ToLower() ?? "2022";
        
        var possiblePaths = new List<string>();

        // Add paths based on preferred version first
        if (preferredVersion == "2022" || preferredVersion == "auto")
        {
            possiblePaths.AddRange(new[]
            {
                @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"
            });
        }

        if (preferredVersion == "2019" || preferredVersion == "auto")
        {
            possiblePaths.AddRange(new[]
            {
                @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"
            });
        }

        var foundPath = possiblePaths.FirstOrDefault(File.Exists);
        if (foundPath != null)
        {
            var version = foundPath.Contains("2022") ? "2022" : foundPath.Contains("2019") ? "2019" : "Unknown";
            _logger.LogInformation("Found VSTest.Console.exe version {Version} at: {Path}", version, foundPath);
        }
        else
        {
            _logger.LogWarning("VSTest.Console.exe not found in standard locations");
        }

        return foundPath;
    }

    private string? FindTestAssembly(string projectPath)
    {
        var projectDir = Path.GetDirectoryName(projectPath);
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        
        // Look for test assemblies in bin directories
        var possiblePaths = new[]
        {
            Path.Combine(projectDir!, "bin", "Debug", $"{projectName}.dll"),
            Path.Combine(projectDir!, "bin", "Release", $"{projectName}.dll"),
            Path.Combine(projectDir!, "bin", "Debug", "net48", $"{projectName}.dll"),
            Path.Combine(projectDir!, "bin", "Release", "net48", $"{projectName}.dll"),
            Path.Combine(projectDir!, "bin", "Debug", "net472", $"{projectName}.dll"),
            Path.Combine(projectDir!, "bin", "Release", "net472", $"{projectName}.dll")
        };

        var testAssembly = possiblePaths.FirstOrDefault(File.Exists);
        
        if (string.IsNullOrEmpty(testAssembly))
        {
            // Fallback: search recursively
            var assemblyFiles = Directory.GetFiles(Path.Combine(projectDir!, "bin"), "*.dll", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f).Equals($"{projectName}.dll", StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            testAssembly = assemblyFiles.FirstOrDefault();
        }

        return testAssembly;
    }

    private string? FindTestAdapterPath(string projectPath)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(projectDir))
                return null;

            // Look for test adapters in packages folder (packages.config style)
            var currentDir = new DirectoryInfo(projectDir);
            while (currentDir != null)
            {
                var packagesDir = Path.Combine(currentDir.FullName, "packages");
                if (Directory.Exists(packagesDir))
                {
                    _logger.LogDebug("Searching for test adapters in packages directory: {PackagesDir}", packagesDir);

                    // Look for NUnit test adapter (prioritize NUnit3TestAdapter)
                    var nunitAdapterPatterns = new[] { "NUnit3TestAdapter.*", "NUnitTestAdapter.*" };
                    foreach (var pattern in nunitAdapterPatterns)
                    {
                        var nunitAdapterDirs = Directory.GetDirectories(packagesDir, pattern, SearchOption.TopDirectoryOnly);
                        foreach (var adapterDir in nunitAdapterDirs)
                        {
                            var possiblePaths = new[]
                            {
                                Path.Combine(adapterDir, "build"),
                                Path.Combine(adapterDir, "build", "net35"),
                                Path.Combine(adapterDir, "build", "net40"),
                                adapterDir
                            };

                            foreach (var path in possiblePaths)
                            {
                                if (Directory.Exists(path))
                                {
                                    var adapterDlls = Directory.GetFiles(path, "*TestAdapter*.dll", SearchOption.AllDirectories);
                                    if (adapterDlls.Length > 0)
                                    {
                                        _logger.LogInformation("Found NUnit test adapter at: {Path}", path);
                                        return path;
                                    }
                                }
                            }
                        }
                    }

                    // Look for xUnit test adapter as fallback
                    var xunitAdapterDirs = Directory.GetDirectories(packagesDir, "xunit.runner.visualstudio.*", SearchOption.TopDirectoryOnly);
                    foreach (var adapterDir in xunitAdapterDirs)
                    {
                        var buildPath = Path.Combine(adapterDir, "build");
                        if (Directory.Exists(buildPath))
                        {
                            _logger.LogInformation("Found xUnit test adapter at: {Path}", buildPath);
                            return buildPath;
                        }
                    }

                    break; // Only check the first packages directory found
                }

                currentDir = currentDir.Parent;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching for test adapter path for project: {ProjectPath}", projectPath);
            return null;
        }
    }
}