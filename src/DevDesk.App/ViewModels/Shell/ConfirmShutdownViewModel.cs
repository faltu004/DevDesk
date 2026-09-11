using CommunityToolkit.Mvvm.Input;
using DevDesk.App.ViewModels.Common;

namespace DevDesk.App.ViewModels.Shell;

/// <summary>
/// ViewModel for coordinating application exit confirmation when active project sessions are running.
/// </summary>
public sealed partial class ConfirmShutdownViewModel : ViewModelBase
{
    public int ActiveProjectCount { get; }

    public string Message => $"DevDesk started {ActiveProjectCount} project{(ActiveProjectCount == 1 ? string.Empty : "s")}. They must be stopped before DevDesk exits.";

    public IRelayCommand ConfirmCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public event Action<bool>? RequestClose;

    public ConfirmShutdownViewModel(int activeProjectCount)
    {
        ActiveProjectCount = activeProjectCount;
        ConfirmCommand = new RelayCommand(() => RequestClose?.Invoke(true));
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }
}
