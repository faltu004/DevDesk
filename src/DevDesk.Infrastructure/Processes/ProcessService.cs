using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using DevDesk.Core.Processes;
using DevDesk.Core.Runner;

namespace DevDesk.Infrastructure.Processes;

/// <summary>
/// Implements IProcessService coordinating OS process enumeration, monotonic CPU delta sampling,
/// authoritative DevDesk Runner ownership correlation, and secure path resolution.
/// </summary>
public sealed class ProcessService : IProcessService
{
    private readonly IProcessSnapshotProvider _snapshotProvider;
    private readonly IProjectRunnerService _runnerService;
    private readonly ILogger<ProcessService> _logger;
    private readonly int _logicalProcessorCount;

    private readonly object _samplingLock = new();
    private Dictionary<ProcessKey, ProcessCpuSample> _cpuBaselines = new();
    private readonly ConcurrentDictionary<ProcessKey, string?> _verifiedPathCache = new();

    private sealed record ProcessCpuSample(
        TimeSpan TotalProcessorTime,
        long TimestampTicks
    );

    public ProcessService(
        IProcessSnapshotProvider snapshotProvider,
        IProjectRunnerService runnerService,
        ILogger<ProcessService> logger,
        int? logicalProcessorCount = null)
    {
        _snapshotProvider = snapshotProvider;
        _runnerService = runnerService;
        _logger = logger;
        _logicalProcessorCount = logicalProcessorCount ?? Math.Max(1, Environment.ProcessorCount);
    }

    public Task<ProcessSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Authoritative ownership snapshot captured once for the entire inspection cycle
            IReadOnlyList<ManagedProcessIdentity> managedIdentities;
            try
            {
                managedIdentities = _runnerService.GetManagedProcesses();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query runner managed processes; defaulting to external.");
                managedIdentities = Array.Empty<ManagedProcessIdentity>();
            }

            var managedByPid = new Dictionary<int, ManagedProcessIdentity>();
            foreach (var managed in managedIdentities)
            {
                managedByPid[managed.ProcessId] = managed;
            }

            // 2. Enumerate OS processes
            var rawList = _snapshotProvider.EnumerateProcesses();
            cancellationToken.ThrowIfCancellationRequested();

            // 3. Monotonic timestamp for CPU delta calculation
            long currentTimestamp = Stopwatch.GetTimestamp();
            double frequency = Stopwatch.Frequency;

            var newBaselines = new Dictionary<ProcessKey, ProcessCpuSample>(rawList.Count);
            var processInfos = new List<ProcessInfo>(rawList.Count);

            lock (_samplingLock)
            {
                foreach (var raw in rawList)
                {
                    double? cpuPercent = null;

                    // CPU is calculated only for verifiable processes with accessible TotalProcessorTime
                    if (raw.Key.IsVerifiable && raw.TotalProcessorTime.HasValue)
                    {
                        if (_cpuBaselines.TryGetValue(raw.Key, out var baseline))
                        {
                            double deltaSeconds = (currentTimestamp - baseline.TimestampTicks) / frequency;
                            TimeSpan deltaCpu = raw.TotalProcessorTime.Value - baseline.TotalProcessorTime;

                            if (deltaSeconds > 0 && deltaCpu >= TimeSpan.Zero)
                            {
                                double rawPercent = (deltaCpu.TotalSeconds / (deltaSeconds * _logicalProcessorCount)) * 100.0;
                                cpuPercent = Math.Clamp(rawPercent, 0.0, 100.0);
                            }
                        }

                        // Store current sample for next cycle
                        newBaselines[raw.Key] = new ProcessCpuSample(raw.TotalProcessorTime.Value, currentTimestamp);
                    }

                    // Ownership correlation: match exclusively on live Job Object membership with verified identity
                    ProcessOwnership ownership = ProcessOwnership.External;
                    Guid? managedProjectId = null;
                    string? managedProjectName = null;
                    Guid? managedSessionId = null;
                    bool isRootManagedProcess = false;

                    if (managedByPid.TryGetValue(raw.ProcessId, out var managedEntry))
                    {
                        // Identity verification to protect against PID recycling:
                        // If an owned process exits and Windows reassigns its PID to an unrelated external process,
                        // PID equality alone is insufficient across the snapshot boundary.
                        // Both the enumerated process and the managed entry must possess verifiable StartTimeUtc
                        // timestamps that match.
                        // If identity cannot be verified or does not match, conservatively treat as External.
                        bool isVerifiedOwner = false;
                        if (raw.Key.IsVerifiable && raw.Key.StartTimeUtc.HasValue && managedEntry.StartTimeUtc.HasValue)
                        {
                            var diff = (raw.Key.StartTimeUtc.Value - managedEntry.StartTimeUtc.Value).Duration();
                            if (diff <= TimeSpan.FromMilliseconds(50))
                            {
                                isVerifiedOwner = true;
                            }
                        }

                        if (isVerifiedOwner)
                        {
                            ownership = ProcessOwnership.Managed;
                            managedProjectId = managedEntry.ProjectId;
                            managedProjectName = managedEntry.ProjectName;
                            managedSessionId = managedEntry.SessionId;
                            isRootManagedProcess = managedEntry.IsRootProcess;
                        }
                    }

                    processInfos.Add(new ProcessInfo
                    {
                        Key = raw.Key,
                        ProcessId = raw.ProcessId,
                        ProcessName = raw.ProcessName,
                        CpuPercent = cpuPercent,
                        WorkingSetBytes = raw.WorkingSetBytes,
                        StartTime = raw.StartTime,
                        IsResponding = raw.IsResponding,
                        ExecutablePath = raw.ExecutablePath,
                        Ownership = ownership,
                        ManagedProjectId = managedProjectId,
                        ManagedProjectName = managedProjectName,
                        ManagedSessionId = managedSessionId,
                        IsRootManagedProcess = isRootManagedProcess
                    });
                }

                // Discard stale baselines for exited processes automatically
                _cpuBaselines = newBaselines;
            }

            // 4. Calculate summary metrics truthfully
            int totalCount = processInfos.Count;
            int managedCount = processInfos.Count(p => p.IsManaged);

            var validCpus = processInfos.Where(p => p.CpuPercent.HasValue).Select(p => p.CpuPercent!.Value).ToList();
            double? sampledCpu = validCpus.Count > 0 ? validCpus.Sum() : null;

            var validWorkingSets = processInfos.Where(p => p.WorkingSetBytes.HasValue).Select(p => p.WorkingSetBytes!.Value).ToList();
            long? combinedWorkingSet = validWorkingSets.Count > 0 ? validWorkingSets.Sum() : null;

            return new ProcessSnapshot
            {
                CapturedAt = DateTimeOffset.UtcNow,
                Processes = processInfos,
                TotalProcessCount = totalCount,
                ManagedProcessCount = managedCount,
                SampledCpuPercent = sampledCpu,
                CombinedWorkingSetBytes = combinedWorkingSet
            };
        }, cancellationToken);
    }

    public Task<string?> ResolveExecutablePathAsync(ProcessKey key, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            // Unverifiable processes are never cached
            if (!key.IsVerifiable)
            {
                return _snapshotProvider.TryResolveExecutablePath(key.ProcessId, null);
            }

            if (_verifiedPathCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var path = _snapshotProvider.TryResolveExecutablePath(key.ProcessId, key.StartTimeUtc);
            _verifiedPathCache[key] = path;
            return path;
        }, cancellationToken);
    }
}
