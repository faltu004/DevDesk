using DevDesk.Core.Processes;

namespace DevDesk.Infrastructure.Processes;

/// <summary>
/// Lightweight raw telemetry captured from the operating system for a single process.
/// Contains only cheap polling fields.
/// </summary>
public sealed record RawProcessEntry
{
    public required ProcessKey Key { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public TimeSpan? TotalProcessorTime { get; init; }
    public long? WorkingSetBytes { get; init; }
    public DateTimeOffset? StartTime { get; init; }
    public bool? IsResponding { get; init; }
    public string? ExecutablePath { get; init; }
}
