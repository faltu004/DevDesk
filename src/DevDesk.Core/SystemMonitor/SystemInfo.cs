namespace DevDesk.Core.SystemMonitor;

/// <summary>
/// Static and slow-moving host operating system metadata.
/// </summary>
public sealed record SystemInfo
{
    public TimeSpan Uptime { get; init; }
    public string OsDescription { get; init; } = string.Empty;
    public int LogicalProcessorCount { get; init; }
}
