using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetFrameworkMCP.Server.Services;

public interface ITestRunnerService
{
    Task<TestResult> RunTestsAsync(string projectPath, string? filter, bool verbose, CancellationToken cancellationToken = default);
}

public class TestRunnerService : ITestRunnerService
{
    private readonly ILogger<TestRunnerService> _logger;
    private readonly McpServerConfiguration _configuration;

    public TestRunnerService(
        ILogger<TestRunnerService> logger,
        IOptions<McpServerConfiguration> configuration)
    {
        _logger = logger;
        _configuration = configuration.Value;
    }

    public async Task<TestResult> RunTestsAsync(
        string projectPath,
        string? filter,
        bool verbose,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var testDetails = new List<TestDetail>();

        try
        {
            // Validate project path
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException($"Test project file not found: {projectPath}");
            }

            // Determine test framework
            var testFramework = await DetectTestFrameworkAsync(projectPath);
            _logger.LogInformation("Detected test framework: {Framework} for project: {ProjectPath}", testFramework, projectPath);

            // Run tests based on framework
            var result = await RunTestsForFrameworkAsync(projectPath, testFramework, filter, verbose, cancellationToken);
            testDetails.AddRange(result.TestDetails);

            stopwatch.Stop();

            return new TestResult
            {
                TotalTests = testDetails.Count,
                PassedTests = testDetails.Count(t => t.Result == "Passed"),
                FailedTests = testDetails.Count(t => t.Result == "Failed"),
                SkippedTests = testDetails.Count(t => t.Result == "Skipped"),
                Duration = stopwatch.Elapsed.TotalSeconds,
                TestDetails = testDetails
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running tests for project: {ProjectPath}", projectPath);
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
                        ClassName = "System",
                        Result = "Failed",
                        Duration = 0,
                        ErrorMessage = ex.Message,
                        StackTrace = ex.StackTrace
                    }
                },
                Output = $"Test execution failed: {ex.Message}\n{ex.StackTrace}"
            };
        }
    }

    private async Task<string> DetectTestFrameworkAsync(string projectPath)
    {
        try
        {
            var projectContent = await File.ReadAllTextAsync(projectPath);

            // Check for package references in order of preference
            if (projectContent.Contains("Microsoft.NET.Test.Sdk") || projectContent.Contains("MSTest.TestFramework"))
            {
                return "MSTest";
            }
            
            if (projectContent.Contains("NUnit") || projectContent.Contains("nunit"))
            {
                return "NUnit";
            }
            
            if (projectContent.Contains("xunit") || projectContent.Contains("xUnit"))
            {
                return "xUnit";
            }

            // Default to VSTest runner which works with most frameworks
            _logger.LogWarning("Could not detect specific test framework, defaulting to VSTest");
            return "VSTest";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting test framework for project: {ProjectPath}", projectPath);
            return "VSTest";
        }
    }

    private async Task<TestResult> RunTestsForFrameworkAsync(
        string projectPath,
        string framework,
        string? filter,
        bool verbose,
        CancellationToken cancellationToken)
    {
        return framework switch
        {
            "MSTest" => await RunMSTestAsync(projectPath, filter, verbose, cancellationToken),
            "NUnit" => await RunNUnitAsync(projectPath, filter, verbose, cancellationToken),
            "xUnit" => await RunXUnitAsync(projectPath, filter, verbose, cancellationToken),
            _ => await RunVSTestAsync(projectPath, filter, verbose, cancellationToken)
        };
    }

    private async Task<TestResult> RunVSTestAsync(string projectPath, string? filter, bool verbose, CancellationToken cancellationToken)
    {
        // Check if we should use dotnet test instead
        if (_configuration.UseDotNetCli)
        {
            return await RunDotNetTestAsync(projectPath, filter, verbose, cancellationToken);
        }

        var vstestPath = FindVSTestConsoleExecutable();
        if (string.IsNullOrEmpty(vstestPath))
        {
            throw new InvalidOperationException("VSTest.Console.exe not found. Please install Visual Studio or Build Tools.");
        }

        // Find test assembly after MSBuild
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
                        ErrorMessage = $"No test assembly found for project {projectName}",
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

        // Add TRX logger for structured output - specify file location
        var trxFileName = $"TestResults_{Guid.NewGuid():N}.trx";
        var trxFilePath = Path.Combine(Path.GetTempPath(), trxFileName);
        args.Append($" /logger:trx;LogFileName=\"{trxFilePath}\"");

        var result = await RunProcessAsync(vstestPath, args.ToString(), cancellationToken);
        
        // Try to parse TRX file first for better error details, fallback to console output
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
        
        return testResult;
    }

    private async Task<TestResult> RunMSTestAsync(string projectPath, string? filter, bool verbose, CancellationToken cancellationToken)
    {
        // For .NET Framework MSTest, use MSBuild + VSTest
        return await RunMSBuildTestAsync(projectPath, filter, verbose, cancellationToken);
    }

    private async Task<TestResult> RunNUnitAsync(string projectPath, string? filter, bool verbose, CancellationToken cancellationToken)
    {
        // For .NET Framework NUnit, use MSBuild + VSTest with NUnit Test Adapter
        return await RunMSBuildTestAsync(projectPath, filter, verbose, cancellationToken);
    }

    private async Task<TestResult> RunXUnitAsync(string projectPath, string? filter, bool verbose, CancellationToken cancellationToken)
    {
        // For .NET Framework xUnit, use MSBuild + VSTest
        return await RunMSBuildTestAsync(projectPath, filter, verbose, cancellationToken);
    }

    private async Task<TestResult> RunMSBuildTestAsync(string projectPath, string? filter, bool verbose, CancellationToken cancellationToken)
    {
        // First build the test project using MSBuild
        var msbuildPath = FindMSBuildExecutable();
        if (string.IsNullOrEmpty(msbuildPath))
        {
            throw new InvalidOperationException("MSBuild.exe not found. Please install Visual Studio or Build Tools.");
        }

        // Find and build the solution instead of just the project
        var solutionPath = FindSolutionForProject(projectPath);
        var buildTarget = !string.IsNullOrEmpty(solutionPath) ? solutionPath : projectPath;
        
        _logger.LogInformation("Building {Type} with MSBuild: {Path}", 
            !string.IsNullOrEmpty(solutionPath) ? "solution" : "project", buildTarget);
        
        // Try different configuration combinations
        var configCombinations = new[]
        {
            new { Config = "Debug", Platform = "\"Any CPU\"" },
            new { Config = "Release", Platform = "\"Any CPU\"" },
            new { Config = "Debug", Platform = "\"Mixed Platforms\"" },
            new { Config = "Release", Platform = "\"Mixed Platforms\"" },
            new { Config = "Debug", Platform = "AnyCPU" },
            new { Config = "Release", Platform = "AnyCPU" }
        };

        (string Output, int ExitCode) buildResult = ("", 1);
        string usedConfig = "Debug";
        string usedPlatform = "Any CPU";

        foreach (var combo in configCombinations)
        {
            var buildArgs = $"\"{buildTarget}\" /p:Configuration={combo.Config} /p:Platform={combo.Platform} /v:minimal /nologo";
            _logger.LogDebug("Trying build with: Configuration={Config}, Platform={Platform}", combo.Config, combo.Platform);
            
            buildResult = await RunProcessAsync(msbuildPath, buildArgs, cancellationToken);
            
            if (buildResult.ExitCode == 0)
            {
                usedConfig = combo.Config;
                usedPlatform = combo.Platform.Trim('"');
                _logger.LogInformation("Build succeeded with Configuration={Config}, Platform={Platform}", usedConfig, usedPlatform);
                break;
            }
            else
            {
                _logger.LogDebug("Build failed with Configuration={Config}, Platform={Platform}: {Error}", 
                    combo.Config, combo.Platform, buildResult.Output.Split('\n').FirstOrDefault(l => l.Contains("error")));
            }
        }
        
        if (buildResult.ExitCode != 0)
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
                        Name = "Build Failed",
                        ClassName = "MSBuild",
                        Result = "Failed",
                        Duration = 0,
                        ErrorMessage = "Test project build failed",
                        StackTrace = null
                    }
                },
                Output = $"Build failed:\n{buildResult.Output}"
            };
        }

        // Now run tests using VSTest 
        return await RunVSTestAsync(projectPath, filter, verbose, cancellationToken);
    }

    private string? FindVSTestConsoleExecutable()
    {
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

        // If not auto mode and preferred version is not 2022 or 2019, add all versions as fallback
        if (preferredVersion != "auto" && preferredVersion != "2022" && preferredVersion != "2019")
        {
            _logger.LogWarning("Unknown PreferredVSVersion '{Version}', falling back to auto detection", preferredVersion);
            possiblePaths.AddRange(new[]
            {
                @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
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

    private string? FindTestAdapterPath(string projectPath)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(projectDir))
                return null;

            var adaptersFound = new List<string>();

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
                            // Check multiple possible locations for the adapter
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
                                        adaptersFound.Add(path);
                                        return path; // Return first found NUnit adapter
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
                            _logger.LogDebug("Found xUnit test adapter at: {Path}", buildPath);
                            adaptersFound.Add(buildPath);
                        }
                    }

                    // Look for MSTest adapter
                    var msTestAdapterDirs = Directory.GetDirectories(packagesDir, "MSTest.TestAdapter.*", SearchOption.TopDirectoryOnly);
                    foreach (var adapterDir in msTestAdapterDirs)
                    {
                        var buildPath = Path.Combine(adapterDir, "build");
                        if (Directory.Exists(buildPath))
                        {
                            _logger.LogDebug("Found MSTest adapter at: {Path}", buildPath);
                            adaptersFound.Add(buildPath);
                        }
                    }
                }
                
                currentDir = currentDir.Parent;
                if (currentDir == null || currentDir.FullName.Length <= 3)
                    break;
            }

            // Look for test adapters in the project output directory
            var outputDirs = new[]
            {
                Path.Combine(projectDir, "bin", "Debug"),
                Path.Combine(projectDir, "bin", "Release")
            };

            foreach (var outputDir in outputDirs)
            {
                if (Directory.Exists(outputDir))
                {
                    var adapterFiles = Directory.GetFiles(outputDir, "*TestAdapter*.dll", SearchOption.TopDirectoryOnly);
                    if (adapterFiles.Length > 0)
                    {
                        _logger.LogInformation("Found test adapter DLLs in output directory: {Path}", outputDir);
                        adaptersFound.Add(outputDir);
                        return outputDir;
                    }
                }
            }

            // If we found any adapters, return the first one
            if (adaptersFound.Count > 0)
            {
                _logger.LogInformation("Using test adapter path: {Path}", adaptersFound[0]);
                return adaptersFound[0];
            }

            _logger.LogWarning("No test adapters found for project: {ProjectPath}", projectPath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for test adapter path for project: {ProjectPath}", projectPath);
            return null;
        }
    }


    private string? FindMSBuildExecutable()
    {
        // Check environment variable first
        var envPath = Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        // Get preferred VS version from configuration
        var preferredVersion = _configuration.PreferredVSVersion?.ToLower() ?? "2022";
        
        // Look for MSBuild.exe in standard Visual Studio locations
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var possiblePaths = new List<string>();

        // Add paths based on preferred version first
        if (preferredVersion == "2022" || preferredVersion == "auto")
        {
            possiblePaths.AddRange(new[]
            {
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFiles, @"Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe")
            });
        }

        if (preferredVersion == "2019" || preferredVersion == "auto")
        {
            possiblePaths.AddRange(new[]
            {
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe")
            });
        }

        // If not auto mode and preferred version is not 2022 or 2019, add all versions as fallback
        if (preferredVersion != "auto" && preferredVersion != "2022" && preferredVersion != "2019")
        {
            _logger.LogWarning("Unknown PreferredVSVersion '{Version}', falling back to auto detection", preferredVersion);
            possiblePaths.AddRange(new[]
            {
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFiles, @"Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"),
                Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe")
            });
        }

        // Add legacy paths as final fallback
        possiblePaths.AddRange(new[]
        {
            Path.Combine(programFilesX86, @"MSBuild\14.0\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"MSBuild\15.0\Bin\MSBuild.exe"),
            @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe",
            @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
        });

        var foundPath = possiblePaths.FirstOrDefault(File.Exists);
        if (foundPath != null)
        {
            var version = foundPath.Contains("2022") ? "2022" : foundPath.Contains("2019") ? "2019" : "Legacy";
            _logger.LogInformation("Found MSBuild.exe version {Version} at: {Path}", version, foundPath);
        }
        else
        {
            _logger.LogWarning("MSBuild.exe not found in standard locations");
        }

        return foundPath;
    }

    private string? FindSolutionForProject(string projectPath)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(projectDir))
                return null;

            var currentDir = new DirectoryInfo(projectDir);
            
            // Search up the directory tree for .sln files
            while (currentDir != null)
            {
                var solutionFiles = currentDir.GetFiles("*.sln");
                if (solutionFiles.Length > 0)
                {
                    // If multiple solutions, prefer one that contains the project name
                    var projectName = Path.GetFileNameWithoutExtension(projectPath);
                    var matchingSolution = solutionFiles.FirstOrDefault(s => s.Name.Contains(projectName, StringComparison.OrdinalIgnoreCase));
                    
                    var selectedSolution = matchingSolution ?? solutionFiles.First();
                    _logger.LogInformation("Found solution for test project: {SolutionPath}", selectedSolution.FullName);
                    return selectedSolution.FullName;
                }
                
                currentDir = currentDir.Parent;
                
                // Don't go too far up - stop at drive root or after 5 levels
                if (currentDir == null || currentDir.FullName.Length <= 3 || projectDir.Split(Path.DirectorySeparatorChar).Length - currentDir.FullName.Split(Path.DirectorySeparatorChar).Length > 5)
                    break;
            }
            
            _logger.LogInformation("No solution file found for project: {ProjectPath}", projectPath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching for solution file for project: {ProjectPath}", projectPath);
            return null;
        }
    }

    private async Task<TestResult> RunDotNetTestAsync(string projectPath, string? filter, bool verbose, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var arguments = new List<string>
            {
                "test",
                $"\"{projectPath}\"",
                "--no-build" // Assume project is already built
            };

            if (!string.IsNullOrEmpty(filter))
            {
                arguments.Add("--filter");
                arguments.Add($"\"{filter}\"");
            }

            if (verbose)
            {
                arguments.Add("--verbosity");
                arguments.Add("detailed");
            }
            else
            {
                arguments.Add("--verbosity");
                arguments.Add("normal");
            }

            // Add logger for structured output
            var trxFileName = $"TestResults_{Guid.NewGuid():N}.trx";
            var trxFilePath = Path.Combine(Path.GetTempPath(), trxFileName);
            arguments.Add($"--logger");
            arguments.Add($"trx;LogFileName=\"{trxFilePath}\"");

            var argumentString = string.Join(" ", arguments);
            _logger.LogInformation("Running dotnet test: {Arguments}", argumentString);

            var result = await RunProcessAsync(_configuration.DotNetPath, argumentString, cancellationToken);
            
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
            _logger.LogError(ex, "Error running dotnet test for project: {ProjectPath}", projectPath);
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
                        ClassName = "DotNetTest",
                        Result = "Failed",
                        Duration = 0,
                        ErrorMessage = ex.Message,
                        StackTrace = ex.StackTrace
                    }
                },
                Output = $"Dotnet test execution failed: {ex.Message}\n{ex.StackTrace}"
            };
        }
    }

    private async Task<(string Output, int ExitCode)> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Running: {FileName} {Arguments}", fileName, arguments);

        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        var output = new StringBuilder();
        var errorOutput = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorOutput.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        var exitCode = process.ExitCode;
        var fullOutput = output.ToString();
        
        if (errorOutput.Length > 0)
        {
            fullOutput += "\n" + errorOutput.ToString();
        }

        _logger.LogDebug("Process exited with code: {ExitCode}", exitCode);
        
        return (fullOutput, exitCode);
    }

    private TestResult ParseDotNetTestOutput(string output)
    {
        var testDetails = new List<TestDetail>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Parse dotnet test output
        var totalTests = 0;
        var passedTests = 0;
        var failedTests = 0;
        var skippedTests = 0;
        var duration = 0.0;

        // Multiple patterns for different output formats
        var summaryPatterns = new[]
        {
            new Regex(@"Total tests:\s*(\d+)(?:\.|,)\s*Passed:\s*(\d+)(?:\.|,)\s*Failed:\s*(\d+)(?:\.|,)\s*Skipped:\s*(\d+)"),
            new Regex(@"Test Run (?:Successful|Failed)\.\s*Total tests:\s*(\d+)\s*Passed:\s*(\d+)\s*Failed:\s*(\d+)\s*Skipped:\s*(\d+)"),
            new Regex(@"Tests run:\s*(\d+),\s*Failures:\s*(\d+),\s*Errors:\s*(\d+),\s*Skipped:\s*(\d+)")
        };

        var timePatterns = new[]
        {
            new Regex(@"Total time:\s*([0-9.,]+)\s*(?:Seconds|s)", RegexOptions.IgnoreCase),
            new Regex(@"Time Elapsed\s*[:\-]?\s*([0-9:.,]+)", RegexOptions.IgnoreCase),
            new Regex(@"Elapsed time:\s*([0-9.,]+)\s*(?:seconds|s)", RegexOptions.IgnoreCase)
        };

        // Parse individual test results - more robust patterns
        var testResultPatterns = new[]
        {
            // Pattern: "  Passed TestNamespace.TestClass.TestMethod [< 1 ms]"
            new Regex(@"^\s*(Passed|Failed|Skipped)\s+([A-Za-z_][A-Za-z0-9_\.]*)\s*\[.*?\]", RegexOptions.IgnoreCase),
            // Pattern: "  × TestNamespace.TestClass.TestMethod"
            new Regex(@"^\s*[×✓✗]\s+([A-Za-z_][A-Za-z0-9_\.]*)", RegexOptions.IgnoreCase),
            // Pattern: "TestNamespace.TestClass.TestMethod ... ok"
            new Regex(@"^([A-Za-z_][A-Za-z0-9_\.]*)\s*\.\.\.\s*(ok|FAILED|passed|failed)", RegexOptions.IgnoreCase)
        };

        foreach (var line in lines)
        {
            // Try to parse summary information
            foreach (var pattern in summaryPatterns)
            {
                var match = pattern.Match(line);
                if (match.Success)
                {
                    if (pattern.ToString().Contains("Tests run"))
                    {
                        // MSTest format: Tests run: 3, Failures: 1, Errors: 0, Skipped: 0
                        totalTests = int.Parse(match.Groups[1].Value);
                        var failures = int.Parse(match.Groups[2].Value);
                        var errors = int.Parse(match.Groups[3].Value);
                        failedTests = failures + errors;
                        skippedTests = int.Parse(match.Groups[4].Value);
                        passedTests = totalTests - failedTests - skippedTests;
                    }
                    else
                    {
                        // Standard format
                        totalTests = int.Parse(match.Groups[1].Value);
                        passedTests = int.Parse(match.Groups[2].Value);
                        failedTests = int.Parse(match.Groups[3].Value);
                        skippedTests = int.Parse(match.Groups[4].Value);
                    }
                    break;
                }
            }

            // Try to parse timing information
            foreach (var pattern in timePatterns)
            {
                var match = pattern.Match(line);
                if (match.Success)
                {
                    var timeStr = match.Groups[1].Value.Replace(",", ".");
                    if (timeStr.Contains(":"))
                    {
                        // Parse time format like "00:00:02.45"
                        if (TimeSpan.TryParse(timeStr, out var timeSpan))
                        {
                            duration = timeSpan.TotalSeconds;
                        }
                    }
                    else
                    {
                        // Parse decimal seconds
                        if (double.TryParse(timeStr, out var parsedDuration))
                        {
                            duration = parsedDuration;
                        }
                    }
                    break;
                }
            }

            // Try to parse individual test results
            foreach (var pattern in testResultPatterns)
            {
                var match = pattern.Match(line);
                if (match.Success)
                {
                    var testDetail = ParseTestDetailFromMatch(match, line);
                    if (testDetail != null)
                    {
                        testDetails.Add(testDetail);
                    }
                    break;
                }
            }
        }

        // If we couldn't parse individual tests but have summary info, create generic entries
        if (testDetails.Count == 0 && totalTests > 0)
        {
            for (int i = 0; i < passedTests; i++)
            {
                testDetails.Add(new TestDetail
                {
                    Name = $"Test_{i + 1}",
                    ClassName = "Unknown",
                    Result = "Passed",
                    Duration = 0
                });
            }
            for (int i = 0; i < failedTests; i++)
            {
                testDetails.Add(new TestDetail
                {
                    Name = $"FailedTest_{i + 1}",
                    ClassName = "Unknown",
                    Result = "Failed",
                    Duration = 0
                });
            }
            for (int i = 0; i < skippedTests; i++)
            {
                testDetails.Add(new TestDetail
                {
                    Name = $"SkippedTest_{i + 1}",
                    ClassName = "Unknown",
                    Result = "Skipped",
                    Duration = 0
                });
            }
        }

        return new TestResult
        {
            TotalTests = totalTests,
            PassedTests = passedTests,
            FailedTests = failedTests,
            SkippedTests = skippedTests,
            Duration = duration,
            TestDetails = testDetails,
            Output = output // Include raw output for debugging
        };
    }

    private TestResult ParseVSTestOutput(string output)
    {
        _logger.LogDebug("Parsing VSTest output. First 20 lines:");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < Math.Min(20, lines.Length); i++)
        {
            _logger.LogDebug("Line {Index}: {Line}", i, lines[i]);
        }

        var testDetails = new List<TestDetail>();
        var totalTests = 0;
        var passedTests = 0;
        var failedTests = 0;
        var skippedTests = 0;
        var duration = 0.0;

        // Parse VSTest console output
        var currentTest = new TestDetail();
        var inFailureDetails = false;
        var errorMessageLines = new List<string>();
        var stackTraceLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Parse summary information
            if (line.Contains("Total tests:") || line.Contains("Test Run"))
            {
                var summaryMatch = Regex.Match(line, @"Total tests:\s*(\d+).*?Passed:\s*(\d+).*?Failed:\s*(\d+).*?Skipped:\s*(\d+)");
                if (summaryMatch.Success)
                {
                    totalTests = int.Parse(summaryMatch.Groups[1].Value);
                    passedTests = int.Parse(summaryMatch.Groups[2].Value);
                    failedTests = int.Parse(summaryMatch.Groups[3].Value);
                    skippedTests = int.Parse(summaryMatch.Groups[4].Value);
                }
                continue;
            }

            // Parse timing information
            var timeMatch = Regex.Match(line, @"Total time:\s*([0-9:.,]+)", RegexOptions.IgnoreCase);
            if (timeMatch.Success)
            {
                var timeStr = timeMatch.Groups[1].Value.Replace(",", ".");
                if (timeStr.Contains(":"))
                {
                    if (TimeSpan.TryParse(timeStr, out var timeSpan))
                    {
                        duration = timeSpan.TotalSeconds;
                    }
                }
                else if (double.TryParse(timeStr, out var parsedDuration))
                {
                    duration = parsedDuration;
                }
                continue;
            }

            // Parse individual test results - try multiple patterns
            var testResultPatterns = new[]
            {
                // VSTest format: "  Passed   TestNamespace.TestClass.TestMethod [< 1 ms]"
                new Regex(@"^\s*(Passed|Failed|Skipped)\s+([A-Za-z_][A-Za-z0-9_\.]*)\s*\[([^\]]+)\]", RegexOptions.IgnoreCase),
                // Alternative format: "✓ TestNamespace.TestClass.TestMethod (26ms)"
                new Regex(@"^\s*[✓×✗-]\s*([A-Za-z_][A-Za-z0-9_\.]*)\s*\(([^)]+)\)", RegexOptions.IgnoreCase),
                // Simple format: "TestNamespace.TestClass.TestMethod: Passed"
                new Regex(@"^([A-Za-z_][A-Za-z0-9_\.]*)\s*:\s*(Passed|Failed|Skipped)", RegexOptions.IgnoreCase),
                // NUnit format: "TestNamespace.TestClass.TestMethod ... Passed"
                new Regex(@"^([A-Za-z_][A-Za-z0-9_\.]*)\s*\.\.\.\s*(Passed|Failed|Skipped)", RegexOptions.IgnoreCase)
            };

            Match? testResultMatch = null;
            int patternIndex = -1;
            for (int p = 0; p < testResultPatterns.Length; p++)
            {
                testResultMatch = testResultPatterns[p].Match(line);
                if (testResultMatch.Success)
                {
                    patternIndex = p;
                    break;
                }
            }

            if (testResultMatch != null && testResultMatch.Success)
            {
                // Save previous test if we have one
                if (currentTest != null && !string.IsNullOrEmpty(currentTest.Name))
                {
                    testDetails.Add(currentTest);
                }

                string testOutcome;
                string testFullName;
                string durationStr = "";

                // Extract based on which pattern matched
                switch (patternIndex)
                {
                    case 0: // VSTest format: "  Passed   TestNamespace.TestClass.TestMethod [< 1 ms]"
                        testOutcome = testResultMatch.Groups[1].Value;
                        testFullName = testResultMatch.Groups[2].Value;
                        durationStr = testResultMatch.Groups[3].Value;
                        break;
                    case 1: // Alternative format: "✓ TestNamespace.TestClass.TestMethod (26ms)"
                        testFullName = testResultMatch.Groups[1].Value;
                        durationStr = testResultMatch.Groups[2].Value;
                        testOutcome = line.Contains("✓") ? "Passed" : "Failed";
                        break;
                    case 2: // Simple format: "TestNamespace.TestClass.TestMethod: Passed"
                        testFullName = testResultMatch.Groups[1].Value;
                        testOutcome = testResultMatch.Groups[2].Value;
                        break;
                    case 3: // NUnit format: "TestNamespace.TestClass.TestMethod ... Passed"
                        testFullName = testResultMatch.Groups[1].Value;
                        testOutcome = testResultMatch.Groups[2].Value;
                        break;
                    default:
                        continue; // Skip if no pattern matched
                }

                _logger.LogDebug("Parsed test: {TestName} = {Result}, Pattern: {Pattern}", testFullName, testOutcome, patternIndex);

                // Parse test duration
                var testDuration = 0.0;
                if (!string.IsNullOrEmpty(durationStr))
                {
                    var durationMatch = Regex.Match(durationStr, @"([0-9.,]+)\s*ms");
                    if (durationMatch.Success && double.TryParse(durationMatch.Groups[1].Value.Replace(",", "."), out var ms))
                    {
                        testDuration = ms / 1000.0;
                    }
                }

                // Extract class and method names
                var className = "Unknown";
                var methodName = testFullName;
                if (testFullName.Contains("."))
                {
                    var lastDotIndex = testFullName.LastIndexOf('.');
                    className = testFullName.Substring(0, lastDotIndex);
                    methodName = testFullName.Substring(lastDotIndex + 1);
                }

                currentTest = new TestDetail
                {
                    Name = methodName,
                    ClassName = className,
                    Result = testOutcome,
                    Duration = testDuration,
                    ErrorMessage = null,
                    StackTrace = null
                };

                inFailureDetails = false;
                errorMessageLines.Clear();
                stackTraceLines.Clear();
                continue;
            }

            // Check for failure details section
            if (line.Contains("Failed") && line.Contains(":"))
            {
                // Look for pattern like "Failed   TestNamespace.TestClass.TestMethod:"
                var failedTestMatch = Regex.Match(line, @"Failed\s+([A-Za-z_][A-Za-z0-9_\.]*):?");
                if (failedTestMatch.Success)
                {
                    inFailureDetails = true;
                    errorMessageLines.Clear();
                    stackTraceLines.Clear();
                    continue;
                }
            }

            // Collect error details when in failure section
            if (inFailureDetails)
            {
                // Skip empty lines and test result headers
                if (string.IsNullOrWhiteSpace(line) || 
                    line.StartsWith("Test Run") || 
                    line.StartsWith("Total tests:") ||
                    line.StartsWith("Passed:") ||
                    line.StartsWith("Results File:"))
                {
                    continue;
                }

                // Detect stack trace (lines starting with "at" or containing file paths)
                if (line.StartsWith("at ") || line.Contains(".cs:line") || line.Contains("in ") && line.Contains(":line"))
                {
                    stackTraceLines.Add(line);
                }
                else
                {
                    // Everything else is error message
                    errorMessageLines.Add(line);
                }

                // End of failure details when we hit another test or summary
                if (i < lines.Length - 1)
                {
                    var nextLine = lines[i + 1].Trim();
                    if (Regex.IsMatch(nextLine, @"^\s*(Passed|Failed|Skipped)\s+") || 
                        nextLine.Contains("Total tests:") ||
                        nextLine.Contains("Test Run"))
                    {
                        // Update the current test with error details
                        if (currentTest != null && currentTest.Result == "Failed")
                        {
                            currentTest.ErrorMessage = string.Join("\n", errorMessageLines).Trim();
                            currentTest.StackTrace = string.Join("\n", stackTraceLines).Trim();
                        }
                        inFailureDetails = false;
                    }
                }
            }
        }

        // Add the last test if we have one
        if (currentTest != null && !string.IsNullOrEmpty(currentTest.Name))
        {
            // Update with any remaining error details
            if (currentTest.Result == "Failed" && (errorMessageLines.Count > 0 || stackTraceLines.Count > 0))
            {
                currentTest.ErrorMessage = string.Join("\n", errorMessageLines).Trim();
                currentTest.StackTrace = string.Join("\n", stackTraceLines).Trim();
            }
            testDetails.Add(currentTest);
        }

        // If we couldn't parse individual tests but have summary info, create generic entries
        if (testDetails.Count == 0 && totalTests > 0)
        {
            for (int i = 0; i < passedTests; i++)
            {
                testDetails.Add(new TestDetail
                {
                    Name = $"PassedTest_{i + 1}",
                    ClassName = "Unknown",
                    Result = "Passed",
                    Duration = 0
                });
            }
            for (int i = 0; i < failedTests; i++)
            {
                testDetails.Add(new TestDetail
                {
                    Name = $"FailedTest_{i + 1}",
                    ClassName = "Unknown",
                    Result = "Failed",
                    Duration = 0
                });
            }
            for (int i = 0; i < skippedTests; i++)
            {
                testDetails.Add(new TestDetail
                {
                    Name = $"SkippedTest_{i + 1}",
                    ClassName = "Unknown",
                    Result = "Skipped",
                    Duration = 0
                });
            }
        }

        var result = new TestResult
        {
            TotalTests = totalTests,
            PassedTests = passedTests,
            FailedTests = failedTests,
            SkippedTests = skippedTests,
            Duration = duration,
            TestDetails = testDetails,
            Output = output
        };

        // If we didn't parse any tests, fall back to the generic parser
        if (testDetails.Count == 0 && output.Contains("Test"))
        {
            _logger.LogDebug("VSTest parsing found no tests, falling back to generic parser");
            return ParseDotNetTestOutput(output);
        }

        _logger.LogDebug("VSTest parsing found {Count} test details", testDetails.Count);
        return result;
    }

    private async Task<TestResult> ParseTrxFileAsync(string trxFilePath, string consoleOutput)
    {
        try
        {
            if (!File.Exists(trxFilePath))
            {
                _logger.LogWarning("TRX file not found, falling back to console output parsing: {TrxPath}", trxFilePath);
                return ParseVSTestOutput(consoleOutput);
            }

            _logger.LogDebug("Parsing TRX file: {TrxPath}", trxFilePath);
            var trxContent = await File.ReadAllTextAsync(trxFilePath);
            var doc = XDocument.Parse(trxContent);

            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            var testDetails = new List<TestDetail>();

            // Parse test definitions to get class and method names
            var unitTests = doc.Descendants(ns + "UnitTest").ToList();
            var testMethods = doc.Descendants(ns + "TestMethod").ToList();

            // Parse test results
            var testResults = doc.Descendants(ns + "UnitTestResult").ToList();

            foreach (var testResult in testResults)
            {
                var testId = testResult.Attribute("testId")?.Value;
                var testName = testResult.Attribute("testName")?.Value ?? "Unknown";
                var outcome = testResult.Attribute("outcome")?.Value ?? "Unknown";
                var duration = testResult.Attribute("duration")?.Value;
                var errorInfo = testResult.Element(ns + "Output")?.Element(ns + "ErrorInfo");

                // Find the corresponding test definition for class name
                var unitTest = unitTests.FirstOrDefault(ut => ut.Attribute("id")?.Value == testId);
                var testMethod = testMethods.FirstOrDefault(tm => tm.Attribute("name")?.Value == testName);

                var className = "Unknown";
                var methodName = testName;

                // Try to extract class name from test method
                if (testMethod != null)
                {
                    className = testMethod.Attribute("className")?.Value ?? "Unknown";
                }
                else if (unitTest != null)
                {
                    // Try to extract from unit test name
                    var fullName = unitTest.Attribute("name")?.Value;
                    if (!string.IsNullOrEmpty(fullName) && fullName.Contains("."))
                    {
                        var lastDotIndex = fullName.LastIndexOf('.');
                        className = fullName.Substring(0, lastDotIndex);
                        methodName = fullName.Substring(lastDotIndex + 1);
                    }
                }

                // Parse duration
                var testDuration = 0.0;
                if (!string.IsNullOrEmpty(duration) && TimeSpan.TryParse(duration, out var timeSpan))
                {
                    testDuration = timeSpan.TotalSeconds;
                }

                // Parse error details
                string? errorMessage = null;
                string? stackTrace = null;

                if (errorInfo != null)
                {
                    var message = errorInfo.Element(ns + "Message")?.Value;
                    var stackTraceElement = errorInfo.Element(ns + "StackTrace")?.Value;

                    errorMessage = message?.Trim();
                    stackTrace = stackTraceElement?.Trim();
                }

                // Map VSTest outcomes to our format
                var mappedResult = outcome switch
                {
                    "Passed" => "Passed",
                    "Failed" => "Failed",
                    "Skipped" or "NotExecuted" => "Skipped",
                    _ => "Failed"
                };

                testDetails.Add(new TestDetail
                {
                    Name = methodName,
                    ClassName = className,
                    Result = mappedResult,
                    Duration = testDuration,
                    ErrorMessage = errorMessage,
                    StackTrace = stackTrace
                });

                _logger.LogDebug("Parsed TRX test: {ClassName}.{MethodName} = {Result}", className, methodName, mappedResult);
            }

            // Calculate summary stats
            var totalTests = testDetails.Count;
            var passedTests = testDetails.Count(t => t.Result == "Passed");
            var failedTests = testDetails.Count(t => t.Result == "Failed");
            var skippedTests = testDetails.Count(t => t.Result == "Skipped");

            // Try to get total duration from TRX file
            var totalDuration = 0.0;
            var times = doc.Descendants(ns + "Times").FirstOrDefault();
            if (times != null)
            {
                var finish = times.Attribute("finish")?.Value;
                var start = times.Attribute("start")?.Value;
                if (!string.IsNullOrEmpty(finish) && !string.IsNullOrEmpty(start))
                {
                    if (DateTime.TryParse(finish, out var finishTime) && DateTime.TryParse(start, out var startTime))
                    {
                        totalDuration = (finishTime - startTime).TotalSeconds;
                    }
                }
            }

            // If no duration from times, sum individual test durations
            if (totalDuration == 0.0)
            {
                totalDuration = testDetails.Sum(t => t.Duration);
            }

            _logger.LogInformation("Successfully parsed TRX file: {TotalTests} tests, {PassedTests} passed, {FailedTests} failed", 
                totalTests, passedTests, failedTests);

            return new TestResult
            {
                TotalTests = totalTests,
                PassedTests = passedTests,
                FailedTests = failedTests,
                SkippedTests = skippedTests,
                Duration = totalDuration,
                TestDetails = testDetails,
                Output = consoleOutput
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse TRX file, falling back to console output: {TrxPath}", trxFilePath);
            return ParseVSTestOutput(consoleOutput);
        }
    }

    private TestDetail? ParseTestDetailFromMatch(Match match, string line)
    {
        try
        {
            string result;
            string testFullName;

            if (match.Groups.Count >= 3 && !string.IsNullOrEmpty(match.Groups[1].Value))
            {
                // Format: "Passed TestNamespace.TestClass.TestMethod [< 1 ms]"
                result = match.Groups[1].Value;
                testFullName = match.Groups[2].Value;
            }
            else if (match.Groups.Count >= 2)
            {
                // Infer result from symbols or context
                if (line.Contains("✓") || line.Contains("ok"))
                    result = "Passed";
                else if (line.Contains("×") || line.Contains("✗") || line.Contains("FAILED"))
                    result = "Failed";
                else if (line.Contains("Skipped") || line.Contains("skipped"))
                    result = "Skipped";
                else
                    result = "Unknown";

                testFullName = match.Groups[1].Value;
            }
            else
            {
                return null;
            }

            // Parse the full test name to extract class and method
            var className = "Unknown";
            var methodName = testFullName;

            if (testFullName.Contains("."))
            {
                var lastDotIndex = testFullName.LastIndexOf('.');
                className = testFullName.Substring(0, lastDotIndex);
                methodName = testFullName.Substring(lastDotIndex + 1);
            }

            // Try to extract duration from the line
            var duration = 0.0;
            var durationMatch = Regex.Match(line, @"\[.*?(\d+(?:\.\d+)?)\s*ms.*?\]");
            if (durationMatch.Success)
            {
                if (double.TryParse(durationMatch.Groups[1].Value, out var ms))
                {
                    duration = ms / 1000.0; // Convert to seconds
                }
            }

            return new TestDetail
            {
                Name = methodName,
                ClassName = className,
                Result = result,
                Duration = duration,
                ErrorMessage = null,
                StackTrace = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to parse test detail from line: {Line}, Error: {Error}", line, ex.Message);
            return null;
        }
    }
}