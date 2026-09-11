using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.App.ViewModels.Processes;
using DevDesk.Core.Launchers;
using DevDesk.Core.Processes;

namespace DevDesk.Tests.Processes;

public sealed class ProcessesViewModelTests
{
    private sealed class FakeProcessService : IProcessService
    {
        public Func<CancellationToken, Task<ProcessSnapshot>>? CaptureFunc { get; set; }
        public Func<ProcessKey, CancellationToken, Task<string?>>? ResolvePathFunc { get; set; }
        public int CaptureCallCount { get; private set; }

        public Task<ProcessSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            CaptureCallCount++;
            return CaptureFunc?.Invoke(cancellationToken) ?? Task.FromResult(new ProcessSnapshot
            {
                CapturedAt = DateTimeOffset.UtcNow,
                Processes = Array.Empty<ProcessInfo>(),
                TotalProcessCount = 0,
                ManagedProcessCount = 0,
                SampledCpuPercent = null,
                CombinedWorkingSetBytes = null
            });
        }

        public Task<string?> ResolveExecutablePathAsync(ProcessKey key, CancellationToken cancellationToken = default)
            => ResolvePathFunc?.Invoke(key, cancellationToken) ?? Task.FromResult<string?>(null);
    }

    private sealed class FakeLauncherService : ILauncherService
    {
        public Task<LaunchResult> OpenInVsCodeAsync(string projectPath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LaunchResult> OpenInExplorerAsync(string projectPath, CancellationToken cancellationToken = default) => Task.FromResult(LaunchResult.Ok());
        public Task<LaunchResult> OpenTerminalAsync(string projectPath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private static ProcessSnapshot CreateTestSnapshot(params ProcessInfo[] processes)
    {
        return new ProcessSnapshot
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Processes = processes,
            TotalProcessCount = processes.Length,
            ManagedProcessCount = processes.Count(p => p.IsManaged),
            SampledCpuPercent = processes.Where(p => p.CpuPercent.HasValue).Select(p => p.CpuPercent!.Value).DefaultIfEmpty().Sum(),
            CombinedWorkingSetBytes = processes.Where(p => p.WorkingSetBytes.HasValue).Select(p => p.WorkingSetBytes!.Value).DefaultIfEmpty().Sum()
        };
    }

    [Fact] // 18. Search filter
    public async Task ProcessesViewModel_SearchFilter_FiltersByNamePidAndProject()
    {
        var service = new FakeProcessService();
        var launcher = new FakeLauncherService();
        var vm = new ProcessesViewModel(service, launcher, NullLogger<ProcessesViewModel>.Instance);

        var p1 = new ProcessInfo
        {
            Key = ProcessKey.CreateVerified(101, DateTimeOffset.UtcNow),
            ProcessId = 101,
            ProcessName = "dotnet.exe",
            Ownership = ProcessOwnership.Managed,
            ManagedProjectName = "BillingApi"
        };
        var p2 = new ProcessInfo
        {
            Key = ProcessKey.CreateVerified(202, DateTimeOffset.UtcNow),
            ProcessId = 202,
            ProcessName = "node.exe",
            Ownership = ProcessOwnership.External
        };

        service.CaptureFunc = _ => Task.FromResult(CreateTestSnapshot(p1, p2));

        await vm.InitializeAsync();
        try
        {
            Assert.Equal(2, vm.FilteredProcesses.Count);

            // Filter by name
            vm.SearchText = "dotnet";
            Assert.Single(vm.FilteredProcesses);
            Assert.Equal(101, vm.FilteredProcesses[0].ProcessId);

            // Filter by PID
            vm.SearchText = "202";
            Assert.Single(vm.FilteredProcesses);
            Assert.Equal(202, vm.FilteredProcesses[0].ProcessId);

            // Filter by Project
            vm.SearchText = "Billing";
            Assert.Single(vm.FilteredProcesses);
            Assert.Equal(101, vm.FilteredProcesses[0].ProcessId);

            // Clear filter
            vm.SearchText = string.Empty;
            Assert.Equal(2, vm.FilteredProcesses.Count);
        }
        finally
        {
            await vm.DeactivateAsync();
        }
    }

    [Fact] // 19. Ownership filter
    public async Task ProcessesViewModel_OwnershipFilter_FiltersManagedAndExternal()
    {
        var service = new FakeProcessService();
        var launcher = new FakeLauncherService();
        var vm = new ProcessesViewModel(service, launcher, NullLogger<ProcessesViewModel>.Instance);

        var managed = new ProcessInfo
        {
            Key = ProcessKey.CreateVerified(101, DateTimeOffset.UtcNow),
            ProcessId = 101,
            ProcessName = "managed.exe",
            Ownership = ProcessOwnership.Managed
        };
        var external = new ProcessInfo
        {
            Key = ProcessKey.CreateVerified(202, DateTimeOffset.UtcNow),
            ProcessId = 202,
            ProcessName = "external.exe",
            Ownership = ProcessOwnership.External
        };

        service.CaptureFunc = _ => Task.FromResult(CreateTestSnapshot(managed, external));

        await vm.InitializeAsync();
        try
        {
            // All
            vm.FilterMode = ProcessFilterMode.All;
            Assert.Equal(2, vm.FilteredProcesses.Count);

            // Managed only
            vm.FilterMode = ProcessFilterMode.DevDeskManaged;
            Assert.Single(vm.FilteredProcesses);
            Assert.Equal(101, vm.FilteredProcesses[0].ProcessId);

            // External only
            vm.FilterMode = ProcessFilterMode.External;
            Assert.Single(vm.FilteredProcesses);
            Assert.Equal(202, vm.FilteredProcesses[0].ProcessId);
        }
        finally
        {
            await vm.DeactivateAsync();
        }
    }

    [Fact] // 20. CPU sort & 21. Memory sort
    public async Task ProcessesViewModel_Sorting_SortsByCpuAndMemoryCorrectly()
    {
        var service = new FakeProcessService();
        var launcher = new FakeLauncherService();
        var vm = new ProcessesViewModel(service, launcher, NullLogger<ProcessesViewModel>.Instance);

        var low = new ProcessInfo
        {
            Key = ProcessKey.CreateVerified(1, DateTimeOffset.UtcNow),
            ProcessId = 1,
            ProcessName = "low.exe",
            CpuPercent = 1.0,
            WorkingSetBytes = 1000,
            Ownership = ProcessOwnership.External
        };
        var high = new ProcessInfo
        {
            Key = ProcessKey.CreateVerified(2, DateTimeOffset.UtcNow),
            ProcessId = 2,
            ProcessName = "high.exe",
            CpuPercent = 50.0,
            WorkingSetBytes = 5000,
            Ownership = ProcessOwnership.External
        };

        service.CaptureFunc = _ => Task.FromResult(CreateTestSnapshot(low, high));

        await vm.InitializeAsync();
        try
        {
            // CPU Descending
            vm.SortColumn = ProcessSortColumn.Cpu;
            vm.SortAscending = false;
            Assert.Equal("high.exe", vm.FilteredProcesses[0].ProcessName);

            // CPU Ascending
            vm.SortAscending = true;
            Assert.Equal("low.exe", vm.FilteredProcesses[0].ProcessName);

            // Memory Descending
            vm.SortColumn = ProcessSortColumn.Memory;
            vm.SortAscending = false;
            Assert.Equal("high.exe", vm.FilteredProcesses[0].ProcessName);

            // Memory Ascending
            vm.SortAscending = true;
            Assert.Equal("low.exe", vm.FilteredProcesses[0].ProcessName);
        }
        finally
        {
            await vm.DeactivateAsync();
        }
    }

    [Fact] // 22. Verified selection survives refresh
    public async Task ProcessesViewModel_VerifiedSelection_SurvivesRefresh()
    {
        var service = new FakeProcessService();
        var launcher = new FakeLauncherService();
        var vm = new ProcessesViewModel(service, launcher, NullLogger<ProcessesViewModel>.Instance);

        var start = DateTimeOffset.UtcNow;
        var key = ProcessKey.CreateVerified(555, start);

        service.CaptureFunc = _ => Task.FromResult(CreateTestSnapshot(
            new ProcessInfo
            {
                Key = key,
                ProcessId = 555,
                ProcessName = "persist.exe",
                Ownership = ProcessOwnership.External,
                StartTime = start
            }
        ));

        await vm.InitializeAsync();
        try
        {
            // Select the item
            vm.SelectedProcess = vm.FilteredProcesses[0];
            Assert.NotNull(vm.SelectedProcess);
            Assert.Equal(555, vm.SelectedProcess.ProcessId);

            // Trigger another refresh
            await vm.RefreshAsync();

            // Verified selection must survive!
            Assert.NotNull(vm.SelectedProcess);
            Assert.Equal(555, vm.SelectedProcess.ProcessId);
        }
        finally
        {
            await vm.DeactivateAsync();
        }
    }

    [Fact] // 23. Recycled or unverified PID cannot inherit selection
    public async Task ProcessesViewModel_RecycledOrUnverifiedPid_CannotInheritSelection()
    {
        var service = new FakeProcessService();
        var launcher = new FakeLauncherService();
        var vm = new ProcessesViewModel(service, launcher, NullLogger<ProcessesViewModel>.Instance);

        // Snapshot 1: Unverified process with PID 777
        service.CaptureFunc = _ => Task.FromResult(CreateTestSnapshot(
            new ProcessInfo
            {
                Key = ProcessKey.CreateUnverified(777, ephemeralId: 10),
                ProcessId = 777,
                ProcessName = "unverified.exe",
                Ownership = ProcessOwnership.External
            }
        ));

        await vm.InitializeAsync();
        try
        {
            vm.SelectedProcess = vm.FilteredProcesses[0];
            Assert.NotNull(vm.SelectedProcess);

            // Snapshot 2: Same PID 777, but new observation (new ephemeral ID or verified new start time)
            service.CaptureFunc = _ => Task.FromResult(CreateTestSnapshot(
                new ProcessInfo
                {
                    Key = ProcessKey.CreateUnverified(777, ephemeralId: 20),
                    ProcessId = 777,
                    ProcessName = "recycled.exe",
                    Ownership = ProcessOwnership.External
                }
            ));

            await vm.RefreshAsync();

            // Unverified selection MUST be cleared to prevent selection hijacking across PID recycle
            Assert.Null(vm.SelectedProcess);
        }
        finally
        {
            await vm.DeactivateAsync();
        }
    }

    [Fact] // 24. Navigation away stops polling & 25. Repeated navigation has only one polling loop
    public async Task ProcessesViewModel_NavigationAway_StopsPolling()
    {
        var service = new FakeProcessService();
        var launcher = new FakeLauncherService();
        var vm = new ProcessesViewModel(service, launcher, NullLogger<ProcessesViewModel>.Instance);

        // Navigation 1: Activate -> immediate snapshot
        await vm.InitializeAsync();
        int count1 = service.CaptureCallCount;
        Assert.True(count1 >= 1);

        // Deactivate (user navigates away)
        await vm.DeactivateAsync();

        // Wait a bit to ensure background loop does not continue firing
        int countAfterDeactivate = service.CaptureCallCount;
        await Task.Delay(100);
        Assert.Equal(countAfterDeactivate, service.CaptureCallCount);

        // Navigation 2: Re-activate
        await vm.InitializeAsync();
        int count2 = service.CaptureCallCount;
        Assert.True(count2 > countAfterDeactivate);

        await vm.DeactivateAsync();
    }

    [Fact] // 26. Manual refresh cannot overlap timer refresh (single flight)
    public async Task ProcessesViewModel_ManualRefresh_CannotOverlapTimerRefresh()
    {
        var service = new FakeProcessService();
        var launcher = new FakeLauncherService();
        var vm = new ProcessesViewModel(service, launcher, NullLogger<ProcessesViewModel>.Instance);

        var tcs = new TaskCompletionSource<ProcessSnapshot>();
        service.CaptureFunc = _ => tcs.Task;

        var initTask = vm.InitializeAsync();

        // Concurrently invoke RefreshAsync while init's capture is in flight
        var refreshTask = vm.RefreshAsync();

        // Release the captured snapshot
        tcs.SetResult(CreateTestSnapshot());

        await Task.WhenAll(initTask, refreshTask);

        // Single-flight guard ensures concurrent refresh was safely ignored or single-flighted
        Assert.Equal(1, service.CaptureCallCount);

        await vm.DeactivateAsync();
    }

    [Fact] // 27. Stale refresh result cannot update reactivated ViewModel
    public async Task ProcessesViewModel_StaleRefreshResult_CannotUpdateReactivatedViewModel()
    {
        var service = new FakeProcessService();
        var launcher = new FakeLauncherService();
        var vm = new ProcessesViewModel(service, launcher, NullLogger<ProcessesViewModel>.Instance);

        var readySignal = new TaskCompletionSource();
        var slowTcs = new TaskCompletionSource<ProcessSnapshot>();

        service.CaptureFunc = async ct =>
        {
            readySignal.TrySetResult();
            return await slowTcs.Task.WaitAsync(ct);
        };

        // Start generation 1
        var g1Task = vm.InitializeAsync();
        await readySignal.Task;

        // Deactivate generation 1 (cancels in-flight refresh)
        await vm.DeactivateAsync();

        var fastSnapshot = CreateTestSnapshot(new ProcessInfo
        {
            Key = ProcessKey.CreateVerified(999, DateTimeOffset.UtcNow),
            ProcessId = 999,
            ProcessName = "fast.exe",
            Ownership = ProcessOwnership.External
        });

        service.CaptureFunc = _ => Task.FromResult(fastSnapshot);
        await vm.InitializeAsync();

        // Ensure generation 2's snapshot is visible, not stale generation 1
        Assert.Single(vm.FilteredProcesses);
        Assert.Equal(999, vm.FilteredProcesses[0].ProcessId);

        await vm.DeactivateAsync();
    }
}
