using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevDesk.App.Services.Navigation;
using DevDesk.App.ViewModels.Common;

namespace DevDesk.App.ViewModels.Shell;

/// <summary>
/// Root view model coordinating application shell presentation, navigation, and top-level state.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private string _appTitle = "DevDesk — Windows Developer Control Center";

    [ObservableProperty]
    private string _systemStatus = "System Ready";

    [ObservableProperty]
    private string _systemVersion = "Windows 11 Developer Environment";

    public ShellViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.CurrentViewModelChanged += OnNavigationChanged;

        // Ensure default initial navigation to Dashboard
        if (_navigationService.CurrentViewModel is null)
        {
            _navigationService.NavigateTo(NavigationItem.Dashboard);
        }
    }

    public object? CurrentViewModel => _navigationService.CurrentViewModel;

    public NavigationItem CurrentItem => _navigationService.CurrentItem;

    public bool IsDashboardSelected => CurrentItem == NavigationItem.Dashboard;

    public bool IsProjectsSelected => CurrentItem == NavigationItem.Projects;

    public bool IsProcessesSelected => CurrentItem == NavigationItem.Processes;

    public bool IsPortsSelected => CurrentItem == NavigationItem.Ports;

    public bool IsCommandsSelected => CurrentItem == NavigationItem.Commands;

    [RelayCommand]
    private void Navigate(NavigationItem destination)
    {
        if (CurrentItem == destination)
        {
            return;
        }

        _navigationService.NavigateTo(destination);
    }

    [RelayCommand]
    private void NavigateToDashboard()
    {
        Navigate(NavigationItem.Dashboard);
    }

    [RelayCommand]
    private void NavigateToProjects()
    {
        Navigate(NavigationItem.Projects);
    }

    [RelayCommand]
    private void NavigateToProcesses()
    {
        Navigate(NavigationItem.Processes);
    }

    [RelayCommand]
    private void NavigateToPorts()
    {
        Navigate(NavigationItem.Ports);
    }

    [RelayCommand]
    private void NavigateToCommands()
    {
        Navigate(NavigationItem.Commands);
    }

    private void OnNavigationChanged()
    {
        OnPropertyChanged(nameof(CurrentViewModel));
        OnPropertyChanged(nameof(CurrentItem));
        OnPropertyChanged(nameof(IsDashboardSelected));
        OnPropertyChanged(nameof(IsProjectsSelected));
        OnPropertyChanged(nameof(IsProcessesSelected));
        OnPropertyChanged(nameof(IsPortsSelected));
        OnPropertyChanged(nameof(IsCommandsSelected));
    }
}
