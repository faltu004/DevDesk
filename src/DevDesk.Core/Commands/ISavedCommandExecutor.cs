namespace DevDesk.Core.Commands;

/// <summary>
/// Service contract coordinating execution, Windows Job Object containment, output capture,
/// and runtime state for DevDesk saved commands.
/// </summary>
public interface ISavedCommandExecutor
{
    /// <summary>
    /// Executes a saved command within a dedicated Windows Job Object.
    /// Guarded by per-command concurrency lock (only one active execution per SavedCommand).
    /// </summary>
    Task<SavedCommandRunResult> RunCommandAsync(Guid commandId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops execution of an active saved command by terminating its owned process tree.
    /// </summary>
    Task<bool> StopCommandAsync(Guid commandId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forcefully stops all currently active saved command process trees.
    /// Used during application shutdown.
    /// </summary>
    Task StopAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the currently active session snapshot for a command, if one is running.
    /// </summary>
    SavedCommandRunSession? GetActiveSession(Guid commandId);

    /// <summary>
    /// Retrieves the latest session (active or most recently completed) for a command.
    /// </summary>
    SavedCommandRunSession? GetLatestSession(Guid commandId);

    /// <summary>
    /// Retrieves all currently active saved command sessions across the application.
    /// </summary>
    IReadOnlyList<SavedCommandRunSession> GetActiveSessions();

    /// <summary>
    /// Retrieves the bounded log output captured for a specific session.
    /// </summary>
    IReadOnlyList<SavedCommandOutputEvent> GetSessionOutput(Guid commandId, Guid sessionId);

    /// <summary>
    /// Event fired whenever a saved command session changes state (Starting, Running, Succeeded, Failed, Cancelled).
    /// </summary>
    event EventHandler<SavedCommandRunSession>? SessionChanged;

    /// <summary>
    /// Event fired whenever stdout or stderr output is captured from a running command.
    /// </summary>
    event EventHandler<SavedCommandOutputEvent>? OutputReceived;
}
