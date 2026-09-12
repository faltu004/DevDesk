using CommunityToolkit.Mvvm.ComponentModel;

namespace DevDesk.App.ViewModels.Dashboard;

/// <summary>
/// Presentation model for an active project displayed on the dashboard.
/// </summary>
public sealed partial class DashboardProjectItem : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _frameworkBadge = string.Empty;

    [ObservableProperty]
    private string _categoryBadge = string.Empty;

    [ObservableProperty]
    private string _statusText = "Stopped";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _portText = "Port —";

    [ObservableProperty]
    private bool _hasActivePort;

    [ObservableProperty]
    private string _iconKind = "Generic";
}
