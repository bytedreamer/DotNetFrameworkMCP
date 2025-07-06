using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetFrameworkMCP.Server.Executors;

/// <summary>
/// Base class for test executors with common functionality
/// </summary>
public abstract class BaseTestExecutor : ITestExecutor
{
    protected readonly ILogger _logger;
    protected readonly McpServerConfiguration _configuration;

    protected BaseTestExecutor(ILogger logger, IOptions<McpServerConfiguration> configuration)
    {
        _logger = logger;
        _configuration = configuration.Value;
    }

    public abstract Task<TestResult> ExecuteTestsAsync(
        string projectPath,
        string? filter,
        bool verbose,
        CancellationToken cancellationToken = default);

    protected async Task<(string Output, int ExitCode)> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
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
                output.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for completion with timeout and cancellation support
        var timeoutMs = _configuration.TestTimeout;
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(combinedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            _logger.LogWarning("Test execution timed out after {TimeoutMs}ms, killing process", timeoutMs);
            process.Kill();
            throw new TimeoutException($"Test execution timed out after {timeoutMs}ms");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Test execution cancelled by user, killing process");
            process.Kill();
            throw;
        }

        return (output.ToString(), process.ExitCode);
    }

    protected async Task<TestResult> ParseTrxFileAsync(string trxFilePath, string consoleOutput)
    {
        var testDetails = new List<TestDetail>();

        try
        {
            if (File.Exists(trxFilePath))
            {
                var trxContent = await File.ReadAllTextAsync(trxFilePath);
                _logger.LogDebug("Parsing TRX file: {TrxFilePath}", trxFilePath);

                var doc = XDocument.Parse(trxContent);
                var ns = doc.Root?.GetDefaultNamespace();

                if (ns != null)
                {
                    // Parse test results
                    var unitTestResults = doc.Descendants(ns + "UnitTestResult");

                    foreach (var result in unitTestResults)
                    {
                        var testId = result.Attribute("testId")?.Value;
                        var testName = result.Attribute("testName")?.Value ?? "Unknown Test";
                        var outcome = result.Attribute("outcome")?.Value ?? "Unknown";
                        var duration = result.Attribute("duration")?.Value;

                        // Try to find the test definition to get class information
                        var className = "Unknown";
                        if (!string.IsNullOrEmpty(testId))
                        {
                            var testDefinition = doc.Descendants(ns + "UnitTest")
                                .FirstOrDefault(t => t.Attribute("id")?.Value == testId);

                            if (testDefinition != null)
                            {
                                var testMethod = testDefinition.Descendants(ns + "TestMethod").FirstOrDefault();
                                className = testMethod?.Attribute("className")?.Value ?? "Unknown";
                            }
                        }

                        // Parse duration
                        var durationSeconds = 0.0;
                        if (!string.IsNullOrEmpty(duration) && TimeSpan.TryParse(duration, out var durationTimeSpan))
                        {
                            durationSeconds = durationTimeSpan.TotalSeconds;
                        }

                        // Get error message and stack trace for failed tests
                        string? errorMessage = null;
                        string? stackTrace = null;

                        if (outcome == "Failed")
                        {
                            var output = result.Element(ns + "Output");
                            var errorInfo = output?.Element(ns + "ErrorInfo");
                            errorMessage = errorInfo?.Element(ns + "Message")?.Value;
                            stackTrace = errorInfo?.Element(ns + "StackTrace")?.Value;
                        }

                        testDetails.Add(new TestDetail
                        {
                            Name = testName,
                            ClassName = className,
                            Result = outcome,
                            Duration = durationSeconds,
                            ErrorMessage = errorMessage,
                            StackTrace = stackTrace
                        });
                    }
                }
            }

            // If no tests were parsed from TRX, fall back to console output parsing
            if (testDetails.Count == 0)
            {
                _logger.LogWarning("No tests found in TRX file, falling back to console output parsing");
                ParseConsoleOutput(consoleOutput, testDetails);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing TRX file: {TrxFilePath}", trxFilePath);
            // Fall back to console output parsing
            ParseConsoleOutput(consoleOutput, testDetails);
        }

        return new TestResult
        {
            TotalTests = testDetails.Count,
            PassedTests = testDetails.Count(t => t.Result == "Passed"),
            FailedTests = testDetails.Count(t => t.Result == "Failed"),
            SkippedTests = testDetails.Count(t => t.Result == "Skipped"),
            TestDetails = testDetails,
            Output = consoleOutput
        };
    }

    private void ParseConsoleOutput(string output, List<TestDetail> testDetails)
    {
        if (string.IsNullOrEmpty(output))
            return;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            // Simple pattern matching for common test output formats
            if (line.Contains("Passed") || line.Contains("Failed") || line.Contains("Skipped"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var result = parts.FirstOrDefault(p => p == "Passed" || p == "Failed" || p == "Skipped");
                    if (result != null)
                    {
                        testDetails.Add(new TestDetail
                        {
                            Name = line.Trim(),
                            ClassName = "ParsedFromConsole",
                            Result = result,
                            Duration = 0,
                            ErrorMessage = result == "Failed" ? "Failed (parsed from console output)" : null,
                            StackTrace = null
                        });
                    }
                }
            }
        }
    }
}