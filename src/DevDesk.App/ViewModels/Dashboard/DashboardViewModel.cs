using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using DevDesk.App.Services.Dialogs;
using DevDesk.App.Services.Navigation;
using DevDesk.App.ViewModels.Common;
using DevDesk.App.ViewModels.Projects;
using DevDesk.Core.Launchers;
using DevDesk.Core.Models;
using DevDesk.Core.Runner;
using DevDesk.Core.Services;
using DevDesk.Core.SystemMonitor;

namespace DevDesk.App.ViewModels.Dashboard;

/// <summary>
/// View model presenting the Dashboard overview state, real-time authoritative host metrics,
/// bounded telemetry history, and single-flight background polling lifecycle.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase, IAsyncInitializable, IAsyncDeactivatable, IDisposable
{
    private readonly ISystemMonitorService _systemMonitorService;
    private readonly ILogger<DashboardViewModel> _logger;
    private readonly INavigationService? _navigationService;
    private readonly IProjectService? _projectService;
    private readonly ILauncherService? _launcherService;
    private readonly IProjectRunnerService? _runnerService;
    private readonly IDialogService? _dialogService;
    private readonly ProjectsViewModel? _projectsViewModel;
    private readonly Action<Action>? _uiDispatcher;

    [ObservableProperty]
    private string _title = "Dashboard";

    [ObservableProperty]
    private string _subtitle = "Your development environment at a glance.";

    [ObservableProperty]
    private string _searchPlaceholder = "Search projects, processes, ports or run a command...";

    [ObservableProperty]
    private string _searchShortcut = "Ctrl + K";

    [ObservableProperty]
    private bool _hasActiveProjects;

    [ObservableProperty]
    private bool _hasRecentProjects;

    [ObservableProperty]
    private bool _canOpenVsCode = true;

    [ObservableProperty]
    private string _vsCodeTooltip = "Open current or recent project in Visual Studio Code";

    // --- Authoritative Host Telemetry Observable Properties ---

    [ObservableProperty]
    private string _cpuValue = "—";

    [ObservableProperty]
    private string _cpuSubtext = "—";

    [ObservableProperty]
    private string _cpuSparklinePoints = "0,14 56,14";

    [ObservableProperty]
    private string _ramValue = "—";

    [ObservableProperty]
    private string _ramSubtext = "—";

    [ObservableProperty]
    private double _ramProgressWidth = 0.0;

    [ObservableProperty]
    private string _storageValue = "—";

    [ObservableProperty]
    private string _storageSubtext = "—";

    [ObservableProperty]
    private double _storageProgressWidth = 0.0;

    [ObservableProperty]
    private string _networkValue = "—";

    [ObservableProperty]
    private string _networkSubtext = "—";

    [ObservableProperty]
    private double _networkBar1Height = 3.0;

    [ObservableProperty]
    private double _networkBar2Height = 3.0;

    [ObservableProperty]
    private double _networkBar3Height = 3.0;

    [ObservableProperty]
    private double _networkBar4Height = 3.0;

    [ObservableProperty]
    private double _networkBar5Height = 3.0;

    [ObservableProperty]
    private string _uptimeText = "—";

    [ObservableProperty]
    private string _osDescriptionText = "Windows";

    // Legacy collection retained for item-binding compatibility
    public ObservableCollection<DashboardMetricItem> SystemMetrics { get; } = new();

    public ObservableCollection<DashboardProjectItem> ActiveProjects { get; } = new();
    public ObservableCollection<DashboardEnvSummaryItem> EnvironmentSummary { get; } = new();
    public ObservableCollection<DashboardRecentProjectItem> RecentProjects { get; } = new();
    public ObservableCollection<DashboardQuickActionItem> QuickActions { get; } = new();

    // Bounded in-memory telemetry histories (maximum 60 samples, zero persistence)
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _networkHistory = new();
    private const int MaxHistorySamples = 60;

    // Polling lifecycle management
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingLoopTask;
    private long _activeGeneration;

    public bool IsPollingActive => _pollingCts != null && !_pollingCts.IsCancellationRequested;

    public DashboardViewModel(
        ISystemMonitorService systemMonitorService,
        ILogger<DashboardViewModel> logger,
        Action<Action>? uiDispatcher = null)
        : this(systemMonitorService, logger, null, null, null, null, null, null, uiDispatcher)
    {
    }

    public DashboardViewModel(
        ISystemMonitorService systemMonitorService,
        ILogger<DashboardViewModel> logger,
        INavigationService? navigationService,
        IProjectService? projectService,
        ILauncherService? launcherService,
        IProjectRunnerService? runnerService,
        IDialogService? dialogService,
        ProjectsViewModel? projectsViewModel,
        Action<Action>? uiDispatcher = null)
    {
        _systemMonitorService = systemMonitorService ?? throw new ArgumentNullException(nameof(systemMonitorService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _navigationService = navigationService;
        _projectService = projectService;
        _launcherService = launcherService;
        _runnerService = runnerService;
        _dialogService = dialogService;
        _projectsViewModel = projectsViewModel;
        _uiDispatcher = uiDispatcher ?? (Action<Action>)(action =>
        {
            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        });

        if (_runnerService != null)
        {
            _runnerService.SessionChanged += OnSessionChanged;
        }

        // Seed legacy metrics collection with default placeholders
        SystemMetrics.Add(new DashboardMetricItem { Title = "CPU", Value = "—", Subtext = "—", IconType = "CPU", VisualType = "Sparkline" });
        SystemMetrics.Add(new DashboardMetricItem { Title = "RAM", Value = "—", Subtext = "—", IconType = "RAM", VisualType = "Progress" });
        SystemMetrics.Add(new DashboardMetricItem { Title = "Storage", Value = "—", Subtext = "—", IconType = "Disk", VisualType = "Progress" });
        SystemMetrics.Add(new DashboardMetricItem { Title = "Network", Value = "—", Subtext = "—", IconType = "Network", VisualType = "Histogram" });

        EnvironmentSummary.Add(new DashboardEnvSummaryItem
        {
            Title = "Active Projects",
            Value = "0",
            Subtext = "of 0 total",
            IconKind = "Projects",
            IconBrushKey = "Brush.Accent.Light"
        });
        EnvironmentSummary.Add(new DashboardEnvSummaryItem
        {
            Title = "Active Dev Ports",
            Value = "0",
            Subtext = "open and in use",
            IconKind = "Ports",
            IconBrushKey = "Brush.Accent.Light"
        });
        EnvironmentSummary.Add(new DashboardEnvSummaryItem
        {
            Title = "Git Changes",
            Value = "—",
            Subtext = "status",
            IconKind = "Git",
            IconBrushKey = "Brush.Status.Success"
        });
        EnvironmentSummary.Add(new DashboardEnvSummaryItem
        {
            Title = "Port Conflicts",
            Value = "0",
            Subtext = "all clear",
            IconKind = "Conflicts",
            IconBrushKey = "Brush.Status.Danger"
        });

        // Initialize real projects from persistence if available
        if (_projectService != null)
        {
            try
            {
                var task = _projectService.GetProjectsAsync();
                if (task.IsCompletedSuccessfully)
                {
                    ApplyProjects(task.Result);
                }
                else
                {
                    _ = LoadProjectsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Initial project retrieval encountered an issue; will retry on initialization");
            }
        }
        else
        {
            UpdateVsCodeAvailability();
        }

        RecentProjects.CollectionChanged += (s, e) => HasRecentProjects = RecentProjects.Count > 0;

        QuickActions = new ObservableCollection<DashboardQuickActionItem>
        {
            new()
            {
                Title = "Add Project",
                Description = "Manually add a project folder",
                IconKind = "Add"
            },
            new()
            {
                Title = "Detect Project",
                Description = "Scan for development projects",
                IconKind = "Detect"
            },
            new()
            {
                Title = "Open VS Code",
                Description = "Open current workspace",
                IconKind = "VSCode"
            },
            new()
            {
                Title = "Check Ports",
                Description = "Scan for port conflicts",
                IconKind = "Ports"
            }
        };
    }

    public async Task InitializeAsync()
    {
        await DeactivateAsync();

        long generation = Interlocked.Increment(ref _activeGeneration);
        _pollingCts = new CancellationTokenSource();
        var ct = _pollingCts.Token;

        // Reset delta sampling baselines on activation
        _systemMonitorService.ResetBaselines();

        // Immediate capture
        await RefreshSnapshotAsync(generation, ct);

        // Exactly one background polling loop
        _pollingLoopTask = RunPollingLoopAsync(generation, ct);

        // Load real project data from persistence
        await LoadProjectsAsync();
    }

    public async Task DeactivateAsync()
    {
        Interlocked.Increment(ref _activeGeneration);

        if (_pollingCts != null)
        {
            _pollingCts.Cancel();
            try
            {
                if (_pollingLoopTask != null)
                {
                    await _pollingLoopTask;
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _pollingCts.Dispose();
                _pollingCts = null;
                _pollingLoopTask = null;
            }
        }

        await _refreshGate.WaitAsync().ConfigureAwait(false);
        _refreshGate.Release();
    }

    private async Task RunPollingLoopAsync(long generation, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1500));
        try
        {
            while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
            {
                if (ct.IsCancellationRequested || generation != Volatile.Read(ref _activeGeneration))
                {
                    break;
                }

                await RefreshSnapshotAsync(generation, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var ct = _pollingCts?.Token ?? CancellationToken.None;
        await RefreshSnapshotAsync(Volatile.Read(ref _activeGeneration), ct);
    }

    private async Task RefreshSnapshotAsync(long generation, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || generation != Volatile.Read(ref _activeGeneration))
        {
            return;
        }

        if (!await _refreshGate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (ct.IsCancellationRequested || generation != Volatile.Read(ref _activeGeneration))
            {
                return;
            }

            var snapshot = await _systemMonitorService.CaptureSnapshotAsync(ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested || generation != Volatile.Read(ref _activeGeneration))
            {
                return;
            }

            ApplySnapshotToViewModel(snapshot);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh system telemetry snapshot on Dashboard.");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void ApplySnapshotToViewModel(SystemSnapshot snapshot)
    {
        DispatchOnUiThread(() =>
        {
            // 1. CPU
            if (snapshot.Cpu.IsAvailable && snapshot.Cpu.UsagePercentage.HasValue)
            {
                double cpuPercent = snapshot.Cpu.UsagePercentage.Value;
                CpuValue = $"{cpuPercent:F0}%";
                CpuSubtext = $"{snapshot.Cpu.LogicalProcessorCount} Cores";

                _cpuHistory.Enqueue(cpuPercent);
                while (_cpuHistory.Count > MaxHistorySamples)
                {
                    _cpuHistory.Dequeue();
                }

                CpuSparklinePoints = BuildSparklinePoints(_cpuHistory);
            }
            else
            {
                CpuValue = "—";
                CpuSubtext = snapshot.Cpu.LogicalProcessorCount > 0
                    ? $"{snapshot.Cpu.LogicalProcessorCount} Cores"
                    : "—";
            }

            // 2. RAM
            if (snapshot.Memory.IsAvailable && snapshot.Memory.TotalPhysicalBytes > 0)
            {
                double usedGb = snapshot.Memory.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0);
                double totalGb = snapshot.Memory.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0);
                double percent = snapshot.Memory.UsagePercentage ?? 0.0;

                RamValue = $"{usedGb:F1} / {totalGb:F1} GB";
                RamSubtext = $"{percent:F0}% in use";
                RamProgressWidth = Math.Clamp((percent / 100.0) * 76.0, 0.0, 76.0);
            }
            else
            {
                RamValue = "—";
                RamSubtext = "Unavailable";
                RamProgressWidth = 0.0;
            }

            // 3. Storage
            var primaryDrive = snapshot.Disk.PrimarySystemDrive;
            if (primaryDrive is { IsReady: true, TotalBytes: > 0 })
            {
                double usedGb = primaryDrive.UsedBytes / (1024.0 * 1024.0 * 1024.0);
                double totalGb = primaryDrive.TotalBytes / (1024.0 * 1024.0 * 1024.0);
                double percent = primaryDrive.UsagePercentage ?? 0.0;
                string driveLetter = !string.IsNullOrWhiteSpace(primaryDrive.DriveName)
                    ? primaryDrive.DriveName.TrimEnd('\\')
                    : "C:";

                StorageValue = $"{driveLetter} {percent:F0}%";
                StorageSubtext = $"{usedGb:F0} / {totalGb:F0} GB used";
                StorageProgressWidth = Math.Clamp((percent / 100.0) * 76.0, 0.0, 76.0);
            }
            else
            {
                StorageValue = "—";
                StorageSubtext = "Unavailable";
                StorageProgressWidth = 0.0;
            }

            // 4. Network
            if (snapshot.Network.IsAvailable && snapshot.Network.TotalBytesPerSecond.HasValue)
            {
                double rx = snapshot.Network.RxBytesPerSecond!.Value;
                double tx = snapshot.Network.TxBytesPerSecond!.Value;
                double total = snapshot.Network.TotalBytesPerSecond.Value;

                NetworkValue = FormatTotalThroughput(total);
                NetworkSubtext = $"↓ {FormatRate(rx)}   ↑ {FormatRate(tx)}";

                _networkHistory.Enqueue(total);
                while (_networkHistory.Count > MaxHistorySamples)
                {
                    _networkHistory.Dequeue();
                }

                UpdateHistogramBars(_networkHistory);
            }
            else
            {
                NetworkValue = "—";
                NetworkSubtext = "—";
                NetworkBar1Height = 3.0;
                NetworkBar2Height = 3.0;
                NetworkBar3Height = 3.0;
                NetworkBar4Height = 3.0;
                NetworkBar5Height = 3.0;
            }

            // 5. System Info & Uptime
            UptimeText = FormatUptime(snapshot.SystemInfo.Uptime);
            OsDescriptionText = !string.IsNullOrWhiteSpace(snapshot.SystemInfo.OsDescription)
                ? snapshot.SystemInfo.OsDescription
                : "Windows";
        });
    }

    private static string BuildSparklinePoints(Queue<double> history)
    {
        if (history.Count < 2)
        {
            return "0,14 56,14";
        }

        var points = new List<string>(history.Count);
        double step = 56.0 / (history.Count - 1);
        int i = 0;

        foreach (var val in history)
        {
            double x = i * step;
            // Map 0% to y=16 (bottom), 100% to y=2 (top)
            double y = 16.0 - (Math.Clamp(val, 0.0, 100.0) / 100.0 * 14.0);
            points.Add($"{x:F1},{y:F1}");
            i++;
        }

        return string.Join(" ", points);
    }

    private void UpdateHistogramBars(Queue<double> history)
    {
        if (history.Count == 0)
        {
            NetworkBar1Height = 3.0;
            NetworkBar2Height = 3.0;
            NetworkBar3Height = 3.0;
            NetworkBar4Height = 3.0;
            NetworkBar5Height = 3.0;
            return;
        }

        var recent = history.TakeLast(5).ToList();
        while (recent.Count < 5)
        {
            recent.Insert(0, 0.0);
        }

        double max = Math.Max(100_000.0, recent.Max()); // At least 100 KB/s scale to avoid tiny noise maxing out

        NetworkBar1Height = Math.Clamp((recent[0] / max) * 15.0 + 3.0, 3.0, 18.0);
        NetworkBar2Height = Math.Clamp((recent[1] / max) * 15.0 + 3.0, 3.0, 18.0);
        NetworkBar3Height = Math.Clamp((recent[2] / max) * 15.0 + 3.0, 3.0, 18.0);
        NetworkBar4Height = Math.Clamp((recent[3] / max) * 15.0 + 3.0, 3.0, 18.0);
        NetworkBar5Height = Math.Clamp((recent[4] / max) * 15.0 + 3.0, 3.0, 18.0);
    }

    private static string FormatRate(double bytesPerSec)
    {
        double bps = bytesPerSec * 8.0;
        if (bps >= 1_000_000.0)
        {
            return $"{bps / 1_000_000.0:F1} Mbps";
        }
        if (bps >= 1_000.0)
        {
            return $"{bps / 1_000.0:F0} Kbps";
        }
        return $"{bps:F0} bps";
    }

    private static string FormatTotalThroughput(double totalBytesPerSec)
    {
        double bps = totalBytesPerSec * 8.0;
        if (bps >= 1_000_000.0)
        {
            return $"{bps / 1_000_000.0:F1} Mbps";
        }
        if (bps >= 1_000.0)
        {
            return $"{bps / 1_000.0:F0} Kbps";
        }
        return "0 Mbps";
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1.0)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        }
        if (uptime.TotalHours >= 1.0)
        {
            return $"{uptime.Hours}h {uptime.Minutes}m";
        }
        return $"{uptime.Minutes}m {uptime.Seconds}s";
    }

    private void DispatchOnUiThread(Action action)
    {
        if (_uiDispatcher != null)
        {
            _uiDispatcher(action);
        }
        else if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    [RelayCommand]
    public void ViewAllProjects()
    {
        _navigationService?.NavigateTo(NavigationItem.Projects);
    }

    [RelayCommand]
    public void CheckPorts()
    {
        _navigationService?.NavigateTo(NavigationItem.Ports);
    }

    [RelayCommand]
    public async Task AddProjectAsync()
    {
        if (_dialogService == null || _projectService == null)
        {
            return;
        }

        try
        {
            var selectedPath = _dialogService.ShowFolderPicker(title: "Select Project Directory");
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            var addVm = new AddEditProjectViewModel(selectedPath);
            if (_dialogService.ShowAddEditProjectDialog(addVm))
            {
                var newProject = addVm.ToProject();
                var saved = await _projectService.AddProjectAsync(newProject);
                await LoadProjectsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add project from Dashboard");
        }
    }

    [RelayCommand]
    public async Task DetectProjectAsync(object? parameter)
    {
        if (_dialogService == null || _projectService == null)
        {
            return;
        }

        try
        {
            string? targetPath = ExtractProjectPath(parameter);
            Guid? targetId = ExtractProjectId(parameter);

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                targetPath = _dialogService.ShowFolderPicker(title: "Select Project Directory to Detect");
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    return;
                }
            }

            var projects = await _projectService.GetProjectsAsync();
            var existing = projects.FirstOrDefault(p =>
                (targetId.HasValue && p.Id == targetId.Value) ||
                string.Equals(p.Path, targetPath, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                var result = await _projectService.DetectAndApplyAsync(existing.Id);
                _logger.LogInformation("Detection applied to registered project {Name}: Framework={Framework}", existing.Name, result.Framework);
            }
            else
            {
                var addVm = new AddEditProjectViewModel(targetPath);
                if (_dialogService.ShowAddEditProjectDialog(addVm))
                {
                    var newProject = addVm.ToProject();
                    var saved = await _projectService.AddProjectAsync(newProject);
                    await _projectService.DetectAndApplyAsync(saved.Id);
                }
            }

            await LoadProjectsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect project from Dashboard");
        }
    }

    [RelayCommand]
    public async Task OpenProjectAsync(object? parameter)
    {
        if (_launcherService == null)
        {
            return;
        }

        string? path = ExtractProjectPath(parameter);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var result = await _launcherService.OpenInExplorerAsync(path);
            if (!result.Success)
            {
                _logger.LogWarning("Failed to open project in Explorer for '{Path}': {Error}", path, result.ErrorMessage);
            }
            else if (_projectService != null)
            {
                var projects = await _projectService.GetProjectsAsync();
                var match = projects.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    match.LastOpenedAt = DateTimeOffset.UtcNow;
                    await _projectService.UpdateProjectAsync(match);
                    await LoadProjectsAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening Explorer for '{Path}'", path);
        }
    }

    [RelayCommand]
    public async Task OpenVsCodeAsync(object? parameter)
    {
        if (_launcherService == null)
        {
            return;
        }

        string? path = ExtractProjectPath(parameter);

        // If no explicit row parameter was passed, resolve project context
        if (string.IsNullOrWhiteSpace(path))
        {
            if (_projectsViewModel?.SelectedProject != null)
            {
                path = _projectsViewModel.SelectedProject.Path;
            }
            else if (RecentProjects.Count > 0)
            {
                path = RecentProjects[0].Path;
            }
            else if (ActiveProjects.Count == 1)
            {
                path = ActiveProjects[0].Path;
            }
            else if (ActiveProjects.Count > 1)
            {
                // Ambiguous: multiple projects exist but none selected/recent.
                // Present existing project selection workflow by navigating to Projects view.
                _navigationService?.NavigateTo(NavigationItem.Projects);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogInformation("Open VS Code requested without project context; no projects available.");
            return;
        }

        try
        {
            var result = await _launcherService.OpenInVsCodeAsync(path);
            if (!result.Success)
            {
                _logger.LogWarning("Failed to launch VS Code for project path '{Path}': {Error}", path, result.ErrorMessage);
            }
            else if (_projectService != null)
            {
                var projects = await _projectService.GetProjectsAsync();
                var match = projects.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    match.LastOpenedAt = DateTimeOffset.UtcNow;
                    await _projectService.UpdateProjectAsync(match);
                    await LoadProjectsAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error launching VS Code for path '{Path}'", path);
        }
    }

    [RelayCommand]
    public async Task OpenTerminalAsync(object? parameter)
    {
        if (_launcherService == null)
        {
            return;
        }

        string? path = ExtractProjectPath(parameter);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var result = await _launcherService.OpenTerminalAsync(path);
            if (!result.Success)
            {
                _logger.LogWarning("Failed to open terminal for project path '{Path}': {Error}", path, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening terminal for path '{Path}'", path);
        }
    }

    [RelayCommand]
    public void ViewLogs(object? parameter)
    {
        string? path = ExtractProjectPath(parameter);
        string? name = ExtractProjectName(parameter);
        Guid? id = ExtractProjectId(parameter);

        if (_projectsViewModel != null)
        {
            var match = _projectsViewModel.Projects.FirstOrDefault(p =>
                (id.HasValue && p.Id == id.Value) ||
                (!string.IsNullOrWhiteSpace(path) && string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(name) && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));

            if (match != null)
            {
                _projectsViewModel.SelectedProject = match;
            }

            _projectsViewModel.SelectDetailsTab("Logs");
        }

        _navigationService?.NavigateTo(NavigationItem.Projects);
    }

    [RelayCommand]
    public async Task RunProjectAsync(object? parameter)
    {
        if (parameter is not DashboardProjectItem projectItem)
        {
            return;
        }

        try
        {
            Guid targetId = projectItem.Id;

            if (_runnerService != null)
            {
                var result = await _runnerService.StartProjectAsync(targetId);
                if (!result.Success)
                {
                    _logger.LogWarning("Failed to start project {Name} ({Id}): {Error}", projectItem.Name, targetId, result.ErrorMessage);
                }
            }

            projectItem.IsRunning = true;
            projectItem.StatusText = "Running";

            if (_projectService != null)
            {
                var project = await _projectService.GetProjectByIdAsync(targetId);
                if (project != null)
                {
                    project.LastOpenedAt = DateTimeOffset.UtcNow;
                    await _projectService.UpdateProjectAsync(project);
                    await LoadProjectsAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error running project {Name}", projectItem.Name);
        }
    }

    [RelayCommand]
    public async Task StopProjectAsync(object? parameter)
    {
        if (parameter is not DashboardProjectItem projectItem)
        {
            return;
        }

        try
        {
            Guid targetId = projectItem.Id;

            if (_runnerService != null)
            {
                var result = await _runnerService.StopProjectAsync(targetId);
                if (!result.Success)
                {
                    _logger.LogWarning("Failed to stop project {Name} ({Id}): {Error}", projectItem.Name, targetId, result.ErrorMessage);
                }
            }

            projectItem.IsRunning = false;
            projectItem.StatusText = "Stopped";
            await LoadProjectsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error stopping project {Name}", projectItem.Name);
        }
    }

    [RelayCommand]
    public async Task ClearHistoryAsync()
    {
        RecentProjects.Clear();
        HasRecentProjects = false;

        if (_projectService != null)
        {
            try
            {
                var projects = await _projectService.GetProjectsAsync();
                foreach (var p in projects.Where(x => x.LastOpenedAt.HasValue))
                {
                    p.LastOpenedAt = null;
                    await _projectService.UpdateProjectAsync(p);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reset project last-opened history in persistence");
            }
        }
    }

    private void OnSessionChanged(object? sender, ProjectRunSession session)
    {
        DispatchOnUiThread(() =>
        {
            var match = ActiveProjects.FirstOrDefault(p => p.Id == session.ProjectId);
            if (match != null)
            {
                match.IsRunning = session.State == ProjectRunState.Running;
                match.StatusText = session.State switch
                {
                    ProjectRunState.Running => "Running",
                    ProjectRunState.Starting => "Starting",
                    ProjectRunState.Stopping => "Stopping",
                    _ => "Stopped"
                };
            }
            if (_projectService != null)
            {
                _ = LoadProjectsAsync();
            }
        });
    }

    public async Task LoadProjectsAsync()
    {
        if (_projectService == null) return;
        try
        {
            var projects = await _projectService.GetProjectsAsync();
            ApplyProjects(projects);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load projects for Dashboard");
        }
    }

    public void ApplyProjects(IReadOnlyList<DeveloperProject> projects)
    {
        DispatchOnUiThread(() =>
        {
            ActiveProjects.Clear();
            foreach (var project in projects)
            {
                var session = _runnerService?.GetSession(project.Id);
                bool isRunning = session?.State == ProjectRunState.Running;
                string statusText = isRunning
                    ? "Running"
                    : session?.State switch
                    {
                        ProjectRunState.Starting => "Starting",
                        ProjectRunState.Stopping => "Stopping",
                        _ => "Stopped"
                    };
                string portText = isRunning && project.DefaultPort.HasValue
                    ? $"Port {project.DefaultPort.Value}"
                    : "Port —";
                bool hasActivePort = isRunning && project.DefaultPort.HasValue;

                var item = new DashboardProjectItem
                {
                    Id = project.Id,
                    Name = project.Name,
                    Path = project.Path,
                    FrameworkBadge = !string.IsNullOrWhiteSpace(project.Framework) ? project.Framework : "Generic",
                    CategoryBadge = !string.IsNullOrWhiteSpace(project.Language)
                        ? project.Language
                        : (!string.IsNullOrWhiteSpace(project.PackageManager) ? project.PackageManager : "Project"),
                    StatusText = statusText,
                    IsRunning = isRunning,
                    PortText = portText,
                    HasActivePort = hasActivePort,
                    IconKind = ResolveIconKind(project.Framework)
                };
                ActiveProjects.Add(item);
            }
            HasActiveProjects = ActiveProjects.Count > 0;

            RecentProjects.Clear();
            var recentList = projects
                .Where(p => p.LastOpenedAt.HasValue)
                .OrderByDescending(p => p.LastOpenedAt!.Value)
                .Take(5)
                .ToList();

            foreach (var p in recentList)
            {
                var session = _runnerService?.GetSession(p.Id);
                bool isRunning = session?.State == ProjectRunState.Running;
                RecentProjects.Add(new DashboardRecentProjectItem
                {
                    Id = p.Id,
                    Name = p.Name,
                    Path = p.Path,
                    LastOpenedText = FormatRelativeTime(p.LastOpenedAt!.Value),
                    StatusColorBrushKey = isRunning ? "Brush.Status.Success" : "Brush.Text.Muted",
                    IconKind = ResolveIconKind(p.Framework)
                });
            }
            HasRecentProjects = RecentProjects.Count > 0;

            UpdateEnvironmentSummary(projects);
            UpdateVsCodeAvailability();
        });
    }

    private void UpdateEnvironmentSummary(IReadOnlyList<DeveloperProject> projects)
    {
        if (EnvironmentSummary.Count > 0)
        {
            int runningCount = projects.Count(p => _runnerService?.GetSession(p.Id)?.State == ProjectRunState.Running);
            EnvironmentSummary[0].Value = runningCount.ToString();
            EnvironmentSummary[0].Subtext = $"of {projects.Count} total";
        }
    }

    private void UpdateVsCodeAvailability()
    {
        if (ActiveProjects.Count == 0 && RecentProjects.Count == 0 && _projectsViewModel?.SelectedProject == null)
        {
            CanOpenVsCode = false;
            VsCodeTooltip = "No project context available. Add a project first to open in Visual Studio Code.";
        }
        else
        {
            CanOpenVsCode = true;
            VsCodeTooltip = "Open current or recent project in Visual Studio Code";
        }
    }

    private static string FormatRelativeTime(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.UtcNow - timestamp;
        if (elapsed.TotalMinutes < 1) return "Just now";
        if (elapsed.TotalMinutes < 60)
        {
            var m = (int)elapsed.TotalMinutes;
            return $"{m} {(m == 1 ? "minute" : "minutes")} ago";
        }
        if (elapsed.TotalHours < 24)
        {
            var h = (int)elapsed.TotalHours;
            return $"{h} {(h == 1 ? "hour" : "hours")} ago";
        }
        if (elapsed.TotalDays < 30)
        {
            var d = (int)elapsed.TotalDays;
            return $"{d} {(d == 1 ? "day" : "days")} ago";
        }
        return timestamp.ToString("MMM d, yyyy");
    }

    private static string ResolveIconKind(string? framework)
    {
        if (string.IsNullOrWhiteSpace(framework)) return "Folder";
        if (framework.Contains("Vite", StringComparison.OrdinalIgnoreCase) ||
            framework.Contains("React", StringComparison.OrdinalIgnoreCase) ||
            framework.Contains("Vue", StringComparison.OrdinalIgnoreCase))
        {
            return "Vite";
        }
        if (framework.Contains("Next", StringComparison.OrdinalIgnoreCase))
        {
            return "Next";
        }
        if (framework.Contains(".NET", StringComparison.OrdinalIgnoreCase) ||
            framework.Contains("DotNet", StringComparison.OrdinalIgnoreCase) ||
            framework.Contains("C#", StringComparison.OrdinalIgnoreCase))
        {
            return "DotNet";
        }
        return "Folder";
    }

    private static string? ExtractProjectPath(object? parameter) => parameter switch
    {
        DashboardProjectItem p => p.Path,
        DashboardRecentProjectItem r => r.Path,
        string s => s,
        _ => null
    };

    private static string? ExtractProjectName(object? parameter) => parameter switch
    {
        DashboardProjectItem p => p.Name,
        DashboardRecentProjectItem r => r.Name,
        _ => null
    };

    private static Guid? ExtractProjectId(object? parameter) => parameter switch
    {
        DashboardProjectItem p => p.Id,
        DashboardRecentProjectItem r => r.Id,
        Guid g => g,
        _ => null
    };

    public void Dispose()
    {
        if (_runnerService != null)
        {
            _runnerService.SessionChanged -= OnSessionChanged;
        }
    }
}
