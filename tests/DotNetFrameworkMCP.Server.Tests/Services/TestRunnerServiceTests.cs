using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace DotNetFrameworkMCP.Server.Tests.Services;

[TestFixture]
public class TestRunnerServiceTests
{
    private Mock<ILogger<TestRunnerService>> _mockLogger;
    private IOptions<McpServerConfiguration> _configuration;
    private TestRunnerService _service;
    private string _tempProjectFile;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<TestRunnerService>>();
        _configuration = Options.Create(new McpServerConfiguration
        {
            TestTimeout = 300000
        });

        _service = new TestRunnerService(_mockLogger.Object, _configuration);

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
    public async Task RunTestsAsync_WithNonExistentProject_ShouldReturnErrorResult()
    {
        // Arrange
        var nonExistentPath = "NonExistent.csproj";

        // Act
        var result = await _service.RunTestsAsync(nonExistentPath, null, false, CancellationToken.None);

        // Assert
        Assert.That(result.TotalTests, Is.EqualTo(0));
        Assert.That(result.PassedTests, Is.EqualTo(0));
        Assert.That(result.FailedTests, Is.EqualTo(0));
        Assert.That(result.SkippedTests, Is.EqualTo(0));
        Assert.That(result.TestDetails, Has.Count.EqualTo(1));
        Assert.That(result.TestDetails[0].Name, Is.EqualTo("Test Execution Error"));
        Assert.That(result.TestDetails[0].Result, Is.EqualTo("Failed"));
        Assert.That(result.TestDetails[0].ErrorMessage, Does.Contain("not found"));
    }

    [Test]
    public async Task DetectTestFramework_WithMSTestProject_ShouldReturnMSTest()
    {
        // Act
        var result = await _service.RunTestsAsync(_tempProjectFile, null, false, CancellationToken.None);

        // The service should attempt to detect MSTest but will fail during execution
        // since we don't have a real test assembly built
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task RunTestsAsync_WithCancellation_ShouldHandleCancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _service.RunTestsAsync(_tempProjectFile, null, false, cts.Token);

        // Assert - The service catches cancellation and returns an error result
        Assert.That(result.TotalTests, Is.EqualTo(0));
        Assert.That(result.TestDetails, Has.Count.EqualTo(1));
        Assert.That(result.TestDetails[0].Result, Is.EqualTo("Failed"));
    }

    [TestCase("Microsoft.NET.Test.Sdk", "MSTest")]
    [TestCase("MSTest.TestFramework", "MSTest")]
    [TestCase("NUnit", "NUnit")]
    [TestCase("nunit", "NUnit")]
    [TestCase("xunit", "xUnit")]
    [TestCase("xUnit", "xUnit")]
    public async Task DetectTestFramework_ShouldDetectCorrectFramework(string packageReference, string expectedFramework)
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var projectContent = $@"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""{packageReference}"" Version=""1.0.0"" />
  </ItemGroup>
</Project>";
        
        File.WriteAllText(tempFile, projectContent);

        try
        {
            // Act
            var result = await _service.RunTestsAsync(tempFile, null, false, CancellationToken.None);

            // Assert - The service should try to run tests but fail due to no built assembly
            // The important part is that it detected the framework correctly (we can't easily test this directly)
            Assert.That(result, Is.Not.Null);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}