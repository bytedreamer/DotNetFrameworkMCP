using System.Text.Json.Serialization;

namespace DotNetFrameworkMCP.Server.Models;

public class AnalyzeSolutionRequest
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

public class SolutionAnalysis
{
    [JsonPropertyName("solutionName")]
    public string SolutionName { get; set; } = string.Empty;

    [JsonPropertyName("projects")]
    public List<ProjectInfo> Projects { get; set; } = new();

    [JsonPropertyName("totalProjects")]
    public int TotalProjects { get; set; }
}

public class ProjectInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("targetFramework")]
    public string TargetFramework { get; set; } = string.Empty;

    [JsonPropertyName("outputType")]
    public string OutputType { get; set; } = string.Empty;

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    [JsonPropertyName("packages")]
    public List<PackageInfo> Packages { get; set; } = new();
}

public class ListPackagesRequest
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

public class PackageInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}