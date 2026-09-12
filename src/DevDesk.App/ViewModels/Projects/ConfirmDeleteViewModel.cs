using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevDesk.App.ViewModels.Common;

namespace DevDesk.App.ViewModels.Projects;

/// <summary>
/// ViewModel coordinating the project removal confirmation safety dialog.
/// Truthfully alerts the user if project-associated saved commands will also be deleted.
/// </summary>
public sealed partial class ConfirmDeleteViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _projectName;

    [ObservableProperty]
    private string _projectPath;

    [ObservableProperty]
    private int _associatedCommandsCount;

    public bool HasAssociatedCommands => AssociatedCommandsCount > 0;

    public string AssociatedCommandsWarning =>
        $"Removing this project will also delete {AssociatedCommandsCount} associated saved command{(AssociatedCommandsCount == 1 ? string.Empty : "s")}.";

    public event Action<bool>? RequestClose;

    public ConfirmDeleteViewModel(string projectName, string projectPath, int associatedCommandsCount = 0)
    {
        _projectName = projectName;
        _projectPath = projectPath;
        _associatedCommandsCount = associatedCommandsCount;
    }

    [RelayCommand]
    private void Confirm()
    {
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}
