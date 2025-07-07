using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DotNetFrameworkMCP.Server.Configuration;
using DotNetFrameworkMCP.Server.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetFrameworkMCP.Server.Executors;

/// <summary>
/// dotnet CLI-based build executor
/// </summary>
public class DotNetBuildExecutor : IBuildExecutor
{
    private readonly ILogger<DotNetBuildExecutor> _logger;
    private readonly McpServerConfiguration _configuration;

    public DotNetBuildExecutor(
        ILogger<DotNetBuildExecutor> logger,
        IOptions<McpServerConfiguration> configuration)
    {
        _logger = logger;
        _configuration = configuration.Value;
    }

    public async Task<BuildResult> ExecuteBuildAsync(
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

            _logger.LogInformation("Using dotnet CLI for build");

            // Build the project using dotnet CLI
            var result = await RunDotNetBuildAsync(projectPath, configuration, platform, restore, cancellationToken);
            
            // Parse the output for errors and warnings
            ParseBuildOutput(result.Output, errors, warnings);

            stopwatch.Stop();

            return new BuildResult
            {
                Success = result.ExitCode == 0,
                Errors = errors,
                Warnings = warnings,
                BuildTime = stopwatch.Elapsed.TotalSeconds,
                Output = TruncateOutput(result.Output, result.ExitCode != 0)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Build failed for project: {ProjectPath}", projectPath);
            stopwatch.Stop();

            return new BuildResult
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
                BuildTime = stopwatch.Elapsed.TotalSeconds,
                Output = TruncateOutput($"Build failed with exception: {ex.Message}\n{ex.StackTrace}", true)
            };
        }
    }

    private async Task<(int ExitCode, string Output)> RunDotNetBuildAsync(
        string projectPath,
        string configuration,
        string platform,
        bool restore,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "build",
            $"\"{projectPath}\"",
            $"--configuration", configuration,
            "--verbosity", "normal"
        };

        // Platform is typically handled differently in dotnet CLI
        // For .NET Framework projects, it's often part of the runtime identifier
        if (!string.IsNullOrEmpty(platform) && platform != "Any CPU")
        {
            arguments.Add($"-p:Platform=\"{platform}\"");
        }

        if (!restore)
        {
            arguments.Add("--no-restore");
        }

        var argumentString = string.Join(" ", arguments);
        _logger.LogDebug("Running: {DotNetPath} {Arguments}", _configuration.DotNetPath, argumentString);

        var psi = new ProcessStartInfo
        {
            FileName = _configuration.DotNetPath,
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

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start dotnet CLI. Make sure .NET SDK is installed and '{_configuration.DotNetPath}' is in PATH.", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for completion with timeout and cancellation support
        var timeoutMs = _configuration.BuildTimeout;
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        
        try
        {
            await process.WaitForExitAsync(combinedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            _logger.LogWarning("Build timed out after {TimeoutMs}ms, killing process", timeoutMs);
            process.Kill();
            throw new TimeoutException($"Build timed out after {timeoutMs}ms");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Build cancelled by user, killing process");
            process.Kill();
            throw;
        }

        return (process.ExitCode, output.ToString());
    }

    private void ParseBuildOutput(string output, List<BuildMessage> errors, List<BuildMessage> warnings)
    {
        if (string.IsNullOrEmpty(output))
            return;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Regex patterns for dotnet build error and warning messages
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

    private string TruncateOutput(string output, bool isFailed)
    {
        const int maxChars = 15000; // Conservative limit to stay under 25k tokens
        
        if (string.IsNullOrEmpty(output) || output.Length <= maxChars)
        {
            return output;
        }

        if (isFailed)
        {
            // For failed builds, prioritize the end of the output (where errors typically appear)
            var lines = output.Split('\n');
            var importantLines = new List<string>();
            var currentLength = 0;
            
            // Add summary line if present
            for (int i = 0; i < Math.Min(10, lines.Length); i++)
            {
                if (lines[i].Contains("Build FAILED") || lines[i].Contains("error") || lines[i].Contains("Error"))
                {
                    importantLines.Add(lines[i]);
                    currentLength += lines[i].Length + 1;
                    break;
                }
            }
            
            // Add errors from the end
            for (int i = lines.Length - 1; i >= 0 && currentLength < maxChars - 100; i--)
            {
                var line = lines[i];
                if (currentLength + line.Length + 1 > maxChars - 100) break;
                
                if (line.Contains("error") || line.Contains("Error") || 
                    line.Contains("warning") || line.Contains("Warning") ||
                    line.Contains("Build FAILED") || line.Contains("Time Elapsed"))
                {
                    importantLines.Insert(importantLines.Count == 0 ? 0 : 1, line);
                    currentLength += line.Length + 1;
                }
            }
            
            var result = string.Join("\n", importantLines);
            if (result.Length < maxChars - 200)
            {
                // Add some context from the end
                var remaining = maxChars - result.Length - 100;
                var endPortion = output.Substring(Math.Max(0, output.Length - remaining));
                result += "\n...\n" + endPortion;
            }
            
            return $"[Output truncated - showing errors and summary]\n{result}";
        }
        else
        {
            // For successful builds, show beginning and end
            var halfMax = maxChars / 2 - 50;
            var start = output.Substring(0, Math.Min(halfMax, output.Length));
            var end = output.Length > halfMax ? output.Substring(output.Length - halfMax) : "";
            
            return start + "\n\n[... middle portion truncated ...]\n\n" + end;
        }
    }
}