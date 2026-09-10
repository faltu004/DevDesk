using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevDesk.App.ViewModels.Common;

namespace DevDesk.App.ViewModels.Projects;

/// <summary>
/// ViewModel coordinating the project removal confirmation safety dialog.
/// </summary>
public sealed partial class ConfirmDeleteViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _projectName;

    [ObservableProperty]
    private string _projectPath;

    public event Action<bool>? RequestClose;

    public ConfirmDeleteViewModel(string projectName, string projectPath)
    {
        _projectName = projectName;
        _projectPath = projectPath;
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
