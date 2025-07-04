using System.Text.Json;
using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Models;
using DotNetFrameworkMCP.Server.Services;
using DotNetFrameworkMCP.Server.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace DotNetFrameworkMCP.Server.Tests.Tools;

[TestFixture]
public class RunTestsHandlerTests
{
    private Mock<ILogger<RunTestsHandler>> _mockLogger;
    private Mock<ITestRunnerService> _mockTestRunnerService;
    private IOptions<McpServerConfiguration> _configuration;
    private RunTestsHandler _handler;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<RunTestsHandler>>();
        _mockTestRunnerService = new Mock<ITestRunnerService>();
        _configuration = Options.Create(new McpServerConfiguration
        {
            TestTimeout = 300000
        });

        _handler = new RunTestsHandler(_mockLogger.Object, _configuration, _mockTestRunnerService.Object);
    }

    [Test]
    public void Name_ShouldReturnCorrectName()
    {
        Assert.That(_handler.Name, Is.EqualTo("run_tests"));
    }

    [Test]
    public void GetDefinition_ShouldReturnValidDefinition()
    {
        var definition = _handler.GetDefinition();

        Assert.That(definition.Name, Is.EqualTo("run_tests"));
        Assert.That(definition.Description, Is.EqualTo("Run tests in a .NET test project"));
        Assert.That(definition.InputSchema.Type, Is.EqualTo("object"));
        Assert.That(definition.InputSchema.Properties, Contains.Key("path"));
        Assert.That(definition.InputSchema.Properties["path"].Type, Is.EqualTo("string"));
        Assert.That(definition.InputSchema.Required, Contains.Item("path"));
    }

    [Test]
    public async Task ExecuteAsync_WithValidRequest_ShouldCallTestRunnerService()
    {
        // Arrange
        var expectedResult = new TestResult
        {
            TotalTests = 5,
            PassedTests = 4,
            FailedTests = 1,
            SkippedTests = 0,
            Duration = 2.5,
            TestDetails = new List<TestDetail>()
        };

        _mockTestRunnerService
            .Setup(x => x.RunTestsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var request = new RunTestsRequest
        {
            Path = "TestProject.csproj",
            Filter = "Category=Unit",
            Verbose = true
        };

        var jsonElement = JsonSerializer.SerializeToElement(request);

        // Act
        var result = await _handler.ExecuteAsync(jsonElement);

        // Assert
        Assert.That(result, Is.EqualTo(expectedResult));
        _mockTestRunnerService.Verify(
            x => x.RunTestsAsync("TestProject.csproj", "Category=Unit", true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_WithMinimalRequest_ShouldUseDefaults()
    {
        // Arrange
        var expectedResult = new TestResult();
        _mockTestRunnerService
            .Setup(x => x.RunTestsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var request = new RunTestsRequest
        {
            Path = "TestProject.csproj"
        };

        var jsonElement = JsonSerializer.SerializeToElement(request);

        // Act
        var result = await _handler.ExecuteAsync(jsonElement);

        // Assert
        _mockTestRunnerService.Verify(
            x => x.RunTestsAsync("TestProject.csproj", null, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void ExecuteAsync_WithNullRequest_ShouldThrowArgumentException()
    {
        // Arrange
        var jsonElement = JsonSerializer.SerializeToElement((object?)null);

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(() => _handler.ExecuteAsync(jsonElement));
    }

    [Test]
    public void ExecuteAsync_WithEmptyPath_ShouldThrowArgumentException()
    {
        // Arrange
        var request = new RunTestsRequest { Path = "" };
        var jsonElement = JsonSerializer.SerializeToElement(request);

        // Act & Assert
        Assert.ThrowsAsync<ArgumentException>(() => _handler.ExecuteAsync(jsonElement));
    }

    [Test]
    public void ExecuteAsync_WhenServiceTimesOut_ShouldThrowTimeoutException()
    {
        // Arrange
        _mockTestRunnerService
            .Setup(x => x.RunTestsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Throws<OperationCanceledException>();

        var request = new RunTestsRequest { Path = "TestProject.csproj" };
        var jsonElement = JsonSerializer.SerializeToElement(request);

        // Act & Assert
        Assert.ThrowsAsync<TimeoutException>(() => _handler.ExecuteAsync(jsonElement));
    }
}