using System.Text.Json;
using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Models;
using DotNetFrameworkMCP.Server.Protocol;
using DotNetFrameworkMCP.Server.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetFrameworkMCP.Server.Tools;

public class BuildProjectHandler : IToolHandler
{
    private readonly ILogger<BuildProjectHandler> _logger;
    private readonly McpServerConfiguration _configuration;
    private readonly IProcessBasedBuildService _buildService;

    public BuildProjectHandler(
        ILogger<BuildProjectHandler> logger,
        IOptions<McpServerConfiguration> configuration,
        IProcessBasedBuildService buildService)
    {
        _logger = logger;
        _configuration = configuration.Value;
        _buildService = buildService;
    }

    public string Name => "build_project";

    public ToolDefinition GetDefinition()
    {
        return new ToolDefinition
        {
            Name = Name,
            Description = "Build a .NET project or solution",
            InputSchema = new JsonSchema
            {
                Type = "object",
                Properties = new Dictionary<string, SchemaProperty>
                {
                    ["path"] = new SchemaProperty
                    {
                        Type = "string",
                        Description = "Path to .csproj or .sln file"
                    },
                    ["configuration"] = new SchemaProperty
                    {
                        Type = "string",
                        Description = "Build configuration",
                        Enum = new List<string> { "Debug", "Release" },
                        Default = "Debug"
                    },
                    ["platform"] = new SchemaProperty
                    {
                        Type = "string",
                        Description = "Target platform",
                        Enum = new List<string> { "Any CPU", "x86", "x64" },
                        Default = "Any CPU"
                    },
                    ["restore"] = new SchemaProperty
                    {
                        Type = "boolean",
                        Description = "Restore NuGet packages",
                        Default = true
                    }
                },
                Required = new List<string> { "path" }
            }
        };
    }

    public async Task<object> ExecuteAsync(JsonElement arguments)
    {
        var request = JsonSerializer.Deserialize<BuildProjectRequest>(arguments.GetRawText());
        if (request == null || string.IsNullOrEmpty(request.Path))
        {
            throw new ArgumentException("Invalid build request");
        }

        _logger.LogInformation("Building project: {Path}", request.Path);

        // Use default values from configuration if not specified
        var configuration = string.IsNullOrEmpty(request.Configuration) 
            ? _configuration.DefaultConfiguration 
            : request.Configuration;
        
        var platform = string.IsNullOrEmpty(request.Platform) 
            ? _configuration.DefaultPlatform 
            : request.Platform;

        // Create cancellation token with timeout
        using var cts = new CancellationTokenSource(_configuration.BuildTimeout);

        try
        {
            _logger.LogDebug("Building project using process-based MSBuild approach");
            return await _buildService.BuildProjectAsync(
                request.Path,
                configuration,
                platform,
                request.Restore,
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Build timed out after {_configuration.BuildTimeout}ms");
        }
    }
}