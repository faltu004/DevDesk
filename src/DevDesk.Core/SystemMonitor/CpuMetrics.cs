namespace DevDesk.Core.SystemMonitor;

/// <summary>
/// Authoritative host-level CPU utilization metrics.
/// </summary>
public sealed record CpuMetrics
{
    public double? UsagePercentage { get; init; }
    public int LogicalProcessorCount { get; init; }
    public bool IsAvailable => UsagePercentage.HasValue;

    public static CpuMetrics Unavailable(int logicalProcessorCount) =>
        new() { UsagePercentage = null, LogicalProcessorCount = logicalProcessorCount };

    public static CpuMetrics Create(double percentage, int logicalProcessorCount) =>
        new() { UsagePercentage = Math.Clamp(percentage, 0.0, 100.0), LogicalProcessorCount = logicalProcessorCount };
}
