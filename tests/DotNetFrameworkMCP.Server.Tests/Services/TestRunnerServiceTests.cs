using DotNetFrameworkMCP.Server.Executors;
using DotNetFrameworkMCP.Server.Models;
using DotNetFrameworkMCP.Server.Services;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace DotNetFrameworkMCP.Server.Tests.Services;

[TestFixture]
public class TestRunnerServiceTests
{
    private Mock<ILogger<TestRunnerService>> _mockLogger;
    private Mock<IExecutorFactory> _mockExecutorFactory;
    private Mock<ITestExecutor> _mockTestExecutor;
    private TestRunnerService _service;
    private string _tempProjectFile;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<TestRunnerService>>();
        _mockExecutorFactory = new Mock<IExecutorFactory>();
        _mockTestExecutor = new Mock<ITestExecutor>();

        _mockExecutorFactory.Setup(x => x.CreateTestExecutor())
            .Returns(_mockTestExecutor.Object);

        _service = new TestRunnerService(_mockLogger.Object, _mockExecutorFactory.Object);

        // Create a temporary project file for testing
        _tempProjectFile = Path.GetTempFileName();
        File.WriteAllText(_tempProjectFile, @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.0.0"" />
    <PackageReference Include=""MSTest.TestFramework"" Version=""2.2.7"" />
    <PackageReference Include=""MSTest.TestAdapter"" Version=""2.2.7"" />
  </ItemGroup>
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
    public async Task RunTestsAsync_CallsExecutorFactory()
    {
        // Arrange
        var expectedResult = new TestResult
        {
            TotalTests = 5,
            PassedTests = 4,
            FailedTests = 1,
            SkippedTests = 0,
            Duration = 2.5,
            TestDetails = new List<TestDetail>(),
            Output = "Test output"
        };

        _mockTestExecutor.Setup(x => x.ExecuteTestsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.RunTestsAsync(_tempProjectFile, null, false);

        // Assert
        Assert.That(result, Is.EqualTo(expectedResult));
        _mockExecutorFactory.Verify(x => x.CreateTestExecutor(), Times.Once);
        _mockTestExecutor.Verify(x => x.ExecuteTestsAsync(
            _tempProjectFile, null, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RunTestsAsync_WithFilter_PassesFilterToExecutor()
    {
        // Arrange
        var filter = "TestCategory=Unit";
        var expectedResult = new TestResult { TotalTests = 1, PassedTests = 1 };

        _mockTestExecutor.Setup(x => x.ExecuteTestsAsync(
                It.IsAny<string>(),
                filter,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.RunTestsAsync(_tempProjectFile, filter, true);

        // Assert
        _mockTestExecutor.Verify(x => x.ExecuteTestsAsync(
            _tempProjectFile, filter, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RunTestsAsync_WhenExecutorThrows_ReturnsFailedResult()
    {
        // Arrange
        _mockTestExecutor.Setup(x => x.ExecuteTestsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test executor failed"));

        // Act
        var result = await _service.RunTestsAsync(_tempProjectFile, null, false);

        // Assert
        Assert.That(result.TotalTests, Is.EqualTo(0));
        Assert.That(result.PassedTests, Is.EqualTo(0));
        Assert.That(result.FailedTests, Is.EqualTo(0));
        Assert.That(result.SkippedTests, Is.EqualTo(0));
        Assert.That(result.TestDetails, Has.Count.EqualTo(1));
        Assert.That(result.TestDetails[0].Result, Is.EqualTo("Failed"));
        Assert.That(result.TestDetails[0].ErrorMessage, Does.Contain("Test executor failed"));
    }

    [Test]
    public async Task RunTestsAsync_WhenFactoryThrows_ReturnsFailedResult()
    {
        // Arrange
        _mockExecutorFactory.Setup(x => x.CreateTestExecutor())
            .Throws(new InvalidOperationException("Factory failed"));

        // Act
        var result = await _service.RunTestsAsync(_tempProjectFile, null, false);

        // Assert
        Assert.That(result.TotalTests, Is.EqualTo(0));
        Assert.That(result.TestDetails, Has.Count.EqualTo(1));
        Assert.That(result.TestDetails[0].Result, Is.EqualTo("Failed"));
        Assert.That(result.Output, Does.Contain("Test service failed"));
    }
}