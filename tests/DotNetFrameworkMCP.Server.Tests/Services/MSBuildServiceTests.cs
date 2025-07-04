using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Services;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace DotNetFrameworkMCP.Server.Tests.Services;

[TestFixture]
public class MSBuildServiceTests
{
    private MSBuildService _service;
    private ILogger<MSBuildService> _logger;
    private IOptions<McpServerConfiguration> _configuration;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Register MSBuild before any tests run
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    [SetUp]
    public void Setup()
    {
        _logger = new TestLogger<MSBuildService>();
        _configuration = Options.Create(new McpServerConfiguration
        {
            DefaultConfiguration = "Debug",
            DefaultPlatform = "Any CPU",
            BuildTimeout = 600000
        });
        _service = new MSBuildService(_logger, _configuration);
    }

    [Test]
    public async Task BuildProjectAsync_WithNonExistentFile_ReturnsFailure()
    {
        var result = await _service.BuildProjectAsync(
            "nonexistent.csproj",
            "Debug",
            "Any CPU",
            false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Is.Not.Empty);
        Assert.That(result.Errors[0].Message, Does.Contain("not found"));
    }

    [Test]
    public async Task BuildProjectAsync_WithCancellation_ReturnsFailure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _service.BuildProjectAsync(
            "test.csproj",
            "Debug",
            "Any CPU",
            false,
            cts.Token);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Is.Not.Empty);
    }

    private class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}