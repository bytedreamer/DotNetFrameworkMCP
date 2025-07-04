using System.Text.Json;
using DotNetFrameworkMCP.Server.Protocol;

namespace DotNetFrameworkMCP.Server.Tests.Protocol;

[TestFixture]
public class McpMessageTests
{
    private JsonSerializerOptions _jsonOptions;

    [SetUp]
    public void Setup()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    [Test]
    public void McpMessage_SerializesCorrectly_WithMethod()
    {
        var message = new McpMessage
        {
            JsonRpc = "2.0",
            Id = 1,
            Method = "test_method",
            Params = new { foo = "bar" }
        };

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<McpMessage>(json, _jsonOptions);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized.JsonRpc, Is.EqualTo("2.0"));
        Assert.That(deserialized.Id?.ToString(), Is.EqualTo("1"));
        Assert.That(deserialized.Method, Is.EqualTo("test_method"));
    }

    [Test]
    public void McpMessage_SerializesCorrectly_WithResult()
    {
        var message = new McpMessage
        {
            JsonRpc = "2.0",
            Id = 2,
            Result = new { success = true }
        };

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        
        Assert.That(json, Does.Contain("\"jsonrpc\":\"2.0\""));
        Assert.That(json, Does.Contain("\"id\":2"));
        Assert.That(json, Does.Contain("\"result\""));
        Assert.That(json, Does.Not.Contain("\"method\""));
        Assert.That(json, Does.Not.Contain("\"params\""));
    }

    [Test]
    public void McpMessage_SerializesCorrectly_WithError()
    {
        var message = new McpMessage
        {
            JsonRpc = "2.0",
            Id = 3,
            Error = new McpError
            {
                Code = McpErrorCodes.MethodNotFound,
                Message = "Method not found"
            }
        };

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<McpMessage>(json, _jsonOptions);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized.Error, Is.Not.Null);
        Assert.That(deserialized.Error.Code, Is.EqualTo(McpErrorCodes.MethodNotFound));
        Assert.That(deserialized.Error.Message, Is.EqualTo("Method not found"));
    }

    [Test]
    public void McpErrorCodes_HaveCorrectValues()
    {
        Assert.That(McpErrorCodes.ParseError, Is.EqualTo(-32700));
        Assert.That(McpErrorCodes.InvalidRequest, Is.EqualTo(-32600));
        Assert.That(McpErrorCodes.MethodNotFound, Is.EqualTo(-32601));
        Assert.That(McpErrorCodes.InvalidParams, Is.EqualTo(-32602));
        Assert.That(McpErrorCodes.InternalError, Is.EqualTo(-32603));
    }
}