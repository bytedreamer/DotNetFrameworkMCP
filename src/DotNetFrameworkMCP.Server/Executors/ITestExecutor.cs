using DotNetFrameworkMCP.Server.Models;

namespace DotNetFrameworkMCP.Server.Executors;

/// <summary>
/// Interface for test execution strategies
/// </summary>
public interface ITestExecutor
{
    /// <summary>
    /// Executes tests for the specified project
    /// </summary>
    /// <param name="projectPath">Path to the test project</param>
    /// <param name="filter">Optional test filter expression</param>
    /// <param name="verbose">Whether to enable verbose output</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Test result with total, passed, failed, skipped counts and details</returns>
    Task<TestResult> ExecuteTestsAsync(
        string projectPath,
        string? filter,
        bool verbose,
        CancellationToken cancellationToken = default);
}