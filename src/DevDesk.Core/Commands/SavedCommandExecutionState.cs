namespace DevDesk.Core.Commands;

/// <summary>
/// Execution states for in-memory saved command sessions.
/// Runtime-only; never persisted to durable storage.
/// </summary>
public enum SavedCommandExecutionState
{
    Idle,
    Starting,
    Running,
    Succeeded,
    Failed,
    Cancelled
}
