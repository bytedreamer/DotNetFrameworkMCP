namespace DotNetFrameworkMCP.Server.Configuration;

public class McpServerConfiguration
{
    public string MsBuildPath { get; set; } = "auto";
    public string DefaultConfiguration { get; set; } = "Debug";
    public string DefaultPlatform { get; set; } = "Any CPU";
    public int TestTimeout { get; set; } = 300000;
    public int BuildTimeout { get; set; } = 1200000; // 20 minutes for large solutions
    public bool EnableDetailedLogging { get; set; } = false;
    public string PreferredVSVersion { get; set; } = "2022"; // Options: "2022", "2019", "auto"
}