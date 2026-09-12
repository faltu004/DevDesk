namespace DevDesk.Core.SystemMonitor;

/// <summary>
/// Authoritative host network throughput rates.
/// </summary>
public sealed record NetworkMetrics
{
    public double? RxBytesPerSecond { get; init; }
    public double? TxBytesPerSecond { get; init; }
    public int ActiveAdapterCount { get; init; }
    public bool IsAvailable => RxBytesPerSecond.HasValue && TxBytesPerSecond.HasValue;

    public double? TotalBytesPerSecond =>
        IsAvailable ? RxBytesPerSecond!.Value + TxBytesPerSecond!.Value : null;

    public static NetworkMetrics Unavailable(int activeAdapters = 0) =>
        new() { RxBytesPerSecond = null, TxBytesPerSecond = null, ActiveAdapterCount = activeAdapters };

    public static NetworkMetrics Create(double rxBytesPerSec, double txBytesPerSec, int activeAdapters) =>
        new()
        {
            RxBytesPerSecond = Math.Max(0.0, rxBytesPerSec),
            TxBytesPerSecond = Math.Max(0.0, txBytesPerSec),
            ActiveAdapterCount = activeAdapters
        };
}
