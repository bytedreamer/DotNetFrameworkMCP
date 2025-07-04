using System.Text.Json;
using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Models;
using DotNetFrameworkMCP.Server.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace DotNetFrameworkMCP.Server.Tests.Tools;

[TestFixture]
public class BuildProjectHandlerTests
{
    private BuildProjectHandler _handler;
    private ILogger<BuildProjectHandler> _logger;
    private IOptions<McpServerConfiguration> _configuration;

    [SetUp]
    public void Setup()
    {
        _logger = new TestLogger<BuildProjectHandler>();
        _configuration = Options.Create(new McpServerConfiguration
        {
            DefaultConfiguration = "Debug",
            DefaultPlatform = "Any CPU",
            BuildTimeout = 600000
        });
        _handler = new BuildProjectHandler(_logger, _configuration);
    }

    [Test]
    public void GetDefinition_ReturnsCorrectToolDefinition()
    {
        var definition = _handler.GetDefinition();

        Assert.That(definition.Name, Is.EqualTo("build_project"));
        Assert.That(definition.Description, Is.EqualTo("Build a .NET project or solution"));
        Assert.That(definition.InputSchema, Is.Not.Null);
        Assert.That(definition.InputSchema.Type, Is.EqualTo("object"));
        Assert.That(definition.InputSchema.Properties, Has.Count.EqualTo(4));
        Assert.That(definition.InputSchema.Properties.ContainsKey("path"), Is.True);
        Assert.That(definition.InputSchema.Required, Contains.Item("path"));
    }

    [Test]
    public async Task ExecuteAsync_WithValidRequest_ReturnsSuccessResult()
    {
        var request = new BuildProjectRequest
        {
            Path = "test.csproj",
            Configuration = "Debug",
            Platform = "Any CPU",
            Restore = true
        };

        var json = JsonSerializer.Serialize(request);
        var element = JsonDocument.Parse(json).RootElement;

        var result = await _handler.ExecuteAsync(element);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.TypeOf<BuildResult>());
        
        var buildResult = (BuildResult)result;
        Assert.That(buildResult.Success, Is.True);
        Assert.That(buildResult.Errors, Is.Empty);
        Assert.That(buildResult.Warnings, Is.Empty);
    }

    [Test]
    public void ExecuteAsync_WithInvalidRequest_ThrowsArgumentException()
    {
        var invalidJson = "{}";
        var element = JsonDocument.Parse(invalidJson).RootElement;

        Assert.ThrowsAsync<ArgumentException>(async () => await _handler.ExecuteAsync(element));
    }

    private class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}