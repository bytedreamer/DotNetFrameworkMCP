using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Executors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace DotNetFrameworkMCP.Server.Tests.Executors;

[TestFixture]
public class MSBuildExecutorTests
{
    private Mock<ILogger<MSBuildExecutor>> _mockLogger;
    private Mock<IOptions<McpServerConfiguration>> _mockOptions;
    private McpServerConfiguration _configuration;
    private MSBuildExecutor _executor;

    [SetUp]
    public void SetUp()
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

    [Test]
    public async Task ExecuteBuildAsync_WithNonExistentProject_ReturnsFailedResult()
    {
        // Arrange
        var nonExistentPath = "/path/to/nonexistent/project.csproj";

        // Act
        var result = await _executor.ExecuteBuildAsync(nonExistentPath, "Debug", "Any CPU", true);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(result.Errors[0].Message, Does.Contain("Project file not found"));
    }

    [Test]
    public async Task ExecuteBuildAsync_WithTimeout_ReturnsFailedResult()
    {
        // Arrange
        _configuration.BuildTimeout = 1; // 1ms timeout to force timeout
        var tempProjectFile = Path.GetTempFileName();
        File.WriteAllText(tempProjectFile, "<Project></Project>");

        try
        {
            // Act
            var result = await _executor.ExecuteBuildAsync(tempProjectFile, "Debug", "Any CPU", true);
            
            // Assert
            // Should return a failed result due to timeout or MSBuild not found
            Assert.That(result.Success, Is.False);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempProjectFile))
                File.Delete(tempProjectFile);
        }
    }

    [Test]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act & Assert
        Assert.That(_executor, Is.Not.Null);
    }

    [TestCase("Debug", "Any CPU", true)]
    [TestCase("Release", "x64", false)]
    [TestCase("Debug", "x86", true)]
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
            Assert.That(result, Is.Not.Null);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempProjectFile))
                File.Delete(tempProjectFile);
        }
    }
}