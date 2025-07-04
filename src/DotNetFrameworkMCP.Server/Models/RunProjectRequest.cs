using System.Text.Json.Serialization;

namespace DotNetFrameworkMCP.Server.Models;

public class RunProjectRequest
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }
}

public class RunResult
{
    [JsonPropertyName("exitCode")]
    public int ExitCode { get; set; }

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public double Duration { get; set; }
}