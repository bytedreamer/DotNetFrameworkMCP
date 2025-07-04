using System.Text.Json;
using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Models;
using DotNetFrameworkMCP.Server.Protocol;
using DotNetFrameworkMCP.Server.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetFrameworkMCP.Server.Tools;

public class RunTestsHandler : IToolHandler
{
    private readonly ILogger<RunTestsHandler> _logger;
    private readonly McpServerConfiguration _configuration;
    private readonly ITestRunnerService _testRunnerService;

    public RunTestsHandler(
        ILogger<RunTestsHandler> logger,
        IOptions<McpServerConfiguration> configuration,
        ITestRunnerService testRunnerService)
    {
        _logger = logger;
        _configuration = configuration.Value;
        _testRunnerService = testRunnerService;
    }

    public string Name => "run_tests";

    public ToolDefinition GetDefinition()
    {
        return new ToolDefinition
        {
            Name = Name,
            Description = "Run tests in a .NET test project",
            InputSchema = new JsonSchema
            {
                Type = "object",
                Properties = new Dictionary<string, SchemaProperty>
                {
                    ["path"] = new SchemaProperty
                    {
                        Type = "string",
                        Description = "Path to test project (.csproj file)"
                    },
                    ["filter"] = new SchemaProperty
                    {
                        Type = "string",
                        Description = "Test filter expression (optional)"
                    },
                    ["verbose"] = new SchemaProperty
                    {
                        Type = "boolean",
                        Description = "Enable verbose output",
                        Default = false
                    }
                },
                Required = new List<string> { "path" }
            }
        };
    }

    public async Task<object> ExecuteAsync(JsonElement arguments)
    {
        var request = JsonSerializer.Deserialize<RunTestsRequest>(arguments.GetRawText());
        if (request == null || string.IsNullOrEmpty(request.Path))
        {
            throw new ArgumentException("Invalid test run request");
        }

        _logger.LogInformation("Running tests for project: {Path}", request.Path);

        if (!string.IsNullOrEmpty(request.Filter))
        {
            _logger.LogInformation("Using test filter: {Filter}", request.Filter);
        }

        // Create cancellation token with timeout
        using var cts = new CancellationTokenSource(_configuration.TestTimeout);

        try
        {
            return await _testRunnerService.RunTestsAsync(
                request.Path,
                request.Filter,
                request.Verbose,
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Test run timed out after {_configuration.TestTimeout}ms");
        }
    }
}