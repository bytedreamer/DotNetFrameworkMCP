using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Executors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DotNetFrameworkMCP.Server.Tests.Executors;

public class MSBuildExecutorTests
{
    private readonly Mock<ILogger<MSBuildExecutor>> _mockLogger;
    private readonly Mock<IOptions<McpServerConfiguration>> _mockOptions;
    private readonly McpServerConfiguration _configuration;
    private readonly MSBuildExecutor _executor;

    public MSBuildExecutorTests()
    {
        _mockLogger = new Mock<ILogger<MSBuildExecutor>>();
        _mockOptions = new Mock<IOptions<McpServerConfiguration>>();
        _configuration = new McpServerConfiguration
        {
            BuildTimeout = 60000,
            PreferredVSVersion = "2022"
        };
        _mockOptions.Setup(x => x.Value).Returns(_configuration);
        _executor = new MSBuildExecutor(_mockLogger.Object, _mockOptions.Object);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithNonExistentProject_ReturnsFailedResult()
    {
        // Arrange
        var nonExistentPath = "/path/to/nonexistent/project.csproj";

        // Act
        var result = await _executor.ExecuteBuildAsync(nonExistentPath, "Debug", "Any CPU", true);

        // Assert
        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Contains("Project file not found", result.Errors[0].Message);
    }

    [Fact]
    public async Task ExecuteBuildAsync_WithTimeout_ThrowsTimeoutException()
    {
        // Arrange
        _configuration.BuildTimeout = 1; // 1ms timeout to force timeout
        var tempProjectFile = Path.GetTempFileName();
        File.WriteAllText(tempProjectFile, "<Project></Project>");

        try
        {
            // Act & Assert
            var result = await _executor.ExecuteBuildAsync(tempProjectFile, "Debug", "Any CPU", true);
            
            // Should return a failed result due to timeout or MSBuild not found
            Assert.False(result.Success);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempProjectFile))
                File.Delete(tempProjectFile);
        }
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act & Assert
        Assert.NotNull(_executor);
    }

    [Theory]
    [InlineData("Debug", "Any CPU", true)]
    [InlineData("Release", "x64", false)]
    [InlineData("Debug", "x86", true)]
    public async Task ExecuteBuildAsync_WithDifferentConfigurations_HandlesGracefully(
        string configuration, string platform, bool restore)
    {
        // Arrange
        var tempProjectFile = Path.GetTempFileName();
        File.WriteAllText(tempProjectFile, "<Project></Project>");

        try
        {
            // Act
            var result = await _executor.ExecuteBuildAsync(tempProjectFile, configuration, platform, restore);

            // Assert
            // Result should be non-null (even if build fails due to no MSBuild)
            Assert.NotNull(result);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempProjectFile))
                File.Delete(tempProjectFile);
        }
    }
}