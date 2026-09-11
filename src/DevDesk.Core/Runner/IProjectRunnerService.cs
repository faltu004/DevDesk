namespace DevDesk.Core.Runner;

/// <summary>
/// Service contract coordinating execution, process tree lifecycle, and output capture
/// for DevDesk-managed developer projects.
/// </summary>
public interface IProjectRunnerService
{
    /// <summary>
    /// Starts execution of a project using its configured RunCommand.
    /// Synchronized per-project to prevent concurrent starts.
    /// </summary>
    Task<ProjectRunResult> StartProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forcefully terminates the DevDesk-owned process tree for an active project session.
    /// Synchronized per-project.
    /// </summary>
    Task<ProjectStopResult> StopProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the DevDesk-owned process tree, confirms complete exit, and re-spawns the project.
    /// Synchronized per-project.
    /// </summary>
    Task<ProjectRunResult> RestartProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops all currently active DevDesk-managed project execution sessions.
    /// Used during controlled application shutdown.
    /// </summary>
    Task<IReadOnlyList<ProjectStopResult>> StopAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current point-in-time session snapshot for a project, if one exists in memory.
    /// </summary>
    ProjectRunSession? GetSession(Guid projectId);

    /// <summary>
    /// Retrieves all currently active (Starting, Running, Stopping) DevDesk project execution sessions.
    /// </summary>
    IReadOnlyList<ProjectRunSession> GetActiveSessions();

    /// <summary>
    /// Retrieves an authoritative immutable snapshot of all live processes currently belonging to active DevDesk sessions
    /// (including both the root process and any descendant processes belonging to the session Job Object).
    /// </summary>
    IReadOnlyList<ManagedProcessIdentity> GetManagedProcesses();

    /// <summary>
    /// Retrieves recent session snapshots for a project (current session + at most 2 completed sessions).
    /// </summary>
    IReadOnlyList<ProjectRunSession> GetRecentSessions(Guid projectId);

    /// <summary>
    /// Retrieves a bounded log snapshot for a specific session ID belonging to a project.
    /// Returns an immutable list of events.
    /// </summary>
    IReadOnlyList<ProcessOutputEvent> GetSessionLogs(Guid projectId, Guid sessionId);

    /// <summary>
    /// Event fired whenever a session state transitions (Starting, Running, Stopping, Exited, Failed).
    /// Safe for non-UI consumption; UI handlers must marshal to their dispatcher.
    /// </summary>
    event EventHandler<ProjectRunSession>? SessionChanged;

    /// <summary>
    /// Event fired whenever stdout or stderr output is captured from a running project.
    /// Safe for non-UI consumption; foundation for Phase 8 Live Logs.
    /// </summary>
    event EventHandler<ProcessOutputEvent>? OutputReceived;
}
