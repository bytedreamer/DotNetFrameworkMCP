using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Services;
using DotNetFrameworkMCP.Server.Tools;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Register MSBuild before anything else
MSBuildLocator.RegisterDefaults();

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        config.AddEnvironmentVariables(prefix: "MCPSERVER_");
    })
    .ConfigureServices((context, services) =>
    {
        // Configuration
        services.Configure<McpServerConfiguration>(
            context.Configuration.GetSection("McpServer"));

        // Register services
        services.AddSingleton<IMSBuildService, MSBuildService>();

        // Register tool handlers
        services.AddSingleton<IToolHandler, BuildProjectHandler>();
        // TODO: Add other tool handlers here
        
        // Register the MCP server
        services.AddSingleton<McpServer>();

        // Configure logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            
            // Only log to stderr to keep stdout clean for MCP messages
            builder.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });
            
            builder.SetMinimumLevel(LogLevel.Information);
            
            if (context.Configuration.GetValue<bool>("McpServer:EnableDetailedLogging"))
            {
                builder.SetMinimumLevel(LogLevel.Debug);
            }
        });
    })
    .UseConsoleLifetime()
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var server = host.Services.GetRequiredService<McpServer>();

try
{
    logger.LogInformation("Starting .NET Framework MCP Server");
    
    // Create cancellation token that responds to Ctrl+C
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (sender, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    await server.RunAsync(cts.Token);
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Server failed to start");
    Environment.Exit(1);
}
