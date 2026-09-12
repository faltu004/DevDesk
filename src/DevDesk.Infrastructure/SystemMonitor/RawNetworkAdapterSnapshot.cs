namespace DevDesk.Infrastructure.SystemMonitor;

/// <summary>
/// Raw per-adapter throughput counter snapshot for delta calculation.
/// </summary>
public sealed record RawNetworkAdapterSnapshot(
    string Id,
    string Name,
    ulong BytesReceived,
    ulong BytesSent,
    bool IsOperationalUp);
