namespace DevDesk.Core.Commands;

/// <summary>
/// Point-in-time captured standard output or standard error event from a running saved command.
/// Includes monotonically increasing sequence number per execution session for deterministic UI ordering.
/// </summary>
public sealed record SavedCommandOutputEvent
{
    public required Guid SessionId { get; init; }
    public required Guid CommandId { get; init; }
    public required long SequenceNumber { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required bool IsError { get; init; }
    public required string Text { get; init; }
}
