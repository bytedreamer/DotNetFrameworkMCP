using System.Text.Json;
using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Protocol;
using DotNetFrameworkMCP.Server.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetFrameworkMCP.Server.Services;

public class McpServer
{
    private readonly ILogger<McpServer> _logger;
    private readonly McpServerConfiguration _configuration;
    private readonly Dictionary<string, IToolHandler> _toolHandlers;
    private readonly JsonSerializerOptions _jsonOptions;

    public McpServer(
        ILogger<McpServer> logger,
        IOptions<McpServerConfiguration> configuration,
        IEnumerable<IToolHandler> toolHandlers)
    {
        _logger = logger;
        _configuration = configuration.Value;
        _toolHandlers = toolHandlers.ToDictionary(h => h.Name);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP Server starting...");

        using var reader = Console.OpenStandardInput();
        using var writer = Console.OpenStandardOutput();

        var buffer = new byte[65536];
        var messageBuffer = new List<byte>();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0)
                {
                    _logger.LogInformation("Input stream closed, shutting down...");
                    break;
                }

                messageBuffer.AddRange(buffer.Take(bytesRead));

                // Process complete messages (look for newline delimiters)
                while (true)
                {
                    var newlineIndex = messageBuffer.IndexOf((byte)'\n');
                    if (newlineIndex == -1)
                        break;

                    var messageBytes = messageBuffer.Take(newlineIndex).ToArray();
                    messageBuffer.RemoveRange(0, newlineIndex + 1);

                    if (messageBytes.Length > 0)
                    {
                        var response = await ProcessMessageAsync(messageBytes);
                        if (response != null)
                        {
                            var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response, _jsonOptions);
                            await writer.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
                            await writer.WriteAsync(new byte[] { (byte)'\n' }, 0, 1, cancellationToken);
                            await writer.FlushAsync(cancellationToken);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in main server loop");
            }
        }

        _logger.LogInformation("MCP Server stopped");
    }

    private async Task<McpMessage?> ProcessMessageAsync(byte[] messageBytes)
    {
        try
        {
            var message = JsonSerializer.Deserialize<McpMessage>(messageBytes, _jsonOptions);
            if (message == null)
            {
                return CreateErrorResponse(null, McpErrorCodes.ParseError, "Failed to parse message");
            }

            _logger.LogDebug("Received message: {Method}", message.Method);

            // Handle different MCP methods
            switch (message.Method)
            {
                case "initialize":
                    return await HandleInitializeAsync(message);
                
                case "tools/list":
                    return await HandleToolsListAsync(message);
                
                case "tools/call":
                    return await HandleToolCallAsync(message);
                
                case null when message.Id != null:
                    // This is likely a response to a request we made
                    return null;
                
                default:
                    return CreateErrorResponse(
                        message.Id,
                        McpErrorCodes.MethodNotFound,
                        $"Method '{message.Method}' not found");
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON message");
            return CreateErrorResponse(null, McpErrorCodes.ParseError, "Invalid JSON");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            return CreateErrorResponse(null, McpErrorCodes.InternalError, ex.Message);
        }
    }

    private Task<McpMessage> HandleInitializeAsync(McpMessage request)
    {
        var response = new McpMessage
        {
            Id = request.Id,
            Result = new
            {
                protocolVersion = "2025-06-18",
                capabilities = new
                {
                    tools = new { }
                },
                serverInfo = new
                {
                    name = "dotnet-framework-mcp",
                    version = "1.0.0"
                }
            }
        };

        return Task.FromResult(response);
    }

    private Task<McpMessage> HandleToolsListAsync(McpMessage request)
    {
        var tools = _toolHandlers.Values.Select(h => h.GetDefinition()).ToList();
        
        var response = new McpMessage
        {
            Id = request.Id,
            Result = new { tools }
        };

        return Task.FromResult(response);
    }

    private async Task<McpMessage> HandleToolCallAsync(McpMessage request)
    {
        try
        {
            var toolCallParams = JsonSerializer.Deserialize<ToolCallParams>(
                JsonSerializer.Serialize(request.Params),
                _jsonOptions);

            if (toolCallParams == null || string.IsNullOrEmpty(toolCallParams.Name))
            {
                return CreateErrorResponse(
                    request.Id,
                    McpErrorCodes.InvalidParams,
                    "Invalid tool call parameters");
            }

            if (!_toolHandlers.TryGetValue(toolCallParams.Name, out var handler))
            {
                return CreateErrorResponse(
                    request.Id,
                    McpErrorCodes.InvalidParams,
                    $"Tool '{toolCallParams.Name}' not found");
            }

            var result = await handler.ExecuteAsync(toolCallParams.Arguments);

            return new McpMessage
            {
                Id = request.Id,
                Result = new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = JsonSerializer.Serialize(result, _jsonOptions)
                        }
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool");
            return CreateErrorResponse(
                request.Id,
                McpErrorCodes.InternalError,
                $"Tool execution failed: {ex.Message}");
        }
    }

    private McpMessage CreateErrorResponse(object? id, int code, string message)
    {
        return new McpMessage
        {
            Id = id,
            Error = new McpError
            {
                Code = code,
                Message = message
            }
        };
    }

    private class ToolCallParams
    {
        public string Name { get; set; } = string.Empty;
        public JsonElement Arguments { get; set; }
    }
}