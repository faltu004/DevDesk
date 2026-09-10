namespace DevDesk.App.ViewModels.Dashboard;

/// <summary>
/// Presentation model for a system metric card on the dashboard.
/// </summary>
public sealed class DashboardMetricItem
{
    public string Title { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Subtext { get; init; } = string.Empty;
    public string IconType { get; init; } = string.Empty;
    public double ProgressValue { get; init; }
    public string VisualType { get; init; } = string.Empty; // "Sparkline", "Progress", "Histogram"
    public string AccentColorBrushKey { get; init; } = "Brush.Accent.Light";
}
