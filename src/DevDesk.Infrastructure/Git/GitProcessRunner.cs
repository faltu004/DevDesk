using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DevDesk.Infrastructure.Git;

/// <summary>
/// Executes Git processes securely with non-interactive environment, structured arguments,
/// concurrent stdout/stderr streaming, memory bounds, and isolated timeout/cancellation handling.
/// </summary>
public sealed class GitProcessRunner : IGitProcessRunner
{
    private readonly IGitToolLocator _toolLocator;
    private readonly ILogger<GitProcessRunner> _logger;

    public GitProcessRunner(IGitToolLocator toolLocator, ILogger<GitProcessRunner> logger)
    {
        _toolLocator = toolLocator ?? throw new ArgumentNullException(nameof(toolLocator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GitProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        int maxOutputBytes = 10 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_toolLocator.TryLocateGit(out var gitPath))
        {
            return new GitProcessResult(
                ExitCode: -1,
                StandardOutput: string.Empty,
                StandardError: "Git executable not found on system PATH",
                IsTimedOut: false,
                IsOutputTruncated: false);
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);

        var psi = new ProcessStartInfo
        {
            FileName = gitPath,
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Non-interactive and deterministic locale environment
        psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
        psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
        psi.EnvironmentVariables["GIT_PAGER"] = "cat";
        psi.EnvironmentVariables["LC_ALL"] = "C";

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                return new GitProcessResult(
                    ExitCode: -1,
                    StandardOutput: string.Empty,
                    StandardError: "Failed to start Git process",
                    IsTimedOut: false,
                    IsOutputTruncated: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Git process {FileName}", gitPath);
            return new GitProcessResult(
                ExitCode: -1,
                StandardOutput: string.Empty,
                StandardError: ex.Message,
                IsTimedOut: false,
                IsOutputTruncated: false);
        }

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var isTruncated = false;
        using var truncationCts = new CancellationTokenSource();

        var stdoutTask = Task.Run(async () =>
        {
            var buffer = new char[4096];
            var totalRead = 0;
            try
            {
                while (true)
                {
                    var read = await process.StandardOutput.ReadAsync(buffer.AsMemory(0, buffer.Length), CancellationToken.None);
                    if (read <= 0) break;

                    totalRead += read;
                    if (totalRead > maxOutputBytes)
                    {
                        isTruncated = true;
                        try { truncationCts.Cancel(); } catch { }
                        break;
                    }

                    stdoutBuilder.Append(buffer, 0, read);
                }
            }
            catch
            {
                // Stream closed or aborted
            }
        });

        var stderrTask = Task.Run(async () =>
        {
            var buffer = new char[2048];
            var totalRead = 0;
            try
            {
                while (true)
                {
                    var read = await process.StandardError.ReadAsync(buffer.AsMemory(0, buffer.Length), CancellationToken.None);
                    if (read <= 0) break;

                    totalRead += read;
                    if (totalRead > 256 * 1024) break; // Bound stderr to 256 KB

                    stderrBuilder.Append(buffer, 0, read);
                }
            }
            catch
            {
                // Stream closed or aborted
            }
        });

        var exitTask = process.WaitForExitAsync(CancellationToken.None);

        using var timeoutCts = new CancellationTokenSource();
        var delayTask = Task.Delay(effectiveTimeout, timeoutCts.Token);

        var cancellationTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => cancellationTcs.TrySetResult());

        var truncationTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var truncReg = truncationCts.Token.Register(() => truncationTcs.TrySetResult());

        var finishedTask = await Task.WhenAny(exitTask, delayTask, cancellationTcs.Task, truncationTcs.Task);

        if (finishedTask == cancellationTcs.Task)
        {
            // Caller cancelled
            TryKillProcess(process);
            await SafeDrainAsync(stdoutTask, stderrTask);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (finishedTask == truncationTcs.Task || isTruncated)
        {
            // Output bounded exceeded
            TryKillProcess(process);
            await SafeDrainAsync(stdoutTask, stderrTask);
            return new GitProcessResult(
                ExitCode: -1,
                StandardOutput: string.Empty,
                StandardError: $"Git output exceeded memory limit of {maxOutputBytes} bytes",
                IsTimedOut: false,
                IsOutputTruncated: true);
        }

        if (finishedTask == delayTask)
        {
            // Command timed out
            TryKillProcess(process);
            await SafeDrainAsync(stdoutTask, stderrTask);
            return new GitProcessResult(
                ExitCode: -1,
                StandardOutput: stdoutBuilder.ToString(),
                StandardError: $"Git operation timed out after {effectiveTimeout.TotalMilliseconds}ms",
                IsTimedOut: true,
                IsOutputTruncated: false);
        }

        // Process finished normally
        timeoutCts.Cancel();
        await SafeDrainAsync(stdoutTask, stderrTask);

        return new GitProcessResult(
            ExitCode: process.ExitCode,
            StandardOutput: stdoutBuilder.ToString(),
            StandardError: stderrBuilder.ToString(),
            IsTimedOut: false,
            IsOutputTruncated: false);
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Process already exited or access denied
        }
    }

    private static async Task SafeDrainAsync(Task stdoutTask, Task stderrTask)
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // Timeout or drain aborted
        }
    }
}
