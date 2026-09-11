namespace DevDesk.Core.Processes;

/// <summary>
/// Immutable point-in-time domain snapshot of a Windows process inspected by DevDesk.
/// </summary>
public sealed record ProcessInfo
{
    public required ProcessKey Key { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public double? CpuPercent { get; init; }
    public long? WorkingSetBytes { get; init; }
    public DateTimeOffset? StartTime { get; init; }
    public bool? IsResponding { get; init; }
    public string? ExecutablePath { get; init; }
    public required ProcessOwnership Ownership { get; init; }
    public Guid? ManagedProjectId { get; init; }
    public string? ManagedProjectName { get; init; }
    public Guid? ManagedSessionId { get; init; }
    public bool IsRootManagedProcess { get; init; }

    public bool IsManaged => Ownership == ProcessOwnership.Managed;
}
