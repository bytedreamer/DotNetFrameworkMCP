using System.Text.Json.Serialization;

namespace DotNetFrameworkMCP.Server.Models;

public class RunTestsRequest
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    [JsonPropertyName("verbose")]
    public bool Verbose { get; set; } = false;
}

public class TestResult
{
    [JsonPropertyName("totalTests")]
    public int TotalTests { get; set; }

    [JsonPropertyName("passedTests")]
    public int PassedTests { get; set; }

    [JsonPropertyName("failedTests")]
    public int FailedTests { get; set; }

    [JsonPropertyName("skippedTests")]
    public int SkippedTests { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("testDetails")]
    public List<TestDetail> TestDetails { get; set; } = new();

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;
}

public class TestDetail
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("className")]
    public string ClassName { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("stackTrace")]
    public string? StackTrace { get; set; }
}