namespace DevDesk.App.ViewModels.Dashboard;

/// <summary>
/// Presentation model for a Developer Environment summary metric card.
/// </summary>
public sealed class DashboardEnvSummaryItem
{
    public string Title { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Subtext { get; set; } = string.Empty;
    public string IconKind { get; set; } = string.Empty;
    public string IconBrushKey { get; set; } = "Brush.Accent.Light";
}
