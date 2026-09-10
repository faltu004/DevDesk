namespace DevDesk.App.ViewModels.Dashboard;

/// <summary>
/// Presentation model for a quick action item tile on the dashboard.
/// </summary>
public sealed class DashboardQuickActionItem
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string IconKind { get; init; } = string.Empty;
}
