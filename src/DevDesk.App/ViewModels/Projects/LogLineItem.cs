namespace DevDesk.App.ViewModels.Projects;

/// <summary>
/// Immutable presentation model for a single process output line.
/// </summary>
public sealed record LogLineItem
{
    public required Guid SessionId { get; init; }
    public required long SequenceNumber { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Text { get; init; }
    public required bool IsError { get; init; }

    public DateTimeOffset LocalTimestamp => Timestamp.ToLocalTime();
    public string FormattedTimestamp => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
    public string StreamBadge => IsError ? "stderr" : "stdout";
}
