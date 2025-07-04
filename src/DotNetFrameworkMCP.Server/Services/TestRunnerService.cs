using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
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

        if (verbose)
        {
            args.Append(" /logger:console;verbosity=detailed");
        }

        // Try to find test adapters for better framework support
        var testAdapterPath = FindTestAdapterPath(projectPath);
        if (!string.IsNullOrEmpty(testAdapterPath))
        {
            args.Append($" /TestAdapterPath:\"{testAdapterPath}\"");
            _logger.LogDebug("Using test adapter path: {TestAdapterPath}", testAdapterPath);
        }

        args.Append(" /logger:trx");

        var result = await RunProcessAsync(vstestPath, args.ToString(), cancellationToken);
        return ParseVSTestOutput(result.Output);
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
        var possiblePaths = new[]
        {
            @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
            @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("Found VSTest.Console.exe at: {Path}", path);
                return path;
            }
        }

        _logger.LogWarning("VSTest.Console.exe not found in standard locations");
        return null;
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

        // Look for MSBuild.exe in standard Visual Studio locations
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var possiblePaths = new[]
        {
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFiles, @"Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
            // Legacy paths
            Path.Combine(programFilesX86, @"MSBuild\14.0\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"MSBuild\15.0\Bin\MSBuild.exe"),
            @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe",
            @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("Found MSBuild.exe at: {Path}", path);
                return path;
            }
        }

        _logger.LogWarning("MSBuild.exe not found in standard locations");
        return null;
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
        // Similar parsing logic for VSTest output
        // For now, delegate to dotnet test parsing as the format is similar
        return ParseDotNetTestOutput(output);
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