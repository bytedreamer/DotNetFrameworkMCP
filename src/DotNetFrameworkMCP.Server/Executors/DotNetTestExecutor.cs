using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetFrameworkMCP.Server.Executors;

/// <summary>
/// dotnet test-based test executor
/// </summary>
public class DotNetTestExecutor : BaseTestExecutor
{
    public DotNetTestExecutor(
        ILogger<DotNetTestExecutor> logger,
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
}