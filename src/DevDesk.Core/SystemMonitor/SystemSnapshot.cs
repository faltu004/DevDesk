namespace DevDesk.Core.SystemMonitor;

/// <summary>
/// Immutable snapshot of host-level system telemetry captured at an instant.
/// Individual metric components may be unavailable independently.
/// </summary>
public sealed record SystemSnapshot
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public CpuMetrics Cpu { get; init; } = CpuMetrics.Unavailable(0);
    public MemoryMetrics Memory { get; init; } = MemoryMetrics.Unavailable();
    public DiskMetrics Disk { get; init; } = DiskMetrics.Empty();
    public NetworkMetrics Network { get; init; } = NetworkMetrics.Unavailable();
    public SystemInfo SystemInfo { get; init; } = new();
}
