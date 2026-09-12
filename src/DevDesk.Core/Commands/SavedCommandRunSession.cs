namespace DevDesk.Core.Commands;

/// <summary>
/// Immutable snapshot representing an active or recently completed execution of a saved command.
/// Retained purely in memory.
/// </summary>
public sealed record SavedCommandRunSession
{
    public required Guid SessionId { get; init; }
    public required Guid CommandId { get; init; }
    public required string CommandName { get; init; }
    public required string ExecutablePath { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required string WorkingDirectory { get; init; }
    public required SavedCommandExecutionState State { get; init; }
    public int? ProcessId { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? ExitedAtUtc { get; init; }
    public int? ExitCode { get; init; }
    public string? ErrorMessage { get; init; }

    public bool IsActive => State is SavedCommandExecutionState.Starting or SavedCommandExecutionState.Running;
    public bool HasExited => State is SavedCommandExecutionState.Succeeded or SavedCommandExecutionState.Failed or SavedCommandExecutionState.Cancelled;
}
