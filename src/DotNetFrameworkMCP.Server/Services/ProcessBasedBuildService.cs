using DotNetFrameworkMCP.Server.Executors;
using DotNetFrameworkMCP.Server.Models;
using Microsoft.Extensions.Logging;

namespace DotNetFrameworkMCP.Server.Services;

public interface IProcessBasedBuildService
{
    Task<Models.BuildResult> BuildProjectAsync(string projectPath, string configuration, string platform, bool restore, CancellationToken cancellationToken = default);
}

public class ProcessBasedBuildService : IProcessBasedBuildService
{
    private readonly ILogger<ProcessBasedBuildService> _logger;
    private readonly IExecutorFactory _executorFactory;

    public ProcessBasedBuildService(
        ILogger<ProcessBasedBuildService> logger,
        IExecutorFactory executorFactory)
    {
        _logger = logger;
        _executorFactory = executorFactory;
    }

    public async Task<Models.BuildResult> BuildProjectAsync(
        string projectPath,
        string configuration,
        string platform,
        bool restore,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting build for project: {ProjectPath}", projectPath);
            
            var buildExecutor = _executorFactory.CreateBuildExecutor();
            return await buildExecutor.ExecuteBuildAsync(projectPath, configuration, platform, restore, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create build executor or execute build for project: {ProjectPath}", projectPath);
            
            return new Models.BuildResult
            {
                Success = false,
                Errors = new List<BuildMessage>
                {
                    new BuildMessage
                    {
                        Message = ex.Message,
                        File = projectPath
                    }
                },
                Warnings = new List<BuildMessage>(),
                BuildTime = 0,
                Output = $"Build service failed: {ex.Message}"
            };
        }
    }
}