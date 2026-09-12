using CommunityToolkit.Mvvm.ComponentModel;

namespace DevDesk.App.ViewModels.Settings;

/// <summary>
/// Presentation item for category selection within the Settings view.
/// </summary>
public sealed partial class SettingsCategoryItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Name => Title;

    public string Subtitle { get; init; } = string.Empty;

    public string IconKind { get; init; } = string.Empty;

    public string IconData { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
