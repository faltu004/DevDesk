using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using DevDesk.Core.Common;
using DevDesk.Core.Launchers;

namespace DevDesk.Infrastructure.Launchers;

/// <summary>
/// Production implementation of ILauncherService providing secure, injection-safe operations
/// to open project directories in external developer tools.
/// </summary>
public sealed class LauncherService : ILauncherService
{
    private readonly IExternalToolLocator _toolLocator;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<LauncherService> _logger;

    internal LauncherService(
        IExternalToolLocator toolLocator,
        IProcessRunner processRunner,
        ILogger<LauncherService> logger)
    {
        _toolLocator = toolLocator;
        _processRunner = processRunner;
        _logger = logger;
    }

    public Task<LaunchResult> OpenInVsCodeAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validationResult = ValidateProjectDirectory(projectPath, out var normalizedPath);
        if (validationResult is not null)
        {
            return Task.FromResult(validationResult);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var vsCodePath = _toolLocator.FindVsCodeExecutable();
        if (string.IsNullOrEmpty(vsCodePath))
        {
            _logger.LogInformation("VS Code launch requested, but Code.exe was not found on this system");
            return Task.FromResult(LaunchResult.Failed(
                LaunchFailureReason.ApplicationNotFound,
                "Visual Studio Code was not found on this computer."));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = vsCodePath,
            WorkingDirectory = normalizedPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(normalizedPath);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var started = _processRunner.Start(startInfo);
            if (!started)
            {
                _logger.LogWarning("Process runner failed to start VS Code binary at '{Exe}' for '{Path}'", vsCodePath, normalizedPath);
                return Task.FromResult(LaunchResult.Failed(
                    LaunchFailureReason.ProcessStartFailed,
                    "Failed to launch Visual Studio Code. Please check system permissions."));
            }

            _logger.LogInformation("Successfully launched VS Code for '{Path}'", normalizedPath);
            return Task.FromResult(LaunchResult.Ok());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception starting VS Code binary at '{Exe}' for '{Path}'", vsCodePath, normalizedPath);
            return Task.FromResult(LaunchResult.Failed(
                LaunchFailureReason.ProcessStartFailed,
                "Failed to launch Visual Studio Code. Please check system permissions."));
        }
    }

    public Task<LaunchResult> OpenInExplorerAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validationResult = ValidateProjectDirectory(projectPath, out var normalizedPath);
        if (validationResult is not null)
        {
            return Task.FromResult(validationResult);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var explorerPath = _toolLocator.FindExplorerExecutable();
        if (string.IsNullOrEmpty(explorerPath))
        {
            _logger.LogWarning("Windows Explorer executable was not found on this system");
            return Task.FromResult(LaunchResult.Failed(
                LaunchFailureReason.ApplicationNotFound,
                "Windows Explorer could not be found on this computer."));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = explorerPath,
            WorkingDirectory = normalizedPath,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(normalizedPath);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var started = _processRunner.Start(startInfo);
            if (!started)
            {
                _logger.LogWarning("Process runner failed to start Windows Explorer binary at '{Exe}' for '{Path}'", explorerPath, normalizedPath);
                return Task.FromResult(LaunchResult.Failed(
                    LaunchFailureReason.ProcessStartFailed,
                    "Failed to open Windows Explorer. Please check system permissions."));
            }

            _logger.LogInformation("Successfully launched Explorer for '{Path}'", normalizedPath);
            return Task.FromResult(LaunchResult.Ok());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception starting Explorer binary at '{Exe}' for '{Path}'", explorerPath, normalizedPath);
            return Task.FromResult(LaunchResult.Failed(
                LaunchFailureReason.ProcessStartFailed,
                "Failed to open Windows Explorer. Please check system permissions."));
        }
    }

    public Task<LaunchResult> OpenTerminalAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validationResult = ValidateProjectDirectory(projectPath, out var normalizedPath);
        if (validationResult is not null)
        {
            return Task.FromResult(validationResult);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var terminalTarget = _toolLocator.FindPreferredTerminal();
        if (terminalTarget is null || string.IsNullOrEmpty(terminalTarget.ExecutablePath))
        {
            _logger.LogWarning("No supported terminal (Windows Terminal, PowerShell) found on this system");
            return Task.FromResult(LaunchResult.Failed(
                LaunchFailureReason.ApplicationNotFound,
                "No supported terminal (Windows Terminal, PowerShell) could be found on this computer."));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = terminalTarget.ExecutablePath,
            WorkingDirectory = normalizedPath,
            UseShellExecute = false,
            CreateNoWindow = false // Must open visibly for user interaction
        };

        // Configure deterministic terminal arguments without executing any project commands
        switch (terminalTarget.Type)
        {
            case TerminalType.WindowsTerminal:
                // -d starts the default shell profile in the target directory
                startInfo.ArgumentList.Add("-d");
                startInfo.ArgumentList.Add(normalizedPath);
                break;

            case TerminalType.PowerShell7:
                // Open interactive pwsh in the project directory
                break;

            case TerminalType.WindowsPowerShell:
                // Clean startup banner; interactive shell in working directory
                startInfo.ArgumentList.Add("-NoLogo");
                break;

            default:
                break;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var started = _processRunner.Start(startInfo);
            if (!started)
            {
                _logger.LogWarning("Process runner failed to start terminal binary at '{Exe}' for '{Path}'", terminalTarget.ExecutablePath, normalizedPath);
                return Task.FromResult(LaunchResult.Failed(
                    LaunchFailureReason.ProcessStartFailed,
                    "Failed to open the terminal. Please check system permissions."));
            }

            _logger.LogInformation("Successfully launched terminal ({Type}) for '{Path}'", terminalTarget.Type, normalizedPath);
            return Task.FromResult(LaunchResult.Ok());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception starting terminal binary at '{Exe}' for '{Path}'", terminalTarget.ExecutablePath, normalizedPath);
            return Task.FromResult(LaunchResult.Failed(
                LaunchFailureReason.ProcessStartFailed,
                "Failed to open the terminal. Please check system permissions."));
        }
    }

    private static LaunchResult? ValidateProjectDirectory(string? projectPath, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return LaunchResult.Failed(
                LaunchFailureReason.InvalidPath,
                "A project path was not specified.");
        }

        try
        {
            normalizedPath = PathHelper.NormalizePath(projectPath);
        }
        catch (Exception)
        {
            return LaunchResult.Failed(
                LaunchFailureReason.InvalidPath,
                "The project path is not a valid filesystem path.");
        }

        if (!Directory.Exists(normalizedPath))
        {
            return LaunchResult.Failed(
                LaunchFailureReason.DirectoryNotFound,
                "The project folder could not be found.");
        }

        return null;
    }
}
