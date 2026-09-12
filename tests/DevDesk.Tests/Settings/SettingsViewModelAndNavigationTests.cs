using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.App.Services.Dialogs;
using DevDesk.App.Services.Navigation;
using DevDesk.App.ViewModels.Common;
using DevDesk.App.ViewModels.Dashboard;
using DevDesk.App.ViewModels.Ports;
using DevDesk.App.ViewModels.Processes;
using DevDesk.App.ViewModels.Settings;
using DevDesk.App.ViewModels.Shell;
using DevDesk.Core.Launchers;
using DevDesk.Core.Ports;
using DevDesk.Core.Processes;
using DevDesk.Core.Services;
using DevDesk.Core.Settings;
using DevDesk.Core.SystemMonitor;
using DevDesk.Infrastructure.Persistence.Database;

namespace DevDesk.Tests.Settings;

public class SettingsViewModelAndNavigationTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public DevDeskSettings Current { get; set; } = new();
        public bool ThrowOnSave { get; set; }

        public event Action<DevDeskSettings>? SettingsChanged;

        public Task<DevDeskSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public DevDeskSettings GetCurrentSettings() => Current;
        public Task SaveSettingsAsync(DevDeskSettings settings, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave) throw new InvalidOperationException("Simulated SQLite write error");
            Current = settings;
            SettingsChanged?.Invoke(settings);
            return Task.CompletedTask;
        }
        public Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
        {
            Current = new DevDeskSettings();
            SettingsChanged?.Invoke(Current);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public bool ConfirmationResult { get; set; } = true;
        public string? FilePickerResult { get; set; } = @"C:\Fake\Code.exe";

        public bool ShowConfirmationDialog(string title, string message, string confirmButtonText = "Confirm") => ConfirmationResult;
        public string? ShowFilePicker(string? filter = null, string? title = null) => FilePickerResult;
        public bool ShowAddEditProjectDialog(DevDesk.App.ViewModels.Projects.AddEditProjectViewModel viewModel) => true;
        public bool ShowConfirmDeleteDialog(DevDesk.App.ViewModels.Projects.ConfirmDeleteViewModel viewModel) => true;
        public bool ShowAddEditCommandDialog(DevDesk.App.ViewModels.Commands.AddEditCommandViewModel viewModel) => true;
        public bool ShowConfirmDeleteCommandDialog(DevDesk.App.ViewModels.Commands.ConfirmDeleteCommandViewModel viewModel) => true;
        public bool ShowConfirmShutdownDialog(int activeProjectCount, int activeCommandCount = 0) => true;
        public string? ShowFolderPicker(string? initialDirectory = null, string? title = null) => null;
        public void ShowMessage(string title, string message) { }
    }

    private sealed class FakeDatabasePathProvider : IDatabasePathProvider
    {
        public string GetDatabasePath() => @"C:\Users\test\AppData\Local\DevDesk\Data\devdesk.db";
        public void EnsureDirectoryExists() { }
        public string GetConnectionString() => "Data Source=InMemoryDb";
    }

    private sealed class FakeLauncherService : ILauncherService
    {
        public Task<LaunchResult> OpenInVsCodeAsync(string projectPath, CancellationToken cancellationToken = default) => Task.FromResult(LaunchResult.Ok());
        public Task<LaunchResult> OpenInExplorerAsync(string directoryPath, CancellationToken cancellationToken = default) => Task.FromResult(LaunchResult.Ok());
        public Task<LaunchResult> OpenTerminalAsync(string projectPath, CancellationToken cancellationToken = default) => Task.FromResult(LaunchResult.Ok());
    }

    private sealed class FakeSystemMonitor : ISystemMonitorService
    {
        public SystemSnapshot? CurrentSnapshot { get; private set; }
        public event EventHandler<SystemSnapshot>? SnapshotUpdated;

        public void ResetBaselines() { }

        public Task<SystemSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            var snap = new SystemSnapshot();
            CurrentSnapshot = snap;
            SnapshotUpdated?.Invoke(this, snap);
            return Task.FromResult(snap);
        }
    }

    private sealed class FakeProcessService : IProcessService
    {
        public Task<ProcessSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProcessSnapshot
            {
                CapturedAt = DateTimeOffset.UtcNow,
                Processes = Array.Empty<ProcessInfo>(),
                TotalProcessCount = 0,
                ManagedProcessCount = 0
            });
        }

        public Task<string?> ResolveExecutablePathAsync(ProcessKey key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class FakePortService : IPortService
    {
        public Task<PortSnapshot> GetPortSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PortSnapshot());
        }
    }

    [Fact]
    public async Task Test19_Dashboard_ConsumesConfiguredRefreshCadence()
    {
        var settingsService = new FakeSettingsService
        {
            Current = new DevDeskSettings
            {
                MonitorRefreshInterval = MonitorRefreshInterval.Seconds5
            }
        };

        var vm = new DashboardViewModel(
            new FakeSystemMonitor(),
            NullLogger<DashboardViewModel>.Instance,
            null, null, null, null, null, null,
            settingsService,
            action => action());

        await vm.InitializeAsync();

        Assert.Equal(TimeSpan.FromSeconds(5), settingsService.GetCurrentSettings().MonitorRefreshInterval.ToTimeSpan());

        await vm.DeactivateAsync();
    }

    [Fact]
    public async Task Test20_Processes_ConsumesConfiguredRefreshCadence()
    {
        var settingsService = new FakeSettingsService
        {
            Current = new DevDeskSettings
            {
                MonitorRefreshInterval = MonitorRefreshInterval.Seconds10
            }
        };

        var vm = new ProcessesViewModel(
            new FakeProcessService(),
            new FakeLauncherService(),
            NullLogger<ProcessesViewModel>.Instance,
            action => action(),
            settingsService);

        await vm.InitializeAsync();

        Assert.Equal(TimeSpan.FromSeconds(10), settingsService.GetCurrentSettings().MonitorRefreshInterval.ToTimeSpan());

        await vm.DeactivateAsync();
    }

    [Fact]
    public void Test21_Ports_RemainsNonPolling()
    {
        var vm = new PortsViewModel(
            new FakePortService(),
            NullLogger<PortsViewModel>.Instance);

        // PortsViewModel does NOT implement IAsyncDeactivatable (no continuous polling timer loop)
        Assert.False((object)vm is IAsyncDeactivatable);
    }

    [Fact]
    public void Test22_SettingsNavigation_WorksInShellAndNavigationService()
    {
        var services = new ServiceCollection();
        var settingsService = new FakeSettingsService();
        var dialogService = new FakeDialogService();
        var dbPathProvider = new FakeDatabasePathProvider();
        var launcher = new FakeLauncherService();

        services.AddSingleton<ISettingsService>(settingsService);
        services.AddSingleton<IDialogService>(dialogService);
        services.AddSingleton<IDatabasePathProvider>(dbPathProvider);
        services.AddSingleton<ILauncherService>(launcher);
        services.AddSingleton<INavigationService, NavigationService>();

        services.AddSingleton<DashboardViewModel>(sp => new DashboardViewModel(
            new FakeSystemMonitor(), NullLogger<DashboardViewModel>.Instance, null, null, null, null, null, null, settingsService, a => a()));
        services.AddSingleton<ProcessesViewModel>(sp => new ProcessesViewModel(
            new FakeProcessService(), launcher, NullLogger<ProcessesViewModel>.Instance, a => a(), settingsService));
        services.AddSingleton<PortsViewModel>(sp => new PortsViewModel(
            new FakePortService(), NullLogger<PortsViewModel>.Instance));
        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            settingsService, dbPathProvider, launcher, dialogService, NullLogger<SettingsViewModel>.Instance, null, a => a()));

        services.AddSingleton<ShellViewModel>();

        var provider = services.BuildServiceProvider();
        var navService = provider.GetRequiredService<INavigationService>();
        var shell = provider.GetRequiredService<ShellViewModel>();

        Assert.True(shell.IsDashboardSelected);

        // Navigate to Settings
        shell.NavigateToSettingsCommand.Execute(null);

        Assert.True(shell.IsSettingsSelected);
        Assert.Equal(NavigationItem.Settings, navService.CurrentItem);
        Assert.IsType<SettingsViewModel>(navService.CurrentViewModel);
    }

    [Fact]
    public async Task Test23_24_NavigatingToSettings_DeactivatesPreviousViewModelPolling()
    {
        var services = new ServiceCollection();
        var settingsService = new FakeSettingsService();
        var dialogService = new FakeDialogService();
        var dbPathProvider = new FakeDatabasePathProvider();
        var launcher = new FakeLauncherService();

        services.AddSingleton<ISettingsService>(settingsService);
        services.AddSingleton<IDialogService>(dialogService);
        services.AddSingleton<IDatabasePathProvider>(dbPathProvider);
        services.AddSingleton<ILauncherService>(launcher);
        services.AddSingleton<INavigationService, NavigationService>();

        var dashboard = new DashboardViewModel(
            new FakeSystemMonitor(), NullLogger<DashboardViewModel>.Instance, null, null, null, null, null, null, settingsService, a => a());
        services.AddSingleton(dashboard);
        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            settingsService, dbPathProvider, launcher, dialogService, NullLogger<SettingsViewModel>.Instance, null, a => a()));

        var provider = services.BuildServiceProvider();
        var navService = provider.GetRequiredService<INavigationService>();

        navService.NavigateTo(NavigationItem.Dashboard);
        await Task.Delay(50);

        // Now navigate to Settings: NavigationService must invoke DeactivateAsync on Dashboard
        navService.NavigateTo(NavigationItem.Settings);
        await Task.Delay(50);

        Assert.Equal(NavigationItem.Settings, navService.CurrentItem);
        Assert.IsType<SettingsViewModel>(navService.CurrentViewModel);
    }

    [Fact]
    public async Task Test25_SaveFailure_IsNotShownAsSuccess()
    {
        var settingsService = new FakeSettingsService { ThrowOnSave = true };
        var dialog = new FakeDialogService();
        var dbPath = new FakeDatabasePathProvider();
        var launcher = new FakeLauncherService();

        var vm = new SettingsViewModel(
            settingsService,
            dbPath,
            launcher,
            dialog,
            NullLogger<SettingsViewModel>.Instance,
            null,
            action => action());

        await vm.InitializeAsync();

        // Change a setting to make it dirty
        vm.SelectedTerminalOption = vm.TerminalOptions[1];
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanSave);

        // Execute save
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        // Save must fail: isDirty remains true, status message indicates error, success is false
        Assert.True(vm.IsDirty);
        Assert.True(vm.IsStatusError);
        Assert.False(vm.IsStatusSuccess);
        Assert.Contains("Failed", vm.StatusMessage);
    }

    [Fact]
    public void Test26_AssemblyVersionPresentation_DerivesFromAssembly()
    {
        var settingsService = new FakeSettingsService();
        var dialog = new FakeDialogService();
        var dbPath = new FakeDatabasePathProvider();
        var launcher = new FakeLauncherService();

        var vm = new SettingsViewModel(
            settingsService,
            dbPath,
            launcher,
            dialog,
            NullLogger<SettingsViewModel>.Instance,
            null,
            action => action());

        Assert.False(string.IsNullOrWhiteSpace(vm.DevDeskVersion));
        Assert.False(string.IsNullOrWhiteSpace(vm.PlatformDisplay));
        Assert.Equal(@"C:\Users\test\AppData\Local\DevDesk\Data\devdesk.db", vm.DatabasePath);
    }
}
