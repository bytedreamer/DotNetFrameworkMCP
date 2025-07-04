using System.Diagnostics;
using System.Text;
using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Models;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetFrameworkMCP.Server.Services;

public interface IMSBuildService
{
    Task<Models.BuildResult> BuildProjectAsync(string projectPath, string configuration, string platform, bool restore, CancellationToken cancellationToken = default);
}

public class MSBuildService : IMSBuildService
{
    private readonly ILogger<MSBuildService> _logger;
    private readonly McpServerConfiguration _configuration;

    public MSBuildService(
        ILogger<MSBuildService> logger,
        IOptions<McpServerConfiguration> configuration)
    {
        _logger = logger;
        _configuration = configuration.Value;
    }

    public async Task<Models.BuildResult> BuildProjectAsync(
        string projectPath, 
        string configuration, 
        string platform, 
        bool restore,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<BuildMessage>();
        var warnings = new List<BuildMessage>();

        try
        {
            // Validate project path
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException($"Project file not found: {projectPath}");
            }

            // Create build parameters
            var buildParams = new BuildParameters
            {
                MaxNodeCount = Environment.ProcessorCount,
                Loggers = new List<Microsoft.Build.Framework.ILogger>
                {
                    new ConsoleLogger(LoggerVerbosity.Minimal),
                    new CollectingLogger(errors, warnings)
                }
            };

            // Create global properties
            var globalProperties = new Dictionary<string, string>
            {
                ["Configuration"] = configuration,
                ["Platform"] = platform
            };

            // Create build request
            var buildRequest = new BuildRequestData(
                projectPath,
                globalProperties,
                null,
                restore ? new[] { "Restore", "Build" } : new[] { "Build" },
                null);

            // Execute build
            var buildResult = await Task.Run(() =>
            {
                using var buildManager = BuildManager.DefaultBuildManager;
                buildManager.BeginBuild(buildParams);
                try
                {
                    var submission = buildManager.PendBuildRequest(buildRequest);
                    return submission.Execute();
                }
                finally
                {
                    buildManager.EndBuild();
                }
            }, cancellationToken);

            stopwatch.Stop();

            return new Models.BuildResult
            {
                Success = buildResult.OverallResult == BuildResultCode.Success,
                Errors = errors,
                Warnings = warnings,
                BuildTime = stopwatch.Elapsed.TotalSeconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build failed for project: {ProjectPath}", projectPath);
            stopwatch.Stop();

            return new Models.BuildResult
            {
                Success = false,
                Errors = new List<BuildMessage>
                {
                    new BuildMessage
                    {
                        Message = ex.Message,
                        File = projectPath
                    }
                },
                Warnings = warnings,
                BuildTime = stopwatch.Elapsed.TotalSeconds
            };
        }
    }

    private class CollectingLogger : Microsoft.Build.Framework.ILogger
    {
        private readonly List<BuildMessage> _errors;
        private readonly List<BuildMessage> _warnings;

        public CollectingLogger(List<BuildMessage> errors, List<BuildMessage> warnings)
        {
            _errors = errors;
            _warnings = warnings;
        }

        public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Normal;
        public string? Parameters { get; set; }

        public void Initialize(IEventSource eventSource)
        {
            eventSource.ErrorRaised += OnErrorRaised;
            eventSource.WarningRaised += OnWarningRaised;
        }

        public void Shutdown()
        {
        }

        private void OnErrorRaised(object sender, BuildErrorEventArgs e)
        {
            _errors.Add(new BuildMessage
            {
                File = e.File,
                Line = e.LineNumber,
                Column = e.ColumnNumber,
                Code = e.Code,
                Message = e.Message,
                Project = e.ProjectFile
            });
        }

        private void OnWarningRaised(object sender, BuildWarningEventArgs e)
        {
            _warnings.Add(new BuildMessage
            {
                File = e.File,
                Line = e.LineNumber,
                Column = e.ColumnNumber,
                Code = e.Code,
                Message = e.Message,
                Project = e.ProjectFile
            });
        }
    }
}