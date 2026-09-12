using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using DevDesk.Core.Commands;
using DevDesk.Core.Services;
using DevDesk.Infrastructure.Runner;

namespace DevDesk.Infrastructure.Commands;

/// <summary>
/// Production executor coordinating saved command lifecycle.
/// Spawns each command within its own Windows Job Object via suspended process creation.
/// Enforces per-command serialization, safe process-tree termination, monotonically sequenced output capture,
/// and in-memory session retention.
/// </summary>
internal sealed class SavedCommandExecutor : ISavedCommandExecutor, IDisposable
{
    private sealed record ActiveCommandEntry
    {
        public required Guid SessionId { get; init; }
        public required IManagedProcess Process { get; init; }
        public required SavedCommandBoundedBuffer Buffer { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public bool IsStopRequested { get; set; }
        public required SavedCommandRunSession Snapshot { get; set; }
    }

    private readonly ISavedCommandService _commandService;
    private readonly ISavedCommandToolResolver _toolResolver;
    private readonly IProcessLauncher _processLauncher;
    private readonly ILogger<SavedCommandExecutor> _logger;

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _commandLocks = new();
    private readonly ConcurrentDictionary<Guid, ActiveCommandEntry> _activeSessions = new();
    private readonly ConcurrentDictionary<Guid, (SavedCommandRunSession Session, SavedCommandBoundedBuffer Buffer)> _latestSessions = new();
    private bool _disposed;

    public event EventHandler<SavedCommandRunSession>? SessionChanged;
    public event EventHandler<SavedCommandOutputEvent>? OutputReceived;

    internal SavedCommandExecutor(
        ISavedCommandService commandService,
        ISavedCommandToolResolver toolResolver,
        IProcessLauncher processLauncher,
        ILogger<SavedCommandExecutor> logger)
    {
        _commandService = commandService;
        _toolResolver = toolResolver;
        _processLauncher = processLauncher;
        _logger = logger;
    }

    public async Task<SavedCommandRunResult> RunCommandAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        var sem = _commandLocks.GetOrAdd(commandId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(cancellationToken);
        try
        {
            // Concurrency guard: Only one active execution per SavedCommand
            if (_activeSessions.TryGetValue(commandId, out var existingActive) && existingActive.Snapshot.IsActive)
            {
                return SavedCommandRunResult.Failed($"Command '{existingActive.Snapshot.CommandName}' is already running.");
            }

            var command = await _commandService.GetCommandByIdAsync(commandId, cancellationToken);
            if (command is null)
            {
                return SavedCommandRunResult.Failed($"SavedCommand with ID '{commandId}' was not found.");
            }

            if (!command.IsEnabled)
            {
                return SavedCommandRunResult.Failed($"Command '{command.Name}' is disabled.");
            }

            // 1. Resolve and validate working directory
            string workingDirectory;
            try
            {
                workingDirectory = await _commandService.ResolveWorkingDirectoryAsync(command, cancellationToken);
            }
            catch (Exception ex)
            {
                return SavedCommandRunResult.Failed($"Failed to resolve working directory: {ex.Message}");
            }

            if (!Directory.Exists(workingDirectory))
            {
                return SavedCommandRunResult.Failed($"Working directory does not exist: {workingDirectory}");
            }

            // 2. Resolve executable tool and validate arguments
            var toolResolution = _toolResolver.ResolveTool(command.Executable, command.Arguments);
            if (!toolResolution.Success)
            {
                return SavedCommandRunResult.Failed(toolResolution.ErrorMessage ?? "Failed to resolve executable tool.");
            }

            var sessionId = Guid.NewGuid();
            var buffer = new SavedCommandBoundedBuffer();
            var cts = new CancellationTokenSource();
            long sequenceNumber = 0;

            var startingSession = new SavedCommandRunSession
            {
                SessionId = sessionId,
                CommandId = command.Id,
                CommandName = command.Name,
                ExecutablePath = toolResolution.ExecutablePath,
                Arguments = command.Arguments,
                WorkingDirectory = workingDirectory,
                State = SavedCommandExecutionState.Starting,
                StartedAtUtc = DateTimeOffset.UtcNow
            };

            var entry = new ActiveCommandEntry
            {
                SessionId = sessionId,
                Process = null!, // assigned below
                Buffer = buffer,
                Cts = cts,
                Snapshot = startingSession
            };

            _activeSessions[commandId] = entry;
            _latestSessions[commandId] = (startingSession, buffer);

            PublishSessionChange(startingSession);

            // For dotnet/msbuild-style commands, set MSBUILDDISABLENODEREUSE=1 strictly on the child process environment
            Dictionary<string, string>? environmentVariables = null;
            var fileName = Path.GetFileName(toolResolution.ExecutablePath);
            if (fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("msbuild.exe", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("msbuild", StringComparison.OrdinalIgnoreCase))
            {
                environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MSBUILDDISABLENODEREUSE"] = "1"
                };
            }

            IManagedProcess process;
            try
            {
                var launchConfig = new ProcessLaunchConfiguration
                {
                    ExecutablePath = toolResolution.ExecutablePath,
                    Arguments = command.Arguments,
                    WorkingDirectory = workingDirectory,
                    IsCmdShim = toolResolution.IsCmdShim,
                    EnvironmentVariables = environmentVariables
                };

                process = _processLauncher.Launch(launchConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch process for saved command '{Name}'", command.Name);

                var failedSnapshot = startingSession with
                {
                    State = SavedCommandExecutionState.Failed,
                    ErrorMessage = $"Launch failed: {ex.Message}",
                    ExitedAtUtc = DateTimeOffset.UtcNow
                };

                _activeSessions.TryRemove(commandId, out _);
                _latestSessions[commandId] = (failedSnapshot, buffer);
                PublishSessionChange(failedSnapshot);

                return SavedCommandRunResult.Failed(failedSnapshot.ErrorMessage);
            }

            var activeEntry = entry with { Process = process };
            var runningSnapshot = startingSession with
            {
                State = SavedCommandExecutionState.Running,
                ProcessId = process.ProcessId
            };
            activeEntry.Snapshot = runningSnapshot;
            _activeSessions[commandId] = activeEntry;
            _latestSessions[commandId] = (runningSnapshot, buffer);

            PublishSessionChange(runningSnapshot);

            // Hook process streams with sequence numbers
            process.StandardOutputReceived += (_, line) =>
            {
                long seq = Interlocked.Increment(ref sequenceNumber);
                var evt = buffer.Add(sessionId, command.Id, line, isError: false, seq);
                OutputReceived?.Invoke(this, evt);
            };

            process.StandardErrorReceived += (_, line) =>
            {
                long seq = Interlocked.Increment(ref sequenceNumber);
                var evt = buffer.Add(sessionId, command.Id, line, isError: true, seq);
                OutputReceived?.Invoke(this, evt);
            };

            // Monitor exit asynchronously
            _ = MonitorProcessExitAsync(commandId, sessionId, process, activeEntry, buffer, cts.Token);

            return SavedCommandRunResult.Succeeded(sessionId, process.ProcessId);
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task MonitorProcessExitAsync(
        Guid commandId,
        Guid sessionId,
        IManagedProcess process,
        ActiveCommandEntry entry,
        SavedCommandBoundedBuffer buffer,
        CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Ignore wait errors
        }

        // Wait a brief moment to guarantee all piped output lines have been fully dispatched
        await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);

        var sem = _commandLocks.GetOrAdd(commandId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            // Protect against stale callbacks from earlier sessions
            if (!_activeSessions.TryGetValue(commandId, out var currentActive) || currentActive.SessionId != sessionId)
            {
                return;
            }

            int? exitCode = process.ExitCode;
            SavedCommandExecutionState finalState;

            if (entry.IsStopRequested)
            {
                finalState = SavedCommandExecutionState.Cancelled;
            }
            else if (exitCode == 0)
            {
                finalState = SavedCommandExecutionState.Succeeded;
            }
            else
            {
                finalState = SavedCommandExecutionState.Failed;
            }

            var finalSnapshot = entry.Snapshot with
            {
                State = finalState,
                ExitCode = exitCode,
                ExitedAtUtc = DateTimeOffset.UtcNow,
                ErrorMessage = finalState == SavedCommandExecutionState.Failed
                    ? $"Process exited with code {exitCode}."
                    : null
            };

            _activeSessions.TryRemove(commandId, out _);
            _latestSessions[commandId] = (finalSnapshot, buffer);

            PublishSessionChange(finalSnapshot);

            try
            {
                process.Dispose();
                entry.Cts.Dispose();
            }
            catch
            {
            }
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<bool> StopCommandAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        var sem = _commandLocks.GetOrAdd(commandId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(cancellationToken);
        try
        {
            if (!_activeSessions.TryGetValue(commandId, out var active))
            {
                return false;
            }

            active.IsStopRequested = true;
            try
            {
                active.Cts.Cancel();
            }
            catch
            {
            }

            try
            {
                active.Process.KillEntireProcessTree();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception while terminating process tree for command session {SessionId}", active.SessionId);
            }

            return true;
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        var activeCommandIds = _activeSessions.Keys.ToList();
        var tasks = activeCommandIds.Select(id => StopCommandAsync(id, cancellationToken));
        await Task.WhenAll(tasks);
    }

    public SavedCommandRunSession? GetActiveSession(Guid commandId)
    {
        return _activeSessions.TryGetValue(commandId, out var entry) && entry.Snapshot.IsActive
            ? entry.Snapshot
            : null;
    }

    public SavedCommandRunSession? GetLatestSession(Guid commandId)
    {
        if (_activeSessions.TryGetValue(commandId, out var active))
        {
            return active.Snapshot;
        }

        if (_latestSessions.TryGetValue(commandId, out var latest))
        {
            return latest.Session;
        }

        return null;
    }

    public IReadOnlyList<SavedCommandRunSession> GetActiveSessions()
    {
        return _activeSessions.Values
            .Where(e => e.Snapshot.IsActive)
            .Select(e => e.Snapshot)
            .ToList();
    }

    public IReadOnlyList<SavedCommandOutputEvent> GetSessionOutput(Guid commandId, Guid sessionId)
    {
        if (_activeSessions.TryGetValue(commandId, out var active) && active.SessionId == sessionId)
        {
            return active.Buffer.GetSnapshot();
        }

        if (_latestSessions.TryGetValue(commandId, out var latest) && latest.Session.SessionId == sessionId)
        {
            return latest.Buffer.GetSnapshot();
        }

        return Array.Empty<SavedCommandOutputEvent>();
    }

    private void PublishSessionChange(SavedCommandRunSession session)
    {
        try
        {
            SessionChanged?.Invoke(this, session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in SavedCommandExecutor.SessionChanged listener");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            StopAllAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
        }
    }
}
