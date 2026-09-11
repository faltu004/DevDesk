using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.Core.Processes;
using DevDesk.Core.Runner;
using DevDesk.Infrastructure.Processes;

namespace DevDesk.Tests.Processes;

public sealed class ProcessServiceTests
{
    private sealed class FakeProcessSnapshotProvider : IProcessSnapshotProvider
    {
        public Func<IReadOnlyList<RawProcessEntry>>? EnumerateFunc { get; set; }
        public Func<int, DateTimeOffset?, string?>? ResolvePathFunc { get; set; }

        public IReadOnlyList<RawProcessEntry> EnumerateProcesses()
            => EnumerateFunc?.Invoke() ?? Array.Empty<RawProcessEntry>();

        public string? TryResolveExecutablePath(int processId, DateTimeOffset? expectedStartTimeUtc)
            => ResolvePathFunc?.Invoke(processId, expectedStartTimeUtc);
    }

    private sealed class FakeProjectRunnerService : IProjectRunnerService
    {
        public List<ManagedProcessIdentity> ManagedProcesses { get; } = new();
        public int GetManagedProcessesCallCount { get; private set; }

        public IReadOnlyList<ManagedProcessIdentity> GetManagedProcesses()
        {
            GetManagedProcessesCallCount++;
            return ManagedProcesses.ToList();
        }

        public Task<ProjectRunResult> StartProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProjectStopResult> StopProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProjectRunResult> RestartProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProjectStopResult>> StopAllAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ProjectRunSession? GetSession(Guid projectId) => null;
        public IReadOnlyList<ProjectRunSession> GetActiveSessions() => Array.Empty<ProjectRunSession>();
        public IReadOnlyList<ProjectRunSession> GetRecentSessions(Guid projectId) => Array.Empty<ProjectRunSession>();
        public IReadOnlyList<ProcessOutputEvent> GetSessionLogs(Guid projectId, Guid sessionId) => Array.Empty<ProcessOutputEvent>();
        public event EventHandler<ProjectRunSession>? SessionChanged { add { } remove { } }
        public event EventHandler<ProcessOutputEvent>? OutputReceived { add { } remove { } }
    }

    [Fact] // 1. Process enumeration snapshot
    public async Task ProcessSnapshot_Enumeration_CapturesSnapshotCorrectly()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance, logicalProcessorCount: 4);

        var startTime = DateTimeOffset.UtcNow;
        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = ProcessKey.CreateVerified(100, startTime),
                ProcessId = 100,
                ProcessName = "app.exe",
                WorkingSetBytes = 1024 * 1024 * 50,
                StartTime = startTime,
                IsResponding = true
            }
        ];

        var snapshot = await service.CaptureSnapshotAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot.TotalProcessCount);
        Assert.Equal(0, snapshot.ManagedProcessCount);
        Assert.Single(snapshot.Processes);
        Assert.Equal("app.exe", snapshot.Processes[0].ProcessName);
    }

    [Fact] // 2. CPU delta math
    public async Task CpuCalculator_DeltaCalculation_ComputesExpectedPercentage()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        // 1 logical core for simple deterministic math
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance, logicalProcessorCount: 1);

        var startTime = DateTimeOffset.UtcNow;
        var key = ProcessKey.CreateVerified(200, startTime);

        // First sample at t1
        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = key,
                ProcessId = 200,
                ProcessName = "worker.exe",
                TotalProcessorTime = TimeSpan.FromSeconds(10),
                WorkingSetBytes = 1024,
                StartTime = startTime
            }
        ];

        var s1 = await service.CaptureSnapshotAsync();
        Assert.Null(s1.Processes[0].CpuPercent); // First sample must be null

        // Second sample with +1 second CPU time after some elapsed time
        await Task.Delay(100);

        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = key,
                ProcessId = 200,
                ProcessName = "worker.exe",
                TotalProcessorTime = TimeSpan.FromSeconds(10.05), // small delta
                WorkingSetBytes = 1024,
                StartTime = startTime
            }
        ];

        var s2 = await service.CaptureSnapshotAsync();
        Assert.NotNull(s2.Processes[0].CpuPercent);
        Assert.True(s2.Processes[0].CpuPercent >= 0.0 && s2.Processes[0].CpuPercent <= 100.0);
    }

    [Fact] // 3. Logical processor normalization
    public async Task CpuCalculator_NormalizesByLogicalProcessorCount()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        // 8 logical cores
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance, logicalProcessorCount: 8);

        var startTime = DateTimeOffset.UtcNow;
        var key = ProcessKey.CreateVerified(250, startTime);

        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = key,
                ProcessId = 250,
                ProcessName = "heavy.exe",
                TotalProcessorTime = TimeSpan.FromSeconds(1),
                StartTime = startTime
            }
        ];

        await service.CaptureSnapshotAsync();
        await Task.Delay(50);

        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = key,
                ProcessId = 250,
                ProcessName = "heavy.exe",
                TotalProcessorTime = TimeSpan.FromSeconds(1.02),
                StartTime = startTime
            }
        ];

        var s2 = await service.CaptureSnapshotAsync();
        Assert.NotNull(s2.Processes[0].CpuPercent);
        Assert.True(s2.Processes[0].CpuPercent <= 100.0);
    }

    [Fact] // 4. Monotonic elapsed-time sampling & 5. First CPU sample is null
    public async Task CpuCalculator_FirstSample_ReturnsNullCpuPercent()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance, logicalProcessorCount: 4);

        var key = ProcessKey.CreateVerified(300, DateTimeOffset.UtcNow);
        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = key,
                ProcessId = 300,
                ProcessName = "first.exe",
                TotalProcessorTime = TimeSpan.FromSeconds(50),
                StartTime = DateTimeOffset.UtcNow
            }
        ];

        var snapshot = await service.CaptureSnapshotAsync();

        Assert.Null(snapshot.Processes[0].CpuPercent);
        Assert.Null(snapshot.SampledCpuPercent); // Initial summary CPU is also null
    }

    [Fact] // 6. Verified PID reuse resets CPU baseline
    public async Task ProcessService_PidReuseWithDifferentStartTime_ResetsCpuBaseline()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance, logicalProcessorCount: 2);

        var start1 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var key1 = ProcessKey.CreateVerified(400, start1);

        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = key1,
                ProcessId = 400,
                ProcessName = "oldApp.exe",
                TotalProcessorTime = TimeSpan.FromSeconds(100),
                StartTime = start1
            }
        ];

        await service.CaptureSnapshotAsync();

        // PID 400 exits, new process launches at start2 with PID 400
        var start2 = DateTimeOffset.UtcNow;
        var key2 = ProcessKey.CreateVerified(400, start2);

        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = key2,
                ProcessId = 400,
                ProcessName = "newApp.exe",
                TotalProcessorTime = TimeSpan.FromSeconds(1), // Total time is much smaller!
                StartTime = start2
            }
        ];

        var s2 = await service.CaptureSnapshotAsync();

        // Must treat as a new process: CPU is null, does NOT compare against oldApp's 100s
        Assert.Null(s2.Processes[0].CpuPercent);
    }

    [Fact] // 7. Unverifiable identity does not reuse CPU baseline
    public async Task ProcessService_UnverifiableIdentity_DoesNotReuseCpuBaseline()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance, logicalProcessorCount: 2);

        // Unverifiable process (StartTime inaccessible) generates ephemeral ID on each observation
        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = ProcessKey.CreateUnverified(500, ephemeralId: 1),
                ProcessId = 500,
                ProcessName = "protected.exe",
                TotalProcessorTime = TimeSpan.FromSeconds(10),
                StartTime = null
            }
        ];

        var s1 = await service.CaptureSnapshotAsync();
        Assert.Null(s1.Processes[0].CpuPercent);

        await Task.Delay(20);

        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = ProcessKey.CreateUnverified(500, ephemeralId: 2),
                ProcessId = 500,
                ProcessName = "protected.exe",
                TotalProcessorTime = TimeSpan.FromSeconds(12),
                StartTime = null
            }
        ];

        var s2 = await service.CaptureSnapshotAsync();
        // Because key is unverifiable, it MUST NOT reuse CPU baseline
        Assert.Null(s2.Processes[0].CpuPercent);
    }

    [Fact] // 8. Unverifiable identity does not reuse path cache
    public async Task ProcessService_UnverifiableIdentity_DoesNotReusePathCache()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance);

        int callCount = 0;
        provider.ResolvePathFunc = (pid, expectedStart) =>
        {
            callCount++;
            return $"C:\\Windows\\System32\\process_{callCount}.exe";
        };

        var unverifiableKey = ProcessKey.CreateUnverified(600, ephemeralId: 999);

        var p1 = await service.ResolveExecutablePathAsync(unverifiableKey);
        var p2 = await service.ResolveExecutablePathAsync(unverifiableKey);

        Assert.Equal(2, callCount); // Must NOT cache unverifiable key
    }

    [Fact] // 9. WorkingSet unavailable remains null, not zero & 10. Combined working set excludes nulls
    public async Task ProcessSnapshot_CombinedWorkingSet_ExcludesUnavailableValuesTruthfully()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance);

        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = ProcessKey.CreateVerified(701, DateTimeOffset.UtcNow),
                ProcessId = 701,
                ProcessName = "p1.exe",
                WorkingSetBytes = 1000
            },
            new RawProcessEntry
            {
                Key = ProcessKey.CreateVerified(702, DateTimeOffset.UtcNow),
                ProcessId = 702,
                ProcessName = "p2.exe",
                WorkingSetBytes = null // Inaccessible/exited
            }
        ];

        var snapshot = await service.CaptureSnapshotAsync();

        Assert.Equal(1000, snapshot.Processes[0].WorkingSetBytes);
        Assert.Null(snapshot.Processes[1].WorkingSetBytes);
        Assert.Equal(1000, snapshot.CombinedWorkingSetBytes); // Truthfully sums only valid samples
    }

    [Fact] // 11. Root Runner process marked Managed
    public async Task ProcessService_RootRunnerProcess_MarkedManagedWithSessionMetadata()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance);

        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var startTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        runner.ManagedProcesses.Add(new ManagedProcessIdentity
        {
            ProcessId = 801,
            ProjectId = projectId,
            ProjectName = "WebAPI",
            SessionId = sessionId,
            IsRootProcess = true,
            StartTimeUtc = startTime
        });

        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = ProcessKey.CreateVerified(801, startTime),
                ProcessId = 801,
                ProcessName = "dotnet.exe",
                StartTime = startTime
            }
        ];

        var snapshot = await service.CaptureSnapshotAsync();

        var p = snapshot.Processes[0];
        Assert.Equal(ProcessOwnership.Managed, p.Ownership);
        Assert.True(p.IsManaged);
        Assert.True(p.IsRootManagedProcess);
        Assert.Equal(projectId, p.ManagedProjectId);
        Assert.Equal("WebAPI", p.ManagedProjectName);
        Assert.Equal(sessionId, p.ManagedSessionId);
    }

    [Fact] // 12. Job Object child process marked Managed
    public async Task ProcessService_JobObjectChildProcess_MarkedManagedWithSessionMetadata()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance);

        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var childStartTime = DateTimeOffset.UtcNow.AddMinutes(-4);

        // Child process spawned under Job Object
        runner.ManagedProcesses.Add(new ManagedProcessIdentity
        {
            ProcessId = 802,
            ProjectId = projectId,
            ProjectName = "WebAPI",
            SessionId = sessionId,
            IsRootProcess = false,
            StartTimeUtc = childStartTime
        });

        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = ProcessKey.CreateVerified(802, childStartTime),
                ProcessId = 802,
                ProcessName = "WebAPI.exe",
                StartTime = childStartTime
            }
        ];

        var snapshot = await service.CaptureSnapshotAsync();

        var p = snapshot.Processes[0];
        Assert.Equal(ProcessOwnership.Managed, p.Ownership);
        Assert.True(p.IsManaged);
        Assert.False(p.IsRootManagedProcess); // Child, not root
        Assert.Equal("WebAPI", p.ManagedProjectName);
    }

    [Fact] // 13. Same executable manually launched remains External
    public async Task ProcessService_SameExecutableManuallyLaunched_RemainsExternal()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance);

        var managedStartTime = DateTimeOffset.UtcNow.AddMinutes(-3);

        // Runner only owns PID 801
        runner.ManagedProcesses.Add(new ManagedProcessIdentity
        {
            ProcessId = 801,
            ProjectId = Guid.NewGuid(),
            ProjectName = "WebAPI",
            SessionId = Guid.NewGuid(),
            IsRootProcess = true,
            StartTimeUtc = managedStartTime
        });

        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = ProcessKey.CreateVerified(801, managedStartTime),
                ProcessId = 801,
                ProcessName = "dotnet.exe",
                StartTime = managedStartTime
            },
            new RawProcessEntry
            {
                // Manually started dotnet.exe on PID 999
                Key = ProcessKey.CreateVerified(999, DateTimeOffset.UtcNow),
                ProcessId = 999,
                ProcessName = "dotnet.exe",
                StartTime = DateTimeOffset.UtcNow
            }
        ];

        var snapshot = await service.CaptureSnapshotAsync();

        Assert.Equal(ProcessOwnership.Managed, snapshot.Processes[0].Ownership);
        Assert.Equal(ProcessOwnership.External, snapshot.Processes[1].Ownership);
        Assert.False(snapshot.Processes[1].IsManaged);
        Assert.Null(snapshot.Processes[1].ManagedProjectId);
    }

    [Fact] // Regression test: Recycled PID matching stale Job-membership PID never becomes Managed
    public async Task ProcessService_RecycledPid_MatchingStaleJobMemberPid_NeverBecomesManaged()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance);

        var originalStartTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var recycledStartTime = DateTimeOffset.UtcNow;

        // Runner recorded stale Job Object member PID 5000 from when it was alive
        runner.ManagedProcesses.Add(new ManagedProcessIdentity
        {
            ProcessId = 5000,
            ProjectId = Guid.NewGuid(),
            ProjectName = "OldRunnerApp",
            SessionId = Guid.NewGuid(),
            IsRootProcess = false,
            StartTimeUtc = originalStartTime
        });

        // PID 5000 exited and Windows recycled PID 5000 for an unrelated external application (notepad.exe)
        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = ProcessKey.CreateVerified(5000, recycledStartTime),
                ProcessId = 5000,
                ProcessName = "notepad.exe",
                StartTime = recycledStartTime
            }
        ];

        var snapshot = await service.CaptureSnapshotAsync();

        var p = snapshot.Processes[0];
        Assert.Equal(ProcessOwnership.External, p.Ownership);
        Assert.False(p.IsManaged);
        Assert.Null(p.ManagedProjectId);
        Assert.Null(p.ManagedProjectName);
        Assert.Null(p.ManagedSessionId);
    }

    [Fact] // Regression test: Unverifiable process matching Job PID never becomes Managed
    public async Task ProcessService_UnverifiableProcess_MatchingJobMemberPid_NeverBecomesManaged()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance);

        runner.ManagedProcesses.Add(new ManagedProcessIdentity
        {
            ProcessId = 6000,
            ProjectId = Guid.NewGuid(),
            ProjectName = "RunnerApp",
            SessionId = Guid.NewGuid(),
            IsRootProcess = true,
            StartTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });

        // Protected or exiting process whose StartTime cannot be read
        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = ProcessKey.CreateUnverified(6000, ephemeralId: 999),
                ProcessId = 6000,
                ProcessName = "unverifiable.exe",
                StartTime = null
            }
        ];

        var snapshot = await service.CaptureSnapshotAsync();

        var p = snapshot.Processes[0];
        Assert.Equal(ProcessOwnership.External, p.Ownership);
        Assert.False(p.IsManaged);
    }

    [Fact] // 14. Port occupancy alone never grants ownership & 15. Executable/path match never grants ownership
    public async Task ProcessService_ExecutablePathMatchAlone_NeverGrantsManagedOwnership()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance);

        // No runner processes active
        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = ProcessKey.CreateVerified(1234, DateTimeOffset.UtcNow),
                ProcessId = 1234,
                ProcessName = "node.exe",
                ExecutablePath = "D:\\Projects\\Frontend\\node_modules\\.bin\\vite.exe"
            }
        ];

        var snapshot = await service.CaptureSnapshotAsync();

        Assert.Equal(ProcessOwnership.External, snapshot.Processes[0].Ownership);
        Assert.False(snapshot.Processes[0].IsManaged);
    }

    [Fact] // 16. Process exit mid-read handled silently & 17. Access denied returns partial metadata
    public async Task ProcessService_AccessDenied_ProducesPartialMetadataWithNulls()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance);

        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry
            {
                Key = ProcessKey.CreateUnverified(4, 1),
                ProcessId = 4,
                ProcessName = "System",
                TotalProcessorTime = null,
                WorkingSetBytes = null,
                StartTime = null,
                IsResponding = null,
                ExecutablePath = null
            }
        ];

        var snapshot = await service.CaptureSnapshotAsync();

        Assert.Single(snapshot.Processes);
        var p = snapshot.Processes[0];
        Assert.Equal(4, p.ProcessId);
        Assert.Equal("System", p.ProcessName);
        Assert.Null(p.CpuPercent);
        Assert.Null(p.WorkingSetBytes);
        Assert.Null(p.StartTime);
        Assert.Null(p.IsResponding);
        Assert.Null(p.ExecutablePath);
    }

    [Fact] // 28. Cancellation propagates
    public async Task ProcessService_Cancellation_PropagatesPromptly()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.CaptureSnapshotAsync(cts.Token);
        });
    }

    [Fact] // 29. Runner ownership snapshot is immutable for one inspection cycle
    public async Task ProcessService_RunnerOwnershipSnapshot_IsImmutableForInspectionCycle()
    {
        var provider = new FakeProcessSnapshotProvider();
        var runner = new FakeProjectRunnerService();
        var service = new ProcessService(provider, runner, NullLogger<ProcessService>.Instance);

        provider.EnumerateFunc = () =>
        [
            new RawProcessEntry { Key = ProcessKey.CreateVerified(1, DateTimeOffset.UtcNow), ProcessId = 1, ProcessName = "p1" },
            new RawProcessEntry { Key = ProcessKey.CreateVerified(2, DateTimeOffset.UtcNow), ProcessId = 2, ProcessName = "p2" }
        ];

        await service.CaptureSnapshotAsync();

        // GetManagedProcesses should have been called exactly ONCE per snapshot, not per process!
        Assert.Equal(1, runner.GetManagedProcessesCallCount);
    }
}
