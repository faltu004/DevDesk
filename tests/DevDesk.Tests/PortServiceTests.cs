using System.IO;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.App.ViewModels.Ports;
using DevDesk.Core.Detection;
using DevDesk.Core.Models;
using DevDesk.Core.Ports;
using DevDesk.Core.Services;
using DevDesk.Infrastructure.Ports;

namespace DevDesk.Tests;

public sealed class PortServiceTests
{
    private readonly FakePortTableProvider _tableProvider;
    private readonly FakeProcessMetadataProvider _processMetadataProvider;
    private readonly FakeProjectService _projectService;
    private readonly PortService _portService;

    public PortServiceTests()
    {
        _tableProvider = new FakePortTableProvider();
        _processMetadataProvider = new FakeProcessMetadataProvider();
        _projectService = new FakeProjectService();

        _portService = new PortService(
            _tableProvider,
            _processMetadataProvider,
            _projectService,
            NullLogger<PortService>.Instance);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(443)]
    [InlineData(3000)]
    [InlineData(5173)]
    [InlineData(65535)]
    public void PortByteOrderHelper_ConvertsBoundaryPortsCorrectly(int port)
    {
        uint networkOrder = PortByteOrderHelper.HostToNetworkOrder(port);
        int convertedBack = PortByteOrderHelper.NetworkToHostOrder(networkOrder);

        Assert.Equal(port, convertedBack);
    }

    [Fact]
    public async Task GetPortSnapshotAsync_Ipv4Listener_MapsCorrectly()
    {
        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(3000, "127.0.0.1:3000", 1234, IsIPv6: false)
        });
        _processMetadataProvider.ProcessNames[1234] = "node.exe";

        var snapshot = await _portService.GetPortSnapshotAsync();

        Assert.True(snapshot.Success);
        Assert.Single(snapshot.ListeningPorts);
        var entry = snapshot.ListeningPorts[0];
        Assert.Equal(3000, entry.Port);
        Assert.Equal("TCP", entry.Protocol);
        Assert.Equal("127.0.0.1:3000", entry.LocalAddress);
        Assert.Equal(1234, entry.OwningProcessId);
        Assert.Equal("node.exe", entry.ProcessName);
        Assert.Equal("Listening", entry.State);
        Assert.False(entry.HasPotentialConflict);
    }

    [Fact]
    public async Task GetPortSnapshotAsync_Ipv6Listener_MapsCorrectly()
    {
        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(8080, "[::]:8080", 5678, IsIPv6: true)
        });
        _processMetadataProvider.ProcessNames[5678] = "dotnet.exe";

        var snapshot = await _portService.GetPortSnapshotAsync();

        Assert.True(snapshot.Success);
        Assert.Single(snapshot.ListeningPorts);
        var entry = snapshot.ListeningPorts[0];
        Assert.Equal(8080, entry.Port);
        Assert.Equal("[::]:8080", entry.LocalAddress);
        Assert.Equal(5678, entry.OwningProcessId);
        Assert.Equal("dotnet.exe", entry.ProcessName);
    }

    [Fact]
    public async Task GetPortSnapshotAsync_DisappearedProcess_FallsBackToUnknown()
    {
        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(5000, "0.0.0.0:5000", 9999, IsIPv6: false)
        });
        _processMetadataProvider.DefaultName = "Unknown"; // Simulated missing process

        var snapshot = await _portService.GetPortSnapshotAsync();

        Assert.True(snapshot.Success);
        Assert.Single(snapshot.ListeningPorts);
        Assert.Equal("Unknown", snapshot.ListeningPorts[0].ProcessName);
    }

    [Fact]
    public async Task GetPortSnapshotAsync_AccessDeniedProcess_FallsBackToProtectedProcess()
    {
        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(445, "0.0.0.0:445", 4, IsIPv6: false)
        });
        _processMetadataProvider.ProcessNames[4] = "System";

        var snapshot = await _portService.GetPortSnapshotAsync();

        Assert.True(snapshot.Success);
        Assert.Single(snapshot.ListeningPorts);
        Assert.Equal("System", snapshot.ListeningPorts[0].ProcessName);
    }

    [Fact]
    public async Task GetPortSnapshotAsync_ConfiguredPortInUse_DetectsPotentialConflict()
    {
        var proj = new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "WebFrontend",
            Path = @"C:\Projects\WebFrontend",
            DefaultPort = 5173
        };
        _projectService.Projects.Add(proj);

        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(5173, "127.0.0.1:5173", 8924, IsIPv6: false)
        });
        _processMetadataProvider.ProcessNames[8924] = "vite.exe";

        var snapshot = await _portService.GetPortSnapshotAsync();

        Assert.True(snapshot.Success);
        Assert.Equal(1, snapshot.ConfiguredProjectPortsCount);
        Assert.Equal(1, snapshot.InUseProjectPortsCount);
        Assert.Equal(0, snapshot.FreeConfiguredPortsCount);
        Assert.Single(snapshot.PotentialConflicts);

        var conflict = snapshot.PotentialConflicts[0];
        Assert.Equal(5173, conflict.Port);
        Assert.Equal("vite.exe", conflict.PrimaryOccupant.ProcessName);
        Assert.Equal(8924, conflict.PrimaryOccupant.OwningProcessId);
        Assert.Contains("WebFrontend", conflict.AffectedProjectNames);
        Assert.Equal("Port 5173 is currently in use by vite.exe (PID 8924).", conflict.Description);

        var portEntry = snapshot.ListeningPorts.Single(p => p.Port == 5173);
        Assert.True(portEntry.HasPotentialConflict);
        Assert.True(portEntry.IsConfiguredProjectPort);
        Assert.Contains("WebFrontend", portEntry.ConfiguredProjectNames);
    }

    [Fact]
    public async Task GetPortSnapshotAsync_FreeConfiguredPort_ProducesNoConflict()
    {
        var proj = new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "ApiServer",
            Path = @"C:\Projects\ApiServer",
            DefaultPort = 8080
        };
        _projectService.Projects.Add(proj);

        // Active listener is on 3000, so 8080 is completely free
        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(3000, "127.0.0.1:3000", 1111, IsIPv6: false)
        });
        _processMetadataProvider.ProcessNames[1111] = "node.exe";

        var snapshot = await _portService.GetPortSnapshotAsync();

        Assert.True(snapshot.Success);
        Assert.Empty(snapshot.PotentialConflicts);
        Assert.Equal(1, snapshot.ConfiguredProjectPortsCount);
        Assert.Equal(1, snapshot.FreeConfiguredPortsCount);
        Assert.Equal(0, snapshot.InUseProjectPortsCount);
    }

    [Fact]
    public async Task GetPortSnapshotAsync_MultipleAddressesSamePort_DeduplicatesConflict()
    {
        var proj = new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "MultiListenApp",
            Path = @"C:\Projects\MultiListenApp",
            DefaultPort = 3000
        };
        _projectService.Projects.Add(proj);

        // Same port 3000 listening on both IPv4 0.0.0.0 and IPv6 [::] with same PID
        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(3000, "0.0.0.0:3000", 2222, IsIPv6: false),
            new RawTcpListener(3000, "[::]:3000", 2222, IsIPv6: true)
        });
        _processMetadataProvider.ProcessNames[2222] = "node.exe";

        var snapshot = await _portService.GetPortSnapshotAsync();

        Assert.True(snapshot.Success);
        Assert.Equal(2, snapshot.ListeningPorts.Count);
        // Only 1 distinct conflict card for port 3000!
        Assert.Single(snapshot.PotentialConflicts);
        Assert.Equal(1, snapshot.InUseProjectPortsCount);
    }

    [Fact]
    public async Task GetPortSnapshotAsync_MultipleOccupantsSamePort_ReportsAdditionalOccupantsCount()
    {
        var proj = new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "App",
            Path = @"C:\Projects\App",
            DefaultPort = 4000
        };
        _projectService.Projects.Add(proj);

        // Two distinct listeners on port 4000 with different PIDs
        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(4000, "127.0.0.1:4000", 100, IsIPv6: false),
            new RawTcpListener(4000, "192.168.1.5:4000", 200, IsIPv6: false)
        });
        _processMetadataProvider.ProcessNames[100] = "app1.exe";
        _processMetadataProvider.ProcessNames[200] = "app2.exe";

        var snapshot = await _portService.GetPortSnapshotAsync();

        Assert.True(snapshot.Success);
        Assert.Single(snapshot.PotentialConflicts);
        var conflict = snapshot.PotentialConflicts[0];
        Assert.Equal(1, conflict.AdditionalOccupantsCount);
        Assert.Equal(2, conflict.AllOccupants.Count);
        Assert.Contains("and 1 other listener(s)", conflict.Description);
    }

    [Fact]
    public async Task GetPortSnapshotAsync_NativePartialFailure_ReturnsUsableSnapshotWithWarning()
    {
        _tableProvider.ResultToReturn = PortTableResult.Ok(
            new[] { new RawTcpListener(3000, "127.0.0.1:3000", 1234, false) },
            partialFailure: true,
            warning: "IPv6 inspection failed");

        var snapshot = await _portService.GetPortSnapshotAsync();

        Assert.True(snapshot.Success);
        Assert.True(snapshot.HasPartialFailure);
        Assert.Equal("IPv6 inspection failed", snapshot.WarningMessage);
        Assert.Single(snapshot.ListeningPorts);
    }

    [Fact]
    public async Task GetPortSnapshotAsync_NativeTotalFailure_ReturnsFailureSnapshot()
    {
        _tableProvider.ResultToReturn = PortTableResult.Failed("Both IPv4 and IPv6 inspections failed");

        var snapshot = await _portService.GetPortSnapshotAsync();

        Assert.False(snapshot.Success);
        Assert.Contains("Both IPv4 and IPv6 inspections failed", snapshot.ErrorMessage);
        Assert.Empty(snapshot.ListeningPorts);
    }

    [Fact]
    public async Task GetPortSnapshotAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _portService.GetPortSnapshotAsync(cts.Token);
        });
    }

    [Fact]
    public async Task PortsViewModel_InitializeAndRefresh_LoadsSnapshotCorrectly()
    {
        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(3000, "127.0.0.1:3000", 1234, false),
            new RawTcpListener(5173, "127.0.0.1:5173", 5678, false)
        });
        _processMetadataProvider.ProcessNames[1234] = "node.exe";
        _processMetadataProvider.ProcessNames[5678] = "vite.exe";

        var proj = new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "FrontendApp",
            Path = @"C:\Projects\FrontendApp",
            DefaultPort = 5173
        };
        _projectService.Projects.Add(proj);

        var vm = new PortsViewModel(_portService, NullLogger<PortsViewModel>.Instance);

        await vm.InitializeAsync();

        Assert.Equal(2, vm.ListeningPorts.Count);
        Assert.Equal(2, vm.FilteredPorts.Count);
        Assert.Single(vm.PotentialConflicts);
        Assert.NotNull(vm.SelectedConflict);
        Assert.Equal(5173, vm.SelectedConflict.Port);
        Assert.Equal(2, vm.TotalListeningPortsCount);
        Assert.Equal(1, vm.PotentialConflictsCount);
        Assert.Equal(1, vm.ConfiguredProjectPortsCount);
        Assert.Equal(0, vm.FreeConfiguredPortsCount);
    }

    [Fact]
    public async Task PortsViewModel_ClientSideFiltering_FiltersAcrossPortProcessAndProject()
    {
        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(3000, "127.0.0.1:3000", 1234, false),
            new RawTcpListener(5173, "127.0.0.1:5173", 5678, false),
            new RawTcpListener(8080, "0.0.0.0:8080", 9999, false)
        });
        _processMetadataProvider.ProcessNames[1234] = "node.exe";
        _processMetadataProvider.ProcessNames[5678] = "vite.exe";
        _processMetadataProvider.ProcessNames[9999] = "java.exe";

        var proj = new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "PortfolioSite",
            Path = @"C:\Projects\PortfolioSite",
            DefaultPort = 5173
        };
        _projectService.Projects.Add(proj);

        var vm = new PortsViewModel(_portService, NullLogger<PortsViewModel>.Instance);
        await vm.InitializeAsync();

        // 1. Filter by port number
        vm.SearchText = "5173";
        Assert.Single(vm.FilteredPorts);
        Assert.Equal(5173, vm.FilteredPorts[0].Port);

        // 2. Filter by process name
        vm.SearchText = "java";
        Assert.Single(vm.FilteredPorts);
        Assert.Equal("java.exe", vm.FilteredPorts[0].ProcessName);

        // 3. Filter by project name
        vm.SearchText = "Portfolio";
        Assert.Single(vm.FilteredPorts);
        Assert.Equal("PortfolioSite", vm.FilteredPorts[0].ProjectDisplay);

        // 4. Clear filter
        vm.SearchText = "";
        Assert.Equal(3, vm.FilteredPorts.Count);
    }

    [Fact]
    public async Task PortsViewModel_ErrorState_ExposesErrorMessageAndAllowsDismiss()
    {
        _tableProvider.ResultToReturn = PortTableResult.Failed("Network table access error");

        var vm = new PortsViewModel(_portService, NullLogger<PortsViewModel>.Instance);
        await vm.InitializeAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("Network table access error", vm.ErrorMessage);

        vm.DismissNotification();
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task PortsViewModel_TotalFailure_ClearsStaleSnapshotData()
    {
        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(3000, "127.0.0.1:3000", 1234, false),
            new RawTcpListener(5173, "127.0.0.1:5173", 5678, false)
        });

        var vm = new PortsViewModel(_portService, NullLogger<PortsViewModel>.Instance);
        await vm.InitializeAsync();

        Assert.Equal(2, vm.ListeningPorts.Count);
        Assert.Equal(2, vm.TotalListeningPortsCount);
        Assert.Null(vm.ErrorMessage);

        // Now native inspection fails completely on refresh
        _tableProvider.ResultToReturn = PortTableResult.Failed("Native table enumeration failed.");
        await vm.RefreshAsync();

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(vm.ListeningPorts);
        Assert.Empty(vm.FilteredPorts);
        Assert.Empty(vm.PotentialConflicts);
        Assert.Equal(0, vm.TotalListeningPortsCount);
        Assert.Equal(0, vm.PotentialConflictsCount);
    }

    [Fact]
    public async Task PortsViewModel_MultipleProjectsWithSameOccupiedPort_TracksAllAffectedProjectsWithoutFabricatingOwnership()
    {
        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(3000, "127.0.0.1:3000", 1234, false)
        });
        _processMetadataProvider.ProcessNames[1234] = "node.exe";

        _projectService.Projects.Add(new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "FrontendApp",
            Path = @"C:\Projects\FrontendApp",
            DefaultPort = 3000
        });
        _projectService.Projects.Add(new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "BackendApp",
            Path = @"C:\Projects\BackendApp",
            DefaultPort = 3000
        });

        var vm = new PortsViewModel(_portService, NullLogger<PortsViewModel>.Instance);
        await vm.InitializeAsync();

        Assert.Single(vm.PotentialConflicts);
        var conflict = vm.PotentialConflicts[0];

        Assert.Equal(3000, conflict.Port);
        Assert.Equal("node.exe", conflict.ProcessName);
        Assert.Equal(1234, conflict.OwningProcessId);
        Assert.Contains("FrontendApp", conflict.AffectedProjectsDisplay);
        Assert.Contains("BackendApp", conflict.AffectedProjectsDisplay);
        Assert.Equal("Port 3000 is currently in use by node.exe (PID 1234).", conflict.Subtitle);
    }

    [Fact]
    public async Task PortsViewModel_MultipleOccupantsOnSamePort_ShowsAdditionalOccupantsText()
    {
        _tableProvider.ResultToReturn = PortTableResult.Ok(new[]
        {
            new RawTcpListener(3000, "127.0.0.1:3000", 1234, false),
            new RawTcpListener(3000, "[::1]:3000", 5678, true)
        });
        _processMetadataProvider.ProcessNames[1234] = "node.exe";
        _processMetadataProvider.ProcessNames[5678] = "docker.exe";

        _projectService.Projects.Add(new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "WebPortal",
            Path = @"C:\Projects\WebPortal",
            DefaultPort = 3000
        });

        var vm = new PortsViewModel(_portService, NullLogger<PortsViewModel>.Instance);
        await vm.InitializeAsync();

        Assert.Single(vm.PotentialConflicts);
        var conflict = vm.PotentialConflicts[0];

        Assert.True(conflict.HasAdditionalOccupants);
        Assert.Equal(1, conflict.AdditionalOccupantsCount);
        Assert.Equal("+1 other occupant(s)", conflict.AdditionalOccupantsText);
        Assert.Contains("and 1 other listener(s).", conflict.Subtitle);
    }

    private sealed class FakePortTableProvider : IWindowsPortTableProvider
    {
        public PortTableResult ResultToReturn { get; set; } = PortTableResult.Ok(Array.Empty<RawTcpListener>());
        public int CallCount { get; private set; }

        public PortTableResult GetListeners(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ResultToReturn;
        }
    }

    private sealed class FakeProcessMetadataProvider : IProcessMetadataProvider
    {
        public Dictionary<int, string> ProcessNames { get; } = new();
        public string DefaultName { get; set; } = "mock.exe";

        public string GetProcessName(int pid)
        {
            return ProcessNames.TryGetValue(pid, out var name) ? name : DefaultName;
        }
    }

    private sealed class FakeProjectService : IProjectService
    {
        public List<DeveloperProject> Projects { get; } = new();

        public Task<IReadOnlyList<DeveloperProject>> GetProjectsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<DeveloperProject>>(Projects.ToList());
        }

        public Task<DeveloperProject?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Projects.FirstOrDefault(p => p.Id == id));

        public Task<DeveloperProject> AddProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default)
        {
            Projects.Add(project);
            return Task.FromResult(project);
        }

        public Task<DeveloperProject> UpdateProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default)
        {
            var idx = Projects.FindIndex(p => p.Id == project.Id);
            if (idx >= 0) Projects[idx] = project;
            return Task.FromResult(project);
        }

        public Task RemoveProjectAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Projects.RemoveAll(p => p.Id == id);
            return Task.CompletedTask;
        }

        public Task<ProjectDetectionResult> DetectAndApplyAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectDetectionResult());
    }
}
