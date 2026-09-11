namespace DevDesk.Core.Runner;

/// <summary>
/// Represents a captured stdout or stderr line emitted by a DevDesk-managed process.
/// Foundation for Phase 8 Live Logs.
/// </summary>
public sealed record ProcessOutputEvent
{
    public required Guid SessionId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string Text { get; init; }
    public required bool IsError { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
