using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Services;
using DotNetFrameworkMCP.Server.Tools;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Register MSBuild before anything else
var msbuildPath = Environment.GetEnvironmentVariable("MSBUILD_PATH");

if (!string.IsNullOrEmpty(msbuildPath))
{
    // Use explicitly specified MSBuild path
    Console.Error.WriteLine($"Using MSBuild from environment variable: {msbuildPath}");
    MSBuildLocator.RegisterMSBuildPath(msbuildPath);
}
else
{
    // Try to find Visual Studio or Build Tools MSBuild (for .NET Framework)
    var instances = MSBuildLocator.QueryVisualStudioInstances()
        .Where(instance => instance.DiscoveryType == DiscoveryType.VisualStudioSetup ||
                          instance.DiscoveryType == DiscoveryType.DeveloperConsole)
        .OrderByDescending(instance => instance.Version)
        .ToList();

    if (instances.Any())
    {
        // Show all available instances
        Console.Error.WriteLine("Available MSBuild instances:");
        foreach (var instance in instances)
        {
            Console.Error.WriteLine($"  - {instance.Name} {instance.Version} at {instance.MSBuildPath}");
            Console.Error.WriteLine($"    Discovery Type: {instance.DiscoveryType}");
        }

        // Prefer Visual Studio instances over standalone build tools
        var vsInstance = instances.FirstOrDefault(i => i.Name.Contains("Visual Studio"));
        var selectedInstance = vsInstance ?? instances.First();
        
        Console.Error.WriteLine($"Using MSBuild: {selectedInstance.Name} {selectedInstance.Version}");
        Console.Error.WriteLine($"MSBuild Path: {selectedInstance.MSBuildPath}");
        MSBuildLocator.RegisterInstance(selectedInstance);
    }
    else
    {
        // Fallback to looking for MSBuild in standard locations
        Console.Error.WriteLine("No Visual Studio MSBuild instances found, checking standard locations...");
        
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        
        var possiblePaths = new[]
        {
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin"),
            Path.Combine(programFiles, @"Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin"),
            Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin"),
            Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin"),
            Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin")
        };

        var msbuildDir = possiblePaths.FirstOrDefault(Directory.Exists);
        if (msbuildDir != null)
        {
            Console.Error.WriteLine($"Found MSBuild at: {msbuildDir}");
            MSBuildLocator.RegisterMSBuildPath(msbuildDir);
        }
        else
        {
            Console.Error.WriteLine("WARNING: Could not find .NET Framework MSBuild!");
            Console.Error.WriteLine("Please install Visual Studio or Build Tools for Visual Studio");
            Console.Error.WriteLine("Or set MSBUILD_PATH environment variable to MSBuild.exe directory");
            MSBuildLocator.RegisterDefaults();
        }
    }
}

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
        services.AddSingleton<IProcessBasedBuildService, ProcessBasedBuildService>();

        // Register tool handlers
        services.AddSingleton<IToolHandler, BuildProjectHandler>();
        // TODO: Add other tool handlers here
        
        // Register the MCP servers
        services.AddSingleton<McpServer>();
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

    // Check if running in TCP mode
    var tcpMode = args.Contains("--tcp");
    var port = 3001;
    
    if (args.Contains("--port"))
    {
        var portIndex = Array.IndexOf(args, "--port");
        if (portIndex + 1 < args.Length && int.TryParse(args[portIndex + 1], out var parsedPort))
        {
            port = parsedPort;
        }
    }

    if (tcpMode)
    {
        var tcpServer = host.Services.GetRequiredService<TcpMcpServer>();
        logger.LogInformation("Starting TCP MCP Server on port {Port}", port);
        await tcpServer.RunAsync(port, cts.Token);
    }
    else
    {
        var server = host.Services.GetRequiredService<McpServer>();
        logger.LogInformation("Starting stdin/stdout MCP Server");
        await server.RunAsync(cts.Token);
    }
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Server failed to start");
    Environment.Exit(1);
}
