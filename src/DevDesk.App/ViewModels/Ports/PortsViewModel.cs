using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using DevDesk.App.ViewModels.Common;
using DevDesk.Core.Ports;

namespace DevDesk.App.ViewModels.Ports;

/// <summary>
/// Root ViewModel for the Ports view coordinating TCP listening port inspection,
/// client-side filtering, and potential project port conflict visualization.
/// </summary>
public sealed partial class PortsViewModel : ViewModelBase, IAsyncInitializable
{
    private readonly IPortService _portService;
    private readonly ILogger<PortsViewModel> _logger;

    [ObservableProperty]
    private string _title = "Ports";

    [ObservableProperty]
    private string _subtitle = "Active listening ports, services, and conflicts.";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _searchPlaceholder = "Search ports, processes, or projects...";

    [ObservableProperty]
    private string _searchShortcut = "Ctrl + K";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _warningMessage;

    [ObservableProperty]
    private int _totalListeningPortsCount;

    [ObservableProperty]
    private int _potentialConflictsCount;

    [ObservableProperty]
    private int _configuredProjectPortsCount;

    [ObservableProperty]
    private int _freeConfiguredPortsCount;

    [ObservableProperty]
    private PortConflictPresentationModel? _selectedConflict;

    public ObservableCollection<PortItemPresentationModel> ListeningPorts { get; } = new();

    public ObservableCollection<PortItemPresentationModel> FilteredPorts { get; } = new();

    public ObservableCollection<PortConflictPresentationModel> PotentialConflicts { get; } = new();

    public bool HasPorts => ListeningPorts.Count > 0;

    public bool HasFilteredPorts => FilteredPorts.Count > 0;

    public bool HasPotentialConflicts => PotentialConflicts.Count > 0;

    public bool HasSelectedConflict => SelectedConflict is not null;

    private readonly object _loadLock = new();
    private CancellationTokenSource? _activeLoadCts;

    public PortsViewModel(
        IPortService portService,
        ILogger<PortsViewModel> logger)
    {
        _portService = portService;
        _logger = logger;
    }

    /// <summary>
    /// Lifecycle activation called when navigating to the Ports view.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadPortsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedConflictChanged(PortConflictPresentationModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedConflict));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await LoadPortsAsync();
    }

    [RelayCommand]
    public void DismissNotification()
    {
        ErrorMessage = null;
        WarningMessage = null;
    }

    [RelayCommand]
    public void SelectConflict(PortConflictPresentationModel? conflict)
    {
        SelectedConflict = conflict;
    }

    public async Task LoadPortsAsync()
    {
        CancellationTokenSource cts;
        CancellationTokenSource? oldCts = null;

        lock (_loadLock)
        {
            if (_activeLoadCts is not null)
            {
                oldCts = _activeLoadCts;
            }
            _activeLoadCts = new CancellationTokenSource();
            cts = _activeLoadCts;
        }

        if (oldCts is not null)
        {
            try
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }
            catch
            {
                // Suppress cancellation disposal exceptions
            }
        }

        var token = cts.Token;

        try
        {
            IsBusy = true;
            IsLoading = true;
            ErrorMessage = null;
            WarningMessage = null;

            var snapshot = await _portService.GetPortSnapshotAsync(token);
            token.ThrowIfCancellationRequested();

            if (!snapshot.Success)
            {
                ErrorMessage = snapshot.ErrorMessage ?? "Failed to inspect network ports.";
                _logger.LogWarning("Port snapshot query failed: {Error}", snapshot.ErrorMessage);

                // Total failure: Clear stale snapshot data so failed inspection is not represented as fresh data
                ListeningPorts.Clear();
                FilteredPorts.Clear();
                PotentialConflicts.Clear();
                SelectedConflict = null;
                TotalListeningPortsCount = 0;
                PotentialConflictsCount = 0;
                ConfiguredProjectPortsCount = snapshot.ConfiguredProjectPortsCount;
                FreeConfiguredPortsCount = 0;

                OnPropertyChanged(nameof(HasPorts));
                OnPropertyChanged(nameof(HasFilteredPorts));
                OnPropertyChanged(nameof(HasPotentialConflicts));
                return;
            }

            if (snapshot.HasPartialFailure)
            {
                WarningMessage = snapshot.WarningMessage;
            }

            // Populate summary metrics
            TotalListeningPortsCount = snapshot.TotalListeningPortsCount;
            PotentialConflictsCount = snapshot.PotentialConflicts.Count;
            ConfiguredProjectPortsCount = snapshot.ConfiguredProjectPortsCount;
            FreeConfiguredPortsCount = snapshot.FreeConfiguredPortsCount;

            // Populate table rows
            ListeningPorts.Clear();
            foreach (var entry in snapshot.ListeningPorts.OrderBy(p => p.Port).ThenBy(p => p.LocalAddress))
            {
                ListeningPorts.Add(new PortItemPresentationModel(entry));
            }

            // Populate conflicts
            PotentialConflicts.Clear();
            foreach (var conflict in snapshot.PotentialConflicts.OrderBy(c => c.Port))
            {
                PotentialConflicts.Add(new PortConflictPresentationModel(conflict));
            }

            SelectedConflict = PotentialConflicts.FirstOrDefault();

            ApplyFilter();

            OnPropertyChanged(nameof(HasPorts));
            OnPropertyChanged(nameof(HasPotentialConflicts));
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Port refresh was cancelled or superseded.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error refreshing Ports screen");
            ErrorMessage = "Failed to inspect network ports. Please verify system permissions.";
        }
        finally
        {
            lock (_loadLock)
            {
                if (_activeLoadCts == cts)
                {
                    IsLoading = false;
                    IsBusy = false;
                }
            }
        }
    }

    public void ApplyFilter()
    {
        FilteredPorts.Clear();

        var query = SearchText?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            foreach (var item in ListeningPorts)
            {
                FilteredPorts.Add(item);
            }
        }
        else
        {
            foreach (var item in ListeningPorts)
            {
                if (MatchesFilter(item, query))
                {
                    FilteredPorts.Add(item);
                }
            }
        }

        OnPropertyChanged(nameof(HasFilteredPorts));
    }

    private static bool MatchesFilter(PortItemPresentationModel item, string query)
    {
        if (item.PortDisplay.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (item.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (item.ProcessIdDisplay.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (item.LocalAddress.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (item.HasConfiguredProjects &&
            item.ProjectDisplay.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
