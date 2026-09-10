namespace DevDesk.App.ViewModels.Dashboard;

/// <summary>
/// Presentation model for a Developer Environment summary metric card.
/// </summary>
public sealed class DashboardEnvSummaryItem
{
    public string Title { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Subtext { get; init; } = string.Empty;
    public string IconKind { get; init; } = string.Empty;
    public string IconBrushKey { get; init; } = "Brush.Accent.Light";
}
