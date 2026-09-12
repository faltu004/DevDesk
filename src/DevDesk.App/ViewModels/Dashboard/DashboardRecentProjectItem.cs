using CommunityToolkit.Mvvm.ComponentModel;

namespace DevDesk.App.ViewModels.Dashboard;

/// <summary>
/// Presentation model for a recent project item.
/// </summary>
public sealed partial class DashboardRecentProjectItem : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _lastOpenedText = string.Empty;

    [ObservableProperty]
    private string _statusColorBrushKey = "Brush.Text.Muted";

    [ObservableProperty]
    private string _iconKind = "Folder";
}
