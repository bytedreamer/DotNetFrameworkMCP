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
                }
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

        // Build the project first to get test assembly
        var projectDir = Path.GetDirectoryName(projectPath);
        var assemblyPattern = Path.Combine(projectDir!, "bin", "**", "*.dll");
        var assemblyFiles = Directory.GetFiles(Path.Combine(projectDir!, "bin"), "*.dll", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).Contains(Path.GetFileNameWithoutExtension(projectPath)))
            .ToList();

        if (!assemblyFiles.Any())
        {
            throw new InvalidOperationException($"No test assemblies found for project {projectPath}. Please build the project first.");
        }

        var testAssembly = assemblyFiles.First();
        var args = new StringBuilder($"\"{testAssembly}\"");

        if (!string.IsNullOrEmpty(filter))
        {
            args.Append($" --TestCaseFilter:\"{filter}\"");
        }

        if (verbose)
        {
            args.Append(" --logger:console;verbosity=detailed");
        }

        args.Append(" --logger:trx");

        var result = await RunProcessAsync(vstestPath, args.ToString(), cancellationToken);
        return ParseVSTestOutput(result.Output);
    }

    private async Task<TestResult> RunMSTestAsync(string projectPath, string? filter, bool verbose, CancellationToken cancellationToken)
    {
        // For MSTest, we'll use dotnet test which works well with MSTest projects
        return await RunDotNetTestAsync(projectPath, filter, verbose, cancellationToken);
    }

    private async Task<TestResult> RunNUnitAsync(string projectPath, string? filter, bool verbose, CancellationToken cancellationToken)
    {
        // For NUnit, we'll also use dotnet test which has good NUnit support
        return await RunDotNetTestAsync(projectPath, filter, verbose, cancellationToken);
    }

    private async Task<TestResult> RunXUnitAsync(string projectPath, string? filter, bool verbose, CancellationToken cancellationToken)
    {
        // For xUnit, we'll also use dotnet test which has excellent xUnit support
        return await RunDotNetTestAsync(projectPath, filter, verbose, cancellationToken);
    }

    private async Task<TestResult> RunDotNetTestAsync(string projectPath, string? filter, bool verbose, CancellationToken cancellationToken)
    {
        var args = new StringBuilder($"test \"{projectPath}\"");

        if (!string.IsNullOrEmpty(filter))
        {
            args.Append($" --filter \"{filter}\"");
        }

        if (verbose)
        {
            args.Append(" --verbosity detailed");
        }

        args.Append(" --logger trx --no-build --no-restore");

        var result = await RunProcessAsync("dotnet", args.ToString(), cancellationToken);
        return ParseDotNetTestOutput(result.Output);
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

        // Look for summary line like: "Total tests: 5. Passed: 4. Failed: 1. Skipped: 0."
        var summaryRegex = new Regex(@"Total tests:\s*(\d+)\.\s*Passed:\s*(\d+)\.\s*Failed:\s*(\d+)\.\s*Skipped:\s*(\d+)\.");
        var timeRegex = new Regex(@"Test Run Successful\.\s*Total tests:\s*\d+\s*Passed:\s*\d+\s*Total time:\s*([0-9.,]+)\s*Seconds");

        foreach (var line in lines)
        {
            var summaryMatch = summaryRegex.Match(line);
            if (summaryMatch.Success)
            {
                totalTests = int.Parse(summaryMatch.Groups[1].Value);
                passedTests = int.Parse(summaryMatch.Groups[2].Value);
                failedTests = int.Parse(summaryMatch.Groups[3].Value);
                skippedTests = int.Parse(summaryMatch.Groups[4].Value);
            }

            var timeMatch = timeRegex.Match(line);
            if (timeMatch.Success)
            {
                if (double.TryParse(timeMatch.Groups[1].Value.Replace(",", "."), out var parsedDuration))
                {
                    duration = parsedDuration;
                }
            }

            // Parse individual test results
            if (line.Contains("Passed") || line.Contains("Failed") || line.Contains("Skipped"))
            {
                var testDetail = ParseTestDetailFromLine(line);
                if (testDetail != null)
                {
                    testDetails.Add(testDetail);
                }
            }
        }

        return new TestResult
        {
            TotalTests = totalTests,
            PassedTests = passedTests,
            FailedTests = failedTests,
            SkippedTests = skippedTests,
            Duration = duration,
            TestDetails = testDetails
        };
    }

    private TestResult ParseVSTestOutput(string output)
    {
        // Similar parsing logic for VSTest output
        // For now, delegate to dotnet test parsing as the format is similar
        return ParseDotNetTestOutput(output);
    }

    private TestDetail? ParseTestDetailFromLine(string line)
    {
        // Parse test details from output lines
        // This is a simplified parser - could be enhanced for more detailed parsing
        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length < 2) return null;

        var result = "Unknown";
        if (line.Contains("Passed")) result = "Passed";
        else if (line.Contains("Failed")) result = "Failed";
        else if (line.Contains("Skipped")) result = "Skipped";

        // Extract test name (this is simplified - real parsing would be more sophisticated)
        var testName = parts.LastOrDefault(p => p.Contains(".")) ?? "Unknown";
        var className = testName.Contains(".") ? testName.Substring(0, testName.LastIndexOf('.')) : "Unknown";
        var methodName = testName.Contains(".") ? testName.Substring(testName.LastIndexOf('.') + 1) : testName;

        return new TestDetail
        {
            Name = methodName,
            ClassName = className,
            Result = result,
            Duration = 0, // Could be extracted from more detailed output
            ErrorMessage = null,
            StackTrace = null
        };
    }
}