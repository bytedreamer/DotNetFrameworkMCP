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
    private readonly IMSBuildService _msBuildService;
    private readonly IProcessBasedBuildService _processBasedBuildService;

    public BuildProjectHandler(
        ILogger<BuildProjectHandler> logger,
        IOptions<McpServerConfiguration> configuration,
        IMSBuildService msBuildService,
        IProcessBasedBuildService processBasedBuildService)
    {
        _logger = logger;
        _configuration = configuration.Value;
        _msBuildService = msBuildService;
        _processBasedBuildService = processBasedBuildService;
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
            // Try MSBuild API first, but fall back to process-based on any failure
            try
            {
                _logger.LogDebug("Attempting MSBuild API approach");
                var result = await _msBuildService.BuildProjectAsync(
                    request.Path,
                    configuration,
                    platform,
                    request.Restore,
                    cts.Token);

                // Check if the result indicates a MSBuild API compatibility issue
                if (!result.Success && result.Errors.Any(e => 
                    e.Message.Contains("System.Configuration.ConfigurationManager") ||
                    e.Message.Contains("Build was canceled") ||
                    e.Message.Contains("internal failure") ||
                    e.Message.Contains("MSB4014")))
                {
                    var errorMessage = string.Join("; ", result.Errors.Select(e => e.Message));
                    _logger.LogWarning("MSBuild API failed with compatibility error, falling back to process-based build. Error: {Error}", errorMessage);
                    
                    // Fallback to process-based MSBuild
                    return await _processBasedBuildService.BuildProjectAsync(
                        request.Path,
                        configuration,
                        platform,
                        request.Restore,
                        cts.Token);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("MSBuild API threw exception, falling back to process-based build. Error: {Error}", ex.Message);
                _logger.LogDebug("Full exception details: {Exception}", ex.ToString());
                
                // Fallback to process-based MSBuild for any MSBuild API exception
                return await _processBasedBuildService.BuildProjectAsync(
                    request.Path,
                    configuration,
                    platform,
                    request.Restore,
                    cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Build timed out after {_configuration.BuildTimeout}ms");
        }
    }
}