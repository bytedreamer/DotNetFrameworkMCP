using DotNetFrameworkMCP.Server.Models;

namespace DotNetFrameworkMCP.Server.Executors;

/// <summary>
/// Interface for build execution strategies
/// </summary>
public interface IBuildExecutor
{
    /// <summary>
    /// Executes a build for the specified project
    /// </summary>
    /// <param name="projectPath">Path to the project or solution file</param>
    /// <param name="configuration">Build configuration (e.g., Debug, Release)</param>
    /// <param name="platform">Target platform (e.g., Any CPU, x86, x64)</param>
    /// <param name="restore">Whether to restore NuGet packages</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Build result with success status, errors, warnings, and output</returns>
    Task<BuildResult> ExecuteBuildAsync(
        string projectPath,
        string configuration,
        string platform,
        bool restore,
        CancellationToken cancellationToken = default);
}