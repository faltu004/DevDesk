namespace DevDesk.Core.Runner;

/// <summary>
/// Immutable snapshot representing authoritative DevDesk ownership of a process
/// belonging to an active Project Runner session (either the root process or a descendant Job Object member).
/// </summary>
public sealed record ManagedProcessIdentity
{
    public required int ProcessId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string ProjectName { get; init; }
    public required Guid SessionId { get; init; }
    public bool IsRootProcess { get; init; }
    public DateTimeOffset? StartTimeUtc { get; init; }
}
