using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevDesk.App.ViewModels.Common;

namespace DevDesk.App.ViewModels.Commands;

/// <summary>
/// ViewModel coordinating the saved command deletion safety dialog.
/// </summary>
public sealed partial class ConfirmDeleteCommandViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _commandName;

    [ObservableProperty]
    private string _executableAndArgs;

    public event Action<bool>? RequestClose;

    public ConfirmDeleteCommandViewModel(string commandName, string executableAndArgs)
    {
        _commandName = commandName;
        _executableAndArgs = executableAndArgs;
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
