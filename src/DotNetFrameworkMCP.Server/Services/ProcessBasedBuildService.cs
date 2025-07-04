using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetFrameworkMCP.Server.Services;

public interface IProcessBasedBuildService
{
    Task<Models.BuildResult> BuildProjectAsync(string projectPath, string configuration, string platform, bool restore, CancellationToken cancellationToken = default);
}

public class ProcessBasedBuildService : IProcessBasedBuildService
{
    private readonly ILogger<ProcessBasedBuildService> _logger;
    private readonly McpServerConfiguration _configuration;

    public ProcessBasedBuildService(
        ILogger<ProcessBasedBuildService> logger,
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

            // Find MSBuild.exe
            var msbuildPath = FindMSBuildExecutable();
            if (string.IsNullOrEmpty(msbuildPath))
            {
                _logger.LogError("Could not find MSBuild.exe in any standard locations");
                throw new InvalidOperationException("Could not find MSBuild.exe. Please install Visual Studio or Build Tools for Visual Studio, or set MSBUILD_EXE_PATH environment variable.");
            }

            _logger.LogInformation("Using MSBuild.exe: {MSBuildPath}", msbuildPath);

            // Build the project using MSBuild.exe process
            var result = await RunMSBuildAsync(msbuildPath, projectPath, configuration, platform, restore, cancellationToken);
            
            // Parse the output for errors and warnings
            ParseBuildOutput(result.Output, errors, warnings);

            stopwatch.Stop();

            return new Models.BuildResult
            {
                Success = result.ExitCode == 0,
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

    private string? FindMSBuildExecutable()
    {
        // Check environment variable first
        var envPath = Environment.GetEnvironmentVariable("MSBUILD_EXE_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        // Look for MSBuild.exe in standard Visual Studio locations
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var possiblePaths = new[]
        {
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFiles, @"Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
            Path.Combine(programFiles, @"Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"),
            // Legacy paths
            Path.Combine(programFilesX86, @"MSBuild\14.0\Bin\MSBuild.exe"),
            Path.Combine(programFilesX86, @"MSBuild\15.0\Bin\MSBuild.exe"),
            @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe",
            @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
        };

        return possiblePaths.FirstOrDefault(File.Exists);
    }

    private async Task<(int ExitCode, string Output)> RunMSBuildAsync(
        string msbuildPath,
        string projectPath,
        string configuration,
        string platform,
        bool restore,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            $"\"{projectPath}\"",
            $"/p:Configuration={configuration}",
            $"/p:Platform=\"{platform}\"",
            "/v:normal", // Normal verbosity
            "/nologo"
        };

        if (restore)
        {
            arguments.Add("/restore");
        }

        var argumentString = string.Join(" ", arguments);
        _logger.LogDebug("Running: {MSBuildPath} {Arguments}", msbuildPath, argumentString);

        var psi = new ProcessStartInfo
        {
            FileName = msbuildPath,
            Arguments = argumentString,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory
        };

        var output = new StringBuilder();

        using var process = new Process { StartInfo = psi };
        
        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for completion with timeout
        var timeoutMs = _configuration.BuildTimeout;
        var completed = await Task.Run(() => process.WaitForExit(timeoutMs), cancellationToken);

        if (!completed)
        {
            process.Kill();
            throw new TimeoutException($"Build timed out after {timeoutMs}ms");
        }

        return (process.ExitCode, output.ToString());
    }

    private void ParseBuildOutput(string output, List<BuildMessage> errors, List<BuildMessage> warnings)
    {
        if (string.IsNullOrEmpty(output))
            return;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Regex patterns for MSBuild error and warning messages
        var errorPattern = new Regex(@"^(.+?)\((\d+),(\d+)\):\s+error\s+([A-Z]+\d+):\s+(.+)$", RegexOptions.Multiline);
        var warningPattern = new Regex(@"^(.+?)\((\d+),(\d+)\):\s+warning\s+([A-Z]+\d+):\s+(.+)$", RegexOptions.Multiline);
        var generalErrorPattern = new Regex(@"^(.+?):\s+error\s+([A-Z]+\d+):\s+(.+)$", RegexOptions.Multiline);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Try specific error pattern first (file with line/column)
            var errorMatch = errorPattern.Match(trimmedLine);
            if (errorMatch.Success)
            {
                errors.Add(new BuildMessage
                {
                    File = errorMatch.Groups[1].Value,
                    Line = int.TryParse(errorMatch.Groups[2].Value, out var errorLine) ? errorLine : 0,
                    Column = int.TryParse(errorMatch.Groups[3].Value, out var errorCol) ? errorCol : 0,
                    Code = errorMatch.Groups[4].Value,
                    Message = errorMatch.Groups[5].Value
                });
                continue;
            }

            // Try warning pattern
            var warningMatch = warningPattern.Match(trimmedLine);
            if (warningMatch.Success)
            {
                warnings.Add(new BuildMessage
                {
                    File = warningMatch.Groups[1].Value,
                    Line = int.TryParse(warningMatch.Groups[2].Value, out var warningLine) ? warningLine : 0,
                    Column = int.TryParse(warningMatch.Groups[3].Value, out var warningCol) ? warningCol : 0,
                    Code = warningMatch.Groups[4].Value,
                    Message = warningMatch.Groups[5].Value
                });
                continue;
            }

            // Try general error pattern (no line/column)
            var generalErrorMatch = generalErrorPattern.Match(trimmedLine);
            if (generalErrorMatch.Success)
            {
                errors.Add(new BuildMessage
                {
                    File = generalErrorMatch.Groups[1].Value,
                    Code = generalErrorMatch.Groups[2].Value,
                    Message = generalErrorMatch.Groups[3].Value
                });
            }
        }
    }
}