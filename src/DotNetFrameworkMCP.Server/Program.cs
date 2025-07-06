using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Executors;
using DotNetFrameworkMCP.Server.Services;
using DotNetFrameworkMCP.Server.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

        // Register executors
        services.AddSingleton<MSBuildExecutor>();
        services.AddSingleton<DotNetBuildExecutor>();
        services.AddSingleton<VSTestExecutor>();
        services.AddSingleton<DotNetTestExecutor>();
        services.AddSingleton<IExecutorFactory, ExecutorFactory>();

        // Register services
        services.AddSingleton<IProcessBasedBuildService, ProcessBasedBuildService>();
        services.AddSingleton<ITestRunnerService, TestRunnerService>();

        // Register tool handlers
        services.AddSingleton<IToolHandler, BuildProjectHandler>();
        services.AddSingleton<IToolHandler, RunTestsHandler>();
        
        // Register the TCP MCP server
        services.AddSingleton<TcpMcpServer>();

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

    // Parse port from command line arguments
    var port = 3001;
    if (args.Contains("--port"))
    {
        var portIndex = Array.IndexOf(args, "--port");
        if (portIndex + 1 < args.Length && int.TryParse(args[portIndex + 1], out var parsedPort))
        {
            port = parsedPort;
        }
    }

    var tcpServer = host.Services.GetRequiredService<TcpMcpServer>();
    logger.LogInformation("Starting TCP MCP Server on port {Port}", port);
    await tcpServer.RunAsync(port, cts.Token);
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Server failed to start");
    Environment.Exit(1);
}
