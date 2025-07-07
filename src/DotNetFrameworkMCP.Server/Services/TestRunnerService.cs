using DotNetFrameworkMCP.Server.Executors;
using DotNetFrameworkMCP.Server.Models;
using Microsoft.Extensions.Logging;

namespace DotNetFrameworkMCP.Server.Services;

public interface ITestRunnerService
{
    Task<TestResult> RunTestsAsync(string projectPath, string? filter, bool verbose, CancellationToken cancellationToken = default);
}

public class TestRunnerService : ITestRunnerService
{
    private readonly ILogger<TestRunnerService> _logger;
    private readonly IExecutorFactory _executorFactory;

    public TestRunnerService(
        ILogger<TestRunnerService> logger,
        IExecutorFactory executorFactory)
    {
        _logger = logger;
        _executorFactory = executorFactory;
    }

    public async Task<TestResult> RunTestsAsync(
        string projectPath,
        string? filter,
        bool verbose,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting test run for project: {ProjectPath}", projectPath);
            
            var testExecutor = _executorFactory.CreateTestExecutor();
            return await testExecutor.ExecuteTestsAsync(projectPath, filter, verbose, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create test executor or execute tests for project: {ProjectPath}", projectPath);
            
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
                        Name = "Test Execution Error",
                        ClassName = "System",
                        Result = "Failed",
                        Duration = 0,
                        ErrorMessage = ex.Message,
                        StackTrace = ex.StackTrace
                    }
                },
                Output = $"Test service failed: {ex.Message}"
            };
        }
    }
}