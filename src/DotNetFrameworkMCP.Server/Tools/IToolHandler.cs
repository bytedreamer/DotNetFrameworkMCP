using System.Text.Json;
using DotNetFrameworkMCP.Server.Protocol;

namespace DotNetFrameworkMCP.Server.Tools;

public interface IToolHandler
{
    string Name { get; }
    ToolDefinition GetDefinition();
    Task<object> ExecuteAsync(JsonElement arguments);
}