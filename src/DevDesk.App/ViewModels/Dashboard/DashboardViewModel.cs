using CommunityToolkit.Mvvm.ComponentModel;
using DevDesk.App.ViewModels.Common;

namespace DevDesk.App.ViewModels.Dashboard;

/// <summary>
/// View model representing the main dashboard view.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Dashboard";

    [ObservableProperty]
    private string _subtitle = "Your development environment at a glance.";

    [ObservableProperty]
    private string _statusMessage = "System Ready";
}
