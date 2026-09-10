namespace DevDesk.App.ViewModels.Dashboard;

/// <summary>
/// Presentation model for an active project displayed on the dashboard.
/// </summary>
public sealed class DashboardProjectItem
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string FrameworkBadge { get; init; } = string.Empty;
    public string CategoryBadge { get; init; } = string.Empty;
    public string StatusText { get; init; } = "Stopped";
    public bool IsRunning { get; init; }
    public string PortText { get; init; } = "Port —";
    public bool HasActivePort { get; init; }
    public string IconKind { get; init; } = "Generic";
}
