namespace DevDesk.Core.Runner;

/// <summary>
/// Immutable point-in-time snapshot of an in-memory project execution session managed by DevDesk.
/// </summary>
public sealed record ProjectRunSession
{
    public required Guid SessionId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string ProjectName { get; init; }
    public int? ProcessId { get; init; }
    public required string CommandText { get; init; }
    public required string ExecutablePath { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required ProjectRunState State { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExitedAt { get; init; }
    public int? ExitCode { get; init; }
    public string? ErrorMessage { get; init; }
    public ProjectTerminationReason TerminationReason { get; init; } = ProjectTerminationReason.None;

    public bool IsActive => State == ProjectRunState.Starting || State == ProjectRunState.Running || State == ProjectRunState.Stopping;
}
