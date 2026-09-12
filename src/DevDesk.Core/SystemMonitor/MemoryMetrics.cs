namespace DevDesk.Core.SystemMonitor;

/// <summary>
/// Authoritative physical RAM metrics.
/// </summary>
public sealed record MemoryMetrics
{
    public bool IsAvailable { get; init; }
    public ulong TotalPhysicalBytes { get; init; }
    public ulong AvailablePhysicalBytes { get; init; }
    public ulong UsedPhysicalBytes { get; init; }
    public double? UsagePercentage { get; init; }

    public static MemoryMetrics Unavailable() =>
        new() { IsAvailable = false, TotalPhysicalBytes = 0, AvailablePhysicalBytes = 0, UsedPhysicalBytes = 0, UsagePercentage = null };

    public static MemoryMetrics Create(ulong totalBytes, ulong availableBytes)
    {
        if (totalBytes == 0 || availableBytes > totalBytes)
        {
            return Unavailable();
        }

        ulong used = totalBytes - availableBytes;
        double percentage = (double)used / totalBytes * 100.0;
        return new MemoryMetrics
        {
            IsAvailable = true,
            TotalPhysicalBytes = totalBytes,
            AvailablePhysicalBytes = availableBytes,
            UsedPhysicalBytes = used,
            UsagePercentage = Math.Clamp(percentage, 0.0, 100.0)
        };
    }
}
