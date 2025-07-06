using DotNetFrameworkMCP.Server.Executors;
using DotNetFrameworkMCP.Server.Models;
using DotNetFrameworkMCP.Server.Services;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace DotNetFrameworkMCP.Server.Tests.Services;

[TestFixture]
public class ProcessBasedBuildServiceTests
{
    private Mock<ILogger<ProcessBasedBuildService>> _mockLogger;
    private Mock<IExecutorFactory> _mockExecutorFactory;
    private Mock<IBuildExecutor> _mockBuildExecutor;
    private ProcessBasedBuildService _service;
    private string _tempProjectFile;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<ProcessBasedBuildService>>();
        _mockExecutorFactory = new Mock<IExecutorFactory>();
        _mockBuildExecutor = new Mock<IBuildExecutor>();

        _mockExecutorFactory.Setup(x => x.CreateBuildExecutor())
            .Returns(_mockBuildExecutor.Object);

        _service = new ProcessBasedBuildService(_mockLogger.Object, _mockExecutorFactory.Object);

        // Create a temporary project file for testing
        _tempProjectFile = Path.GetTempFileName();
        File.WriteAllText(_tempProjectFile, @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
  </PropertyGroup>
</Project>");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempProjectFile))
        {
            File.Delete(_tempProjectFile);
        }
    }

    [Test]
    public async Task BuildProjectAsync_CallsExecutorFactory()
    {
        // Arrange
        var expectedResult = new BuildResult
        {
            Success = true,
            BuildTime = 5.2,
            Errors = new List<BuildMessage>(),
            Warnings = new List<BuildMessage>(),
            Output = "Build succeeded"
        };

        _mockBuildExecutor.Setup(x => x.ExecuteBuildAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.BuildProjectAsync(_tempProjectFile, "Debug", "Any CPU", true);

        // Assert
        Assert.That(result, Is.EqualTo(expectedResult));
        _mockExecutorFactory.Verify(x => x.CreateBuildExecutor(), Times.Once);
        _mockBuildExecutor.Verify(x => x.ExecuteBuildAsync(
            _tempProjectFile, "Debug", "Any CPU", true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task BuildProjectAsync_WithDifferentParameters_PassesToExecutor()
    {
        // Arrange
        var expectedResult = new BuildResult { Success = true };
        var configuration = "Release";
        var platform = "x64";
        var restore = false;

        _mockBuildExecutor.Setup(x => x.ExecuteBuildAsync(
                It.IsAny<string>(),
                configuration,
                platform,
                restore,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.BuildProjectAsync(_tempProjectFile, configuration, platform, restore);

        // Assert
        _mockBuildExecutor.Verify(x => x.ExecuteBuildAsync(
            _tempProjectFile, configuration, platform, restore, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task BuildProjectAsync_WhenExecutorThrows_ReturnsFailedResult()
    {
        // Arrange
        _mockBuildExecutor.Setup(x => x.ExecuteBuildAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Build executor failed"));

        // Act
        var result = await _service.BuildProjectAsync(_tempProjectFile, "Debug", "Any CPU", true);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(result.Errors[0].Message, Does.Contain("Build executor failed"));
        Assert.That(result.Output, Does.Contain("Build service failed"));
    }

    [Test]
    public async Task BuildProjectAsync_WhenFactoryThrows_ReturnsFailedResult()
    {
        // Arrange
        _mockExecutorFactory.Setup(x => x.CreateBuildExecutor())
            .Throws(new InvalidOperationException("Factory failed"));

        // Act
        var result = await _service.BuildProjectAsync(_tempProjectFile, "Debug", "Any CPU", true);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(result.Errors[0].Message, Does.Contain("Factory failed"));
        Assert.That(result.BuildTime, Is.EqualTo(0));
    }

    [Test]
    public async Task BuildProjectAsync_WithCancellationToken_PassesToExecutor()
    {
        // Arrange
        var expectedResult = new BuildResult { Success = true };
        var cancellationTokenSource = new CancellationTokenSource();

        _mockBuildExecutor.Setup(x => x.ExecuteBuildAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                cancellationTokenSource.Token))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.BuildProjectAsync(_tempProjectFile, "Debug", "Any CPU", true, cancellationTokenSource.Token);

        // Assert
        _mockBuildExecutor.Verify(x => x.ExecuteBuildAsync(
            _tempProjectFile, "Debug", "Any CPU", true, cancellationTokenSource.Token), Times.Once);
    }
}