using DotNetFrameworkMCP.Server.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotNetFrameworkMCP.Server.Executors;

/// <summary>
/// Factory for creating build and test executors based on configuration
/// </summary>
public interface IExecutorFactory
{
    IBuildExecutor CreateBuildExecutor();
    ITestExecutor CreateTestExecutor();
}

public class ExecutorFactory : IExecutorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly McpServerConfiguration _configuration;

    public ExecutorFactory(IServiceProvider serviceProvider, IOptions<McpServerConfiguration> configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration.Value;
    }

    public IBuildExecutor CreateBuildExecutor()
    {
        return _configuration.UseDotNetCli
            ? _serviceProvider.GetRequiredService<DotNetBuildExecutor>()
            : _serviceProvider.GetRequiredService<MSBuildExecutor>();
    }

    public ITestExecutor CreateTestExecutor()
    {
        return _configuration.UseDotNetCli
            ? _serviceProvider.GetRequiredService<DotNetTestExecutor>()
            : _serviceProvider.GetRequiredService<VSTestExecutor>();
    }
}