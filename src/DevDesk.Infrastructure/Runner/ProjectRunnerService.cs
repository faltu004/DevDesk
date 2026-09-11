using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;
using DevDesk.Core.Runner;
using DevDesk.Core.Services;

namespace DevDesk.Infrastructure.Runner;

/// <summary>
/// Production project execution coordinator.
/// Enforces per-project serialization, strict process ownership, process-tree termination,
/// bounded output buffering, and safe Windows package-manager shim execution.
/// </summary>
public sealed class ProjectRunnerService : IProjectRunnerService, IDisposable
{
    private readonly IProjectService _projectService;
    private readonly IProcessLauncher _processLauncher;
    private readonly IRunnerToolLocator _toolLocator;
    private readonly ILogger<ProjectRunnerService> _logger;

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _projectLocks = new();
    private readonly ConcurrentDictionary<Guid, ActiveProcessEntry> _activeSessions = new();
    private readonly ConcurrentDictionary<Guid, LinkedList<CompletedSessionRecord>> _completedSessions = new();
    private readonly object _historyLock = new();
    private bool _disposed;

    public event EventHandler<ProjectRunSession>? SessionChanged;
    public event EventHandler<ProcessOutputEvent>? OutputReceived;

    internal ProjectRunnerService(
        IProjectService projectService,
        IProcessLauncher processLauncher,
        IRunnerToolLocator toolLocator,
        ILogger<ProjectRunnerService> logger)
    {
        _projectService = projectService;
        _processLauncher = processLauncher;
        _toolLocator = toolLocator;
        _logger = logger;
    }

    public async Task<ProjectRunResult> StartProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var sem = GetProjectLock(projectId);
        await sem.WaitAsync(cancellationToken);
        try
        {
            return await StartProjectInternalAsync(projectId, cancellationToken);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<ProjectStopResult> StopProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var sem = GetProjectLock(projectId);
        await sem.WaitAsync(cancellationToken);
        try
        {
            return await StopProjectInternalAsync(projectId, cancellationToken);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<ProjectRunResult> RestartProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var sem = GetProjectLock(projectId);
        await sem.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Restarting project {ProjectId}", projectId);

            // 1. Forcefully stop owned process tree if active and wait for confirmed exit
            var stopResult = await StopProjectInternalAsync(projectId, cancellationToken);
            if (!stopResult.Success)
            {
                _logger.LogWarning("Stop failed during restart of project {ProjectId}: {Error}", projectId, stopResult.ErrorMessage);
                return ProjectRunResult.Failed($"Failed to stop existing process during restart: {stopResult.ErrorMessage}");
            }

            // 2. Start project with latest configuration
            return await StartProjectInternalAsync(projectId, cancellationToken);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<IReadOnlyList<ProjectStopResult>> StopAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ProjectStopResult>();
        var activeProjectIds = _activeSessions
            .Where(kvp => kvp.Value.CurrentSnapshot.IsActive)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var projectId in activeProjectIds)
        {
            try
            {
                var res = await StopProjectAsync(projectId, cancellationToken);
                results.Add(res);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping project {ProjectId} during StopAll", projectId);
                results.Add(ProjectStopResult.Failed(projectId, ex.Message));
            }
        }

        return results;
    }

    public ProjectRunSession? GetSession(Guid projectId)
    {
        return _activeSessions.TryGetValue(projectId, out var entry)
            ? entry.CurrentSnapshot
            : null;
    }

    public IReadOnlyList<ProjectRunSession> GetActiveSessions()
    {
        return _activeSessions.Values
            .Select(e => e.CurrentSnapshot)
            .Where(s => s.IsActive)
            .ToList();
    }

    public IReadOnlyList<ProjectRunSession> GetRecentSessions(Guid projectId)
    {
        var list = new List<ProjectRunSession>();
        if (_activeSessions.TryGetValue(projectId, out var active))
        {
            list.Add(active.CurrentSnapshot);
        }

        lock (_historyLock)
        {
            if (_completedSessions.TryGetValue(projectId, out var completed))
            {
                foreach (var record in completed.Reverse())
                {
                    if (list.All(s => s.SessionId != record.Session.SessionId))
                    {
                        list.Add(record.Session);
                    }
                }
            }
        }

        return list;
    }

    public IReadOnlyList<ProcessOutputEvent> GetSessionLogs(Guid projectId, Guid sessionId)
    {
        if (_activeSessions.TryGetValue(projectId, out var active) && active.SessionId == sessionId)
        {
            return active.LogBuffer.GetSnapshot();
        }

        lock (_historyLock)
        {
            if (_completedSessions.TryGetValue(projectId, out var completed))
            {
                foreach (var record in completed)
                {
                    if (record.Session.SessionId == sessionId)
                    {
                        return record.LogBuffer.GetSnapshot();
                    }
                }
            }
        }

        return Array.Empty<ProcessOutputEvent>();
    }

    private async Task<ProjectRunResult> StartProjectInternalAsync(Guid projectId, CancellationToken cancellationToken)
    {
        // 1. Prevent duplicate starts for an already active session
        if (_activeSessions.TryGetValue(projectId, out var existing) && existing.CurrentSnapshot.IsActive)
        {
            _logger.LogWarning("Project {ProjectId} already has an active session (PID {Pid})", projectId, existing.CurrentSnapshot.ProcessId);
            return ProjectRunResult.Failed(
                $"Project '{existing.CurrentSnapshot.ProjectName}' is already running (PID {existing.CurrentSnapshot.ProcessId}).",
                existing.CurrentSnapshot);
        }

        // 2. Fetch project configuration
        var project = await _projectService.GetProjectByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return ProjectRunResult.Failed($"Project with ID '{projectId}' was not found.");
        }

        // 3. Validate project path on disk
        if (!Directory.Exists(project.Path))
        {
            return ProjectRunResult.Failed($"Project directory does not exist on disk: {project.Path}");
        }

        // 4. Validate and parse RunCommand according to Phase 7 allowlist
        var validation = CommandShapeValidator.ValidateAndParse(project.RunCommand);
        if (!validation.IsValid)
        {
            return ProjectRunResult.Failed(validation.ErrorMessage ?? "Invalid run command format.");
        }

        // 5. Resolve concrete tool or trusted .cmd shim path
        string? resolvedToolPath = _toolLocator.ResolveToolPath(validation.Tool, validation.IsCmdShim);
        if (string.IsNullOrWhiteSpace(resolvedToolPath))
        {
            string expectedTarget = validation.IsCmdShim ? $"{validation.Tool}.cmd" : $"{validation.Tool}.exe";
            return ProjectRunResult.Failed(
                $"Could not locate '{expectedTarget}' on this machine. Please verify that {validation.Tool} is installed and present in PATH.");
        }

        var sessionId = Guid.NewGuid();
        var logBuffer = new BoundedLogBuffer();

        var startingSnapshot = new ProjectRunSession
        {
            SessionId = sessionId,
            ProjectId = project.Id,
            ProjectName = project.Name,
            CommandText = project.RunCommand ?? string.Empty,
            ExecutablePath = resolvedToolPath,
            Arguments = validation.Arguments,
            State = ProjectRunState.Starting,
            StartedAt = DateTimeOffset.UtcNow
        };

        PublishSessionChange(startingSnapshot);

        IManagedProcess process;
        try
        {
            var config = new ProcessLaunchConfiguration
            {
                WorkingDirectory = project.Path,
                ExecutablePath = resolvedToolPath,
                Arguments = validation.Arguments,
                IsCmdShim = validation.IsCmdShim
            };

            process = _processLauncher.Launch(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to spawn process for project {Name}", project.Name);

            var failedSnapshot = startingSnapshot with
            {
                State = ProjectRunState.Failed,
                TerminationReason = ProjectTerminationReason.LaunchFailed,
                ErrorMessage = $"Failed to start process: {ex.Message}",
                ExitedAt = DateTimeOffset.UtcNow
            };

            RecordCompletedSession(project.Id, failedSnapshot, logBuffer);
            PublishSessionChange(failedSnapshot);
            return ProjectRunResult.Failed(failedSnapshot.ErrorMessage, failedSnapshot);
        }

        var runningSnapshot = startingSnapshot with
        {
            ProcessId = process.ProcessId,
            State = ProjectRunState.Running
        };

        var entry = new ActiveProcessEntry
        {
            SessionId = sessionId,
            ProjectId = project.Id,
            Process = process,
            LogBuffer = logBuffer,
            CurrentSnapshot = runningSnapshot
        };

        _activeSessions[project.Id] = entry;

        // Hook stdout/stderr output redirection
        process.StandardOutputReceived += (_, text) =>
        {
            long seq = entry.NextSequenceNumber();
            var evt = logBuffer.Add(sessionId, project.Id, text, isError: false, sequenceNumber: seq);
            OutputReceived?.Invoke(this, evt);
        };

        process.StandardErrorReceived += (_, text) =>
        {
            long seq = entry.NextSequenceNumber();
            var evt = logBuffer.Add(sessionId, project.Id, text, isError: true, sequenceNumber: seq);
            OutputReceived?.Invoke(this, evt);
        };

        // Hook natural process exit with SessionId guard
        process.Exited += (_, exitCode) =>
        {
            HandleProcessExited(sessionId, project.Id, exitCode);
        };

        PublishSessionChange(runningSnapshot);
        _logger.LogInformation("Project {Name} started successfully (PID {Pid})", project.Name, runningSnapshot.ProcessId);

        return ProjectRunResult.Succeeded(runningSnapshot);
    }

    private async Task<ProjectStopResult> StopProjectInternalAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (!_activeSessions.TryGetValue(projectId, out var entry) || !entry.CurrentSnapshot.IsActive)
        {
            return ProjectStopResult.Succeeded(projectId);
        }

        var stoppingSnapshot = entry.CurrentSnapshot with
        {
            State = ProjectRunState.Stopping
        };
        entry.CurrentSnapshot = stoppingSnapshot;
        PublishSessionChange(stoppingSnapshot);

        _logger.LogInformation("Forcefully terminating process tree for project {ProjectId} (PID {Pid})",
            projectId, stoppingSnapshot.ProcessId);

        // Forcefully terminate DevDesk-owned process tree
        entry.Process.KillEntireProcessTree();

        // Await confirmed process exit with timeout
        using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(exitCts.Token, cancellationToken);
        bool waitSucceeded = false;
        try
        {
            await entry.Process.WaitForExitAsync(linkedCts.Token);
            waitSucceeded = entry.Process.HasExited;
        }
        catch (OperationCanceledException)
        {
            waitSucceeded = entry.Process.HasExited;
        }

        if (!waitSucceeded)
        {
            _logger.LogError("Failed to terminate process tree for project {ProjectId} (PID {Pid}) within timeout",
                projectId, stoppingSnapshot.ProcessId);

            var failedStopSnapshot = entry.CurrentSnapshot with
            {
                State = ProjectRunState.Running,
                ErrorMessage = "Failed to stop project: process termination timed out."
            };
            entry.CurrentSnapshot = failedStopSnapshot;
            PublishSessionChange(failedStopSnapshot);

            return ProjectStopResult.Failed(projectId, "Failed to terminate project process tree: operation timed out.");
        }

        int? exitCode = entry.Process.ExitCode;

        var exitedSnapshot = entry.CurrentSnapshot with
        {
            State = ProjectRunState.Exited,
            TerminationReason = ProjectTerminationReason.StoppedByDevDesk,
            ExitedAt = DateTimeOffset.UtcNow,
            ExitCode = exitCode,
            ErrorMessage = null
        };

        entry.CurrentSnapshot = exitedSnapshot;
        if (entry.TryMarkDisposed())
        {
            entry.Process.Dispose();
        }

        _activeSessions.TryRemove(projectId, out _);
        RecordCompletedSession(projectId, exitedSnapshot, entry.LogBuffer);
        PublishSessionChange(exitedSnapshot);

        _logger.LogInformation("Project {ProjectId} stopped", projectId);
        return ProjectStopResult.Succeeded(projectId);
    }

    private void HandleProcessExited(Guid sessionId, Guid projectId, int exitCode)
    {
        if (!_activeSessions.TryGetValue(projectId, out var entry))
        {
            return;
        }

        // Stale session guard: ignore exit callback if session was replaced by a newer session
        if (entry.SessionId != sessionId)
        {
            _logger.LogDebug("Ignoring exit event for stale session {SessionId}", sessionId);
            return;
        }

        // If StopProjectInternalAsync is already coordinating the stop sequence, do not preempt or race it
        if (entry.CurrentSnapshot.State == ProjectRunState.Stopping)
        {
            _logger.LogDebug("Process exit received during Stop for session {SessionId}; delegating completion to Stop coordinator.", sessionId);
            return;
        }

        if (!entry.TryMarkDisposed())
        {
            return;
        }

        var exitedSnapshot = entry.CurrentSnapshot with
        {
            State = ProjectRunState.Exited,
            TerminationReason = ProjectTerminationReason.NaturalExit,
            ExitedAt = DateTimeOffset.UtcNow,
            ExitCode = exitCode
        };

        entry.CurrentSnapshot = exitedSnapshot;
        entry.Process.Dispose();

        _activeSessions.TryRemove(projectId, out _);
        RecordCompletedSession(projectId, exitedSnapshot, entry.LogBuffer);
        PublishSessionChange(exitedSnapshot);

        _logger.LogInformation("Process for project {ProjectId} exited with code {Code}", projectId, exitCode);
    }

    private void RecordCompletedSession(Guid projectId, ProjectRunSession session, BoundedLogBuffer logBuffer)
    {
        lock (_historyLock)
        {
            var list = _completedSessions.GetOrAdd(projectId, _ => new LinkedList<CompletedSessionRecord>());
            var node = list.First;
            while (node != null)
            {
                if (node.Value.Session.SessionId == session.SessionId)
                {
                    node.Value = new CompletedSessionRecord(session, node.Value.LogBuffer);
                    return;
                }
                node = node.Next;
            }

            list.AddLast(new CompletedSessionRecord(session, logBuffer));
            while (list.Count > 2)
            {
                list.RemoveFirst();
            }
        }
    }

    private void PublishSessionChange(ProjectRunSession session)
    {
        SessionChanged?.Invoke(this, session);
    }

    private SemaphoreSlim GetProjectLock(Guid projectId)
    {
        return _projectLocks.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var entry in _activeSessions.Values)
        {
            try
            {
                entry.Process.Dispose();
            }
            catch
            {
                // Best effort disposal
            }
        }

        _activeSessions.Clear();
        lock (_historyLock)
        {
            _completedSessions.Clear();
        }

        foreach (var sem in _projectLocks.Values)
        {
            sem.Dispose();
        }

        _projectLocks.Clear();
    }

    private sealed class ActiveProcessEntry
    {
        private int _disposedState;
        private long _sequenceCounter;

        public required Guid SessionId { get; init; }
        public required Guid ProjectId { get; init; }
        public required IManagedProcess Process { get; init; }
        public required BoundedLogBuffer LogBuffer { get; init; }
        public required ProjectRunSession CurrentSnapshot { get; set; }

        public long NextSequenceNumber() => Interlocked.Increment(ref _sequenceCounter);
        public bool TryMarkDisposed() => Interlocked.Exchange(ref _disposedState, 1) == 0;
    }

    private sealed record CompletedSessionRecord(ProjectRunSession Session, BoundedLogBuffer LogBuffer);
}
