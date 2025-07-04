using System.Text.Json.Serialization;

namespace DotNetFrameworkMCP.Server.Models;

public class BuildProjectRequest
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("configuration")]
    public string Configuration { get; set; } = "Debug";

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "Any CPU";

    [JsonPropertyName("restore")]
    public bool Restore { get; set; } = true;
}

public class BuildResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errors")]
    public List<BuildMessage> Errors { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<BuildMessage> Warnings { get; set; } = new();

    [JsonPropertyName("buildTime")]
    public double BuildTime { get; set; }

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;
}

public class BuildMessage
{
    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("column")]
    public int Column { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("project")]
    public string? Project { get; set; }
}