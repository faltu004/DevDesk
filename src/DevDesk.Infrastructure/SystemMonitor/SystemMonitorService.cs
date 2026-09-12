using DevDesk.Core.SystemMonitor;
using Microsoft.Extensions.Logging;

namespace DevDesk.Infrastructure.SystemMonitor;

/// <summary>
/// Authoritative implementation of ISystemMonitorService coordinating host telemetry sampling,
/// truthful per-interface and CPU delta calculation, baseline resets, and independent fault isolation.
/// </summary>
public sealed class SystemMonitorService : ISystemMonitorService
{
    private readonly ISystemMetricsProvider _provider;
    private readonly IMonotonicClock _clock;
    private readonly ILogger<SystemMonitorService> _logger;

    // CPU sampling state
    private bool _hasCpuBaseline;
    private ulong _prevIdleTime;
    private ulong _prevKernelTime;
    private ulong _prevUserTime;

    // Network sampling state (per-interface delta tracking)
    private Dictionary<string, (ulong RxBytes, ulong TxBytes)> _previousAdapters = new(StringComparer.Ordinal);
    private long? _previousNetworkTimestamp;

    // Disk caching state (slow-cadence telemetry)
    private DiskMetrics? _lastDiskMetrics;
    private long? _lastDiskTimestamp;
    private bool _forceDiskRefresh = true;

    // Lock protecting sampler state during concurrent/isolated calls
    private readonly object _stateLock = new();

    public SystemSnapshot? CurrentSnapshot { get; private set; }

    public event EventHandler<SystemSnapshot>? SnapshotUpdated;

    public SystemMonitorService(
        ISystemMetricsProvider provider,
        IMonotonicClock clock,
        ILogger<SystemMonitorService> logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resets all delta sampling baselines (CPU and Network). Called when Dashboard becomes active
    /// after being inactive, ensuring no false deltas span across inactive time windows.
    /// </summary>
    public void ResetBaselines()
    {
        lock (_stateLock)
        {
            _hasCpuBaseline = false;
            _prevIdleTime = 0;
            _prevKernelTime = 0;
            _prevUserTime = 0;

            _previousAdapters.Clear();
            _previousNetworkTimestamp = null;

            _forceDiskRefresh = true;
        }
    }

    public Task<SystemSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int logicalCores;
        try
        {
            logicalCores = _provider.GetLogicalProcessorCount();
        }
        catch
        {
            logicalCores = 1;
        }

        CpuMetrics cpuMetrics;
        MemoryMetrics memoryMetrics;
        DiskMetrics diskMetrics;
        NetworkMetrics networkMetrics;
        SystemInfo systemInfo;

        lock (_stateLock)
        {
            long nowTimestamp = _clock.GetTimestamp();

            // 1. CPU
            cpuMetrics = SampleCpu(logicalCores);

            // 2. Memory
            memoryMetrics = SampleMemory();

            // 3. Storage / Disk (slow cadence or forced)
            diskMetrics = SampleDisk(nowTimestamp);

            // 4. Network (per-interface delta)
            networkMetrics = SampleNetwork(nowTimestamp);

            // 5. System Info & Uptime
            systemInfo = SampleSystemInfo(logicalCores);
        }

        var snapshot = new SystemSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Cpu = cpuMetrics,
            Memory = memoryMetrics,
            Disk = diskMetrics,
            Network = networkMetrics,
            SystemInfo = systemInfo
        };

        CurrentSnapshot = snapshot;
        SnapshotUpdated?.Invoke(this, snapshot);

        return Task.FromResult(snapshot);
    }

    private CpuMetrics SampleCpu(int logicalCores)
    {
        try
        {
            if (!_provider.TryGetRawCpuTimes(out ulong idle, out ulong kernel, out ulong user))
            {
                _hasCpuBaseline = false;
                return CpuMetrics.Unavailable(logicalCores);
            }

            if (!_hasCpuBaseline)
            {
                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;
                _hasCpuBaseline = true;
                return CpuMetrics.Unavailable(logicalCores);
            }

            // Check non-decreasing counters
            if (idle < _prevIdleTime || kernel < _prevKernelTime || user < _prevUserTime)
            {
                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;
                return CpuMetrics.Unavailable(logicalCores);
            }

            ulong deltaKernel = kernel - _prevKernelTime;
            ulong deltaUser = user - _prevUserTime;
            ulong deltaIdle = idle - _prevIdleTime;
            ulong total = deltaKernel + deltaUser;

            // Advance baseline
            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;

            // Validate total and idle delta
            if (total == 0 || deltaIdle > total)
            {
                return CpuMetrics.Unavailable(logicalCores);
            }

            ulong busy = total - deltaIdle;
            double percent = (double)busy / (double)total * 100.0;

            return CpuMetrics.Create(percent, logicalCores);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sampling CPU metrics.");
            _hasCpuBaseline = false;
            return CpuMetrics.Unavailable(logicalCores);
        }
    }

    private MemoryMetrics SampleMemory()
    {
        try
        {
            if (!_provider.TryGetRawMemory(out ulong total, out ulong avail))
            {
                return MemoryMetrics.Unavailable();
            }

            return MemoryMetrics.Create(total, avail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sampling physical memory metrics.");
            return MemoryMetrics.Unavailable();
        }
    }

    private DiskMetrics SampleDisk(long nowTimestamp)
    {
        try
        {
            bool shouldRefresh = _lastDiskMetrics == null
                || _forceDiskRefresh
                || _lastDiskTimestamp == null
                || _clock.Elapsed(_lastDiskTimestamp.Value, nowTimestamp) >= TimeSpan.FromSeconds(20);

            if (shouldRefresh)
            {
                _lastDiskMetrics = _provider.GetDiskMetrics();
                _lastDiskTimestamp = nowTimestamp;
                _forceDiskRefresh = false;
            }

            return _lastDiskMetrics ?? DiskMetrics.Empty();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sampling disk metrics.");
            return _lastDiskMetrics ?? DiskMetrics.Empty();
        }
    }

    private NetworkMetrics SampleNetwork(long currentTimestamp)
    {
        try
        {
            IReadOnlyList<RawNetworkAdapterSnapshot> currentAdapters = _provider.GetNetworkAdapterSnapshots();

            if (_previousNetworkTimestamp == null || _previousAdapters.Count == 0)
            {
                _previousAdapters.Clear();
                foreach (var a in currentAdapters)
                {
                    _previousAdapters[a.Id] = (a.BytesReceived, a.BytesSent);
                }
                _previousNetworkTimestamp = currentTimestamp;
                return NetworkMetrics.Unavailable(currentAdapters.Count);
            }

            TimeSpan elapsed = _clock.Elapsed(_previousNetworkTimestamp.Value, currentTimestamp);
            double elapsedSeconds = elapsed.TotalSeconds;

            if (elapsedSeconds <= 0.0)
            {
                return NetworkMetrics.Unavailable(currentAdapters.Count);
            }

            ulong totalDeltaRx = 0;
            ulong totalDeltaTx = 0;
            int contributingAdapters = 0;
            var nextPrevious = new Dictionary<string, (ulong RxBytes, ulong TxBytes)>(StringComparer.Ordinal);

            foreach (var adapter in currentAdapters)
            {
                nextPrevious[adapter.Id] = (adapter.BytesReceived, adapter.BytesSent);

                if (_previousAdapters.TryGetValue(adapter.Id, out var prev))
                {
                    // Non-decreasing counter check
                    if (adapter.BytesReceived >= prev.RxBytes && adapter.BytesSent >= prev.TxBytes)
                    {
                        totalDeltaRx += (adapter.BytesReceived - prev.RxBytes);
                        totalDeltaTx += (adapter.BytesSent - prev.TxBytes);
                        contributingAdapters++;
                    }
                    // If counters reset/decreased, nextPrevious has updated baseline without adding a spike
                }
                // New adapter: baseline recorded in nextPrevious, no throughput contribution yet
            }

            _previousAdapters = nextPrevious;
            _previousNetworkTimestamp = currentTimestamp;

            if (contributingAdapters == 0)
            {
                return NetworkMetrics.Unavailable(currentAdapters.Count);
            }

            double rxRate = totalDeltaRx / elapsedSeconds;
            double txRate = totalDeltaTx / elapsedSeconds;

            return NetworkMetrics.Create(rxRate, txRate, currentAdapters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sampling network throughput metrics.");
            return NetworkMetrics.Unavailable();
        }
    }

    private SystemInfo SampleSystemInfo(int logicalCores)
    {
        try
        {
            return new SystemInfo
            {
                Uptime = _provider.GetUptime(),
                OsDescription = _provider.GetOsDescription(),
                LogicalProcessorCount = logicalCores
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error querying static system information.");
            return new SystemInfo
            {
                Uptime = TimeSpan.Zero,
                OsDescription = "Windows",
                LogicalProcessorCount = logicalCores
            };
        }
    }
}
