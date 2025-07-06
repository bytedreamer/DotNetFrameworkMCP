using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Executors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DotNetFrameworkMCP.Server.Tests.Executors;

public class ExecutorFactoryTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Mock<IOptions<McpServerConfiguration>> _mockOptions;
    private readonly McpServerConfiguration _configuration;

    public ExecutorFactoryTests()
    {
        _configuration = new McpServerConfiguration();
        _mockOptions = new Mock<IOptions<McpServerConfiguration>>();
        _mockOptions.Setup(x => x.Value).Returns(_configuration);

        var services = new ServiceCollection();
        
        // Register executors
        services.AddSingleton<MSBuildExecutor>();
        services.AddSingleton<DotNetBuildExecutor>();
        services.AddSingleton<VSTestExecutor>();
        services.AddSingleton<DotNetTestExecutor>();
        
        // Register configuration
        services.AddSingleton(_mockOptions.Object);
        
        // Register loggers
        services.AddSingleton<ILogger<MSBuildExecutor>>(Mock.Of<ILogger<MSBuildExecutor>>());
        services.AddSingleton<ILogger<DotNetBuildExecutor>>(Mock.Of<ILogger<DotNetBuildExecutor>>());
        services.AddSingleton<ILogger<VSTestExecutor>>(Mock.Of<ILogger<VSTestExecutor>>());
        services.AddSingleton<ILogger<DotNetTestExecutor>>(Mock.Of<ILogger<DotNetTestExecutor>>());

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void CreateBuildExecutor_WhenUseDotNetCliIsFalse_ReturnsMSBuildExecutor()
    {
        // Arrange
        _configuration.UseDotNetCli = false;
        var factory = new ExecutorFactory(_serviceProvider, _mockOptions.Object);

        // Act
        var executor = factory.CreateBuildExecutor();

        // Assert
        Assert.IsType<MSBuildExecutor>(executor);
    }

    [Fact]
    public void CreateBuildExecutor_WhenUseDotNetCliIsTrue_ReturnsDotNetBuildExecutor()
    {
        // Arrange
        _configuration.UseDotNetCli = true;
        var factory = new ExecutorFactory(_serviceProvider, _mockOptions.Object);

        // Act
        var executor = factory.CreateBuildExecutor();

        // Assert
        Assert.IsType<DotNetBuildExecutor>(executor);
    }

    [Fact]
    public void CreateTestExecutor_WhenUseDotNetCliIsFalse_ReturnsVSTestExecutor()
    {
        // Arrange
        _configuration.UseDotNetCli = false;
        var factory = new ExecutorFactory(_serviceProvider, _mockOptions.Object);

        // Act
        var executor = factory.CreateTestExecutor();

        // Assert
        Assert.IsType<VSTestExecutor>(executor);
    }

    [Fact]
    public void CreateTestExecutor_WhenUseDotNetCliIsTrue_ReturnsDotNetTestExecutor()
    {
        // Arrange
        _configuration.UseDotNetCli = true;
        var factory = new ExecutorFactory(_serviceProvider, _mockOptions.Object);

        // Act
        var executor = factory.CreateTestExecutor();

        // Assert
        Assert.IsType<DotNetTestExecutor>(executor);
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var factory = new ExecutorFactory(_serviceProvider, _mockOptions.Object);

        // Assert
        Assert.NotNull(factory);
    }
}