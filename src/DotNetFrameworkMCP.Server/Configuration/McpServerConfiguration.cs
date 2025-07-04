namespace DotNetFrameworkMCP.Server.Configuration;

public class McpServerConfiguration
{
    public string MsBuildPath { get; set; } = "auto";
    public string DefaultConfiguration { get; set; } = "Debug";
    public string DefaultPlatform { get; set; } = "Any CPU";
    public int TestTimeout { get; set; } = 300000;
    public int BuildTimeout { get; set; } = 600000;
    public bool EnableDetailedLogging { get; set; } = false;
}