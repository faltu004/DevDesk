using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using DevDesk.App.ViewModels.Common;
using DevDesk.Core.Launchers;
using DevDesk.Core.Processes;

namespace DevDesk.App.ViewModels.Processes;

public enum ProcessFilterMode
{
    All = 0,
    DevDeskManaged = 1,
    External = 2
}

public enum ProcessSortColumn
{
    Cpu = 0,
    Memory = 1,
    Pid = 2,
    Name = 3
}

/// <summary>
/// Root ViewModel coordinating read-only Windows process inspection, truthful CPU metrics,
/// single-flight background polling, and authoritative DevDesk runner ownership visualization.
/// </summary>
public sealed partial class ProcessesViewModel : ViewModelBase, IAsyncInitializable, IAsyncDeactivatable
{
    private readonly IProcessService _processService;
    private readonly ILauncherService _launcherService;
    private readonly ILogger<ProcessesViewModel> _logger;
    private readonly Action<Action>? _uiDispatcher;

    [ObservableProperty]
    private string _title = "Processes";

    [ObservableProperty]
    private string _subtitle = "Monitor active development processes on this machine.";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ProcessFilterMode _filterMode = ProcessFilterMode.All;

    public int FilterModeIndex
    {
        get => (int)FilterMode;
        set
        {
            if (value >= 0 && value <= 2 && (int)FilterMode != value)
            {
                FilterMode = (ProcessFilterMode)value;
            }
        }
    }

    [ObservableProperty]
    private ProcessSortColumn _sortColumn = ProcessSortColumn.Cpu;

    [ObservableProperty]
    private bool _sortAscending = false;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _observedProcessesCount;

    [ObservableProperty]
    private int _devDeskManagedCount;

    [ObservableProperty]
    private string _sampledCpuText = "—";

    [ObservableProperty]
    private string _combinedWorkingSetText = "—";

    [ObservableProperty]
    private ProcessItemPresentationModel? _selectedProcess;

    public ObservableCollection<ProcessItemPresentationModel> FilteredProcesses { get; } = new();

    private readonly List<ProcessItemPresentationModel> _allProcesses = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingLoopTask;
    private long _activeGeneration;

    public ProcessesViewModel(
        IProcessService processService,
        ILauncherService launcherService,
        ILogger<ProcessesViewModel> logger,
        Action<Action>? uiDispatcher = null)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _launcherService = launcherService ?? throw new ArgumentNullException(nameof(launcherService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _uiDispatcher = uiDispatcher;
    }

    public async Task InitializeAsync()
    {
        await DeactivateAsync();

        long generation = Interlocked.Increment(ref _activeGeneration);
        _pollingCts = new CancellationTokenSource();
        var ct = _pollingCts.Token;

        // Immediate initial snapshot
        await RefreshSnapshotAsync(generation, ct);

        // Single background polling loop
        _pollingLoopTask = RunPollingLoopAsync(generation, ct);
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

        // Ensure in-flight refresh has exited and released the gate
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

        // Single-flight guard: do not allow concurrent refresh runs
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

            if (_allProcesses.Count == 0)
            {
                SetPropertyOnDispatcher(() => IsLoading = true);
            }

            var snapshot = await _processService.CaptureSnapshotAsync(ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested || generation != Volatile.Read(ref _activeGeneration))
            {
                return;
            }

            ApplySnapshotToUi(snapshot, generation);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture process snapshot.");
        }
        finally
        {
            SetPropertyOnDispatcher(() => IsLoading = false);
            _refreshGate.Release();
        }
    }

    private void ApplySnapshotToUi(ProcessSnapshot snapshot, long generation)
    {
        void UpdateAction()
        {
            if (generation != Volatile.Read(ref _activeGeneration))
            {
                return;
            }

            ObservedProcessesCount = snapshot.TotalProcessCount;
            DevDeskManagedCount = snapshot.ManagedProcessCount;
            SampledCpuText = snapshot.SampledCpuPercent.HasValue
                ? $"{snapshot.SampledCpuPercent.Value:F1}%"
                : "—";
            CombinedWorkingSetText = ProcessItemPresentationModel.FormatBytes(snapshot.CombinedWorkingSetBytes);

            var oldSelectionKey = SelectedProcess?.Key;
            bool wasSelectionVerifiable = oldSelectionKey.HasValue && oldSelectionKey.Value.IsVerifiable;

            _allProcesses.Clear();
            foreach (var p in snapshot.Processes)
            {
                _allProcesses.Add(new ProcessItemPresentationModel(p));
            }

            ApplyFilterAndSort();

            // Selection preservation: only verified process identities survive across refreshes
            if (wasSelectionVerifiable)
            {
                var matched = FilteredProcesses.FirstOrDefault(p => p.Key.Equals(oldSelectionKey!.Value));
                SelectedProcess = matched;
            }
            else
            {
                SelectedProcess = null;
            }
        }

        RunOnDispatcher(UpdateAction);
    }

    private void SetPropertyOnDispatcher(Action action) => RunOnDispatcher(action);

    private void RunOnDispatcher(Action action)
    {
        if (_uiDispatcher is not null)
        {
            _uiDispatcher(action);
        }
        else
        {
            action();
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilterAndSort();

    partial void OnFilterModeChanged(ProcessFilterMode value)
    {
        OnPropertyChanged(nameof(FilterModeIndex));
        ApplyFilterAndSort();
    }

    partial void OnSortColumnChanged(ProcessSortColumn value) => ApplyFilterAndSort();

    partial void OnSortAscendingChanged(bool value) => ApplyFilterAndSort();

    partial void OnSelectedProcessChanged(ProcessItemPresentationModel? value)
    {
        if (value is not null && string.IsNullOrEmpty(value.ExecutablePath))
        {
            _ = ResolveSelectedPathAsync(value);
        }
    }

    private async Task ResolveSelectedPathAsync(ProcessItemPresentationModel target)
    {
        try
        {
            var ct = _pollingCts?.Token ?? CancellationToken.None;
            var path = await _processService.ResolveExecutablePathAsync(target.Key, ct);
            if (!string.IsNullOrEmpty(path) && SelectedProcess?.Key.Equals(target.Key) == true)
            {
                SetPropertyOnDispatcher(() => target.ExecutablePath = path);
            }
        }
        catch
        {
            // Best effort
        }
    }

    private void ApplyFilterAndSort()
    {
        var query = _allProcesses.AsEnumerable();

        // 1. Filter by Ownership
        if (FilterMode == ProcessFilterMode.DevDeskManaged)
        {
            query = query.Where(p => p.IsManaged);
        }
        else if (FilterMode == ProcessFilterMode.External)
        {
            query = query.Where(p => !p.IsManaged);
        }

        // 2. Filter by Search text
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(p =>
                p.ProcessName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.ProcessId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (p.ManagedProjectName != null && p.ManagedProjectName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.ExecutablePath != null && p.ExecutablePath.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        // 3. Sorting
        query = SortColumn switch
        {
            ProcessSortColumn.Cpu => SortAscending
                ? query.OrderBy(p => p.CpuPercent ?? -1).ThenBy(p => p.ProcessName)
                : query.OrderByDescending(p => p.CpuPercent ?? -1).ThenBy(p => p.ProcessName),

            ProcessSortColumn.Memory => SortAscending
                ? query.OrderBy(p => p.WorkingSetBytes ?? -1).ThenBy(p => p.ProcessName)
                : query.OrderByDescending(p => p.WorkingSetBytes ?? -1).ThenBy(p => p.ProcessName),

            ProcessSortColumn.Pid => SortAscending
                ? query.OrderBy(p => p.ProcessId)
                : query.OrderByDescending(p => p.ProcessId),

            ProcessSortColumn.Name => SortAscending
                ? query.OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                : query.OrderByDescending(p => p.ProcessName, StringComparer.OrdinalIgnoreCase),

            _ => query
        };

        var filteredList = query.ToList();
        FilteredProcesses.Clear();
        foreach (var item in filteredList)
        {
            FilteredProcesses.Add(item);
        }
    }

    [RelayCommand]
    private void SetSort(string column)
    {
        if (Enum.TryParse<ProcessSortColumn>(column, true, out var col))
        {
            if (SortColumn == col)
            {
                SortAscending = !SortAscending;
            }
            else
            {
                SortColumn = col;
                SortAscending = (col == ProcessSortColumn.Name || col == ProcessSortColumn.Pid);
            }
        }
    }

    [RelayCommand]
    private void CopyPid()
    {
        if (SelectedProcess != null)
        {
            try
            {
                Clipboard.SetText(SelectedProcess.ProcessId.ToString());
            }
            catch
            {
                // Clipboard can be locked by other apps
            }
        }
    }

    [RelayCommand]
    private async Task OpenLocationAsync()
    {
        if (SelectedProcess?.ExecutablePath is { } path && File.Exists(path))
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    await _launcherService.OpenInExplorerAsync(dir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to open location for process executable: {Path}", path);
            }
        }
    }
}
