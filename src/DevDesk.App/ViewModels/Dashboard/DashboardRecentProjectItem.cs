namespace DevDesk.App.ViewModels.Dashboard;

/// <summary>
/// Presentation model for a recent project item.
/// </summary>
public sealed class DashboardRecentProjectItem
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string LastOpenedText { get; init; } = string.Empty;
    public string StatusColorBrushKey { get; init; } = "Brush.Text.Muted";
    public string IconKind { get; init; } = "Folder";
}
