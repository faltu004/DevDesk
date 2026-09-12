using DevDesk.Core.SystemMonitor;

namespace DevDesk.Infrastructure.SystemMonitor;

/// <summary>
/// Abstraction providing raw, un-deltad host metrics.
/// Allows 100% deterministic test fakes without live Windows host calls.
/// </summary>
public interface ISystemMetricsProvider
{
    bool TryGetRawCpuTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);
    bool TryGetRawMemory(out ulong totalPhysicalBytes, out ulong availablePhysicalBytes);
    DiskMetrics GetDiskMetrics();
    IReadOnlyList<RawNetworkAdapterSnapshot> GetNetworkAdapterSnapshots();
    TimeSpan GetUptime();
    string GetOsDescription();
    int GetLogicalProcessorCount();
}
