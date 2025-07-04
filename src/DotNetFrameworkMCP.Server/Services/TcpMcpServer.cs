using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Protocol;
using DotNetFrameworkMCP.Server.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetFrameworkMCP.Server.Services;

public class TcpMcpServer
{
    private readonly ILogger<TcpMcpServer> _logger;
    private readonly McpServerConfiguration _configuration;
    private readonly Dictionary<string, IToolHandler> _toolHandlers;
    private readonly JsonSerializerOptions _jsonOptions;
    private TcpListener? _listener;

    public TcpMcpServer(
        ILogger<TcpMcpServer> logger,
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

    public async Task RunAsync(int port = 3001, CancellationToken cancellationToken = default)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        
        _logger.LogInformation("TCP MCP Server listening on port {Port}", port);
        
        // Register cancellation callback to stop the listener
        using var registration = cancellationToken.Register(() =>
        {
            _logger.LogInformation("Cancellation requested, stopping TCP listener");
            _listener?.Stop();
        });

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync().WaitAsync(cancellationToken);
                _logger.LogInformation("Client connected from {RemoteEndPoint}", client.Client.RemoteEndPoint);
                
                // Handle each client in a separate task
                _ = Task.Run(async () => await HandleClientAsync(client, cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation occurs
            _logger.LogInformation("TCP server shutdown requested");
        }
        catch (ObjectDisposedException)
        {
            // Expected when cancellation occurs
        }
        finally
        {
            _listener?.Stop();
            _listener = null;
            _logger.LogInformation("TCP MCP Server stopped");
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var buffer = new byte[4096];
                var messageBuffer = new List<byte>();

                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0)
                        break;

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
                                var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                                var responseBytes = Encoding.UTF8.GetBytes(responseJson + "\n");
                                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
                                await stream.FlushAsync(cancellationToken);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client");
        }
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
                    name = "dotnet-framework-mcp-tcp",
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