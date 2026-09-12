using CommunityToolkit.Mvvm.Input;
using DevDesk.App.ViewModels.Common;

namespace DevDesk.App.ViewModels.Shell;

/// <summary>
/// ViewModel for coordinating application exit confirmation when active project sessions or saved commands are running.
/// </summary>
public sealed partial class ConfirmShutdownViewModel : ViewModelBase
{
    public int ActiveProjectCount { get; }
    public int ActiveCommandCount { get; }

    public int TotalActiveCount => ActiveProjectCount + ActiveCommandCount;

    public string Title => "Active Processes Running - DevDesk";

    public string Subtitle => "DevDesk is managing active execution sessions.";

    public string Message
    {
        get
        {
            if (ActiveProjectCount > 0 && ActiveCommandCount > 0)
            {
                return $"DevDesk started {TotalActiveCount} active processes:\n" +
                       $"  • {ActiveProjectCount} project{(ActiveProjectCount == 1 ? string.Empty : "s")}\n" +
                       $"  • {ActiveCommandCount} saved command{(ActiveCommandCount == 1 ? string.Empty : "s")}\n\n" +
                       "They must be stopped before DevDesk exits.";
            }

            if (ActiveCommandCount > 0)
            {
                return $"DevDesk started {ActiveCommandCount} saved command{(ActiveCommandCount == 1 ? string.Empty : "s")}. " +
                       "They must be stopped before DevDesk exits.";
            }

            return $"DevDesk started {ActiveProjectCount} project{(ActiveProjectCount == 1 ? string.Empty : "s")}. " +
                   "They must be stopped before DevDesk exits.";
        }
    }

    public IRelayCommand ConfirmCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public event Action<bool>? RequestClose;

    public ConfirmShutdownViewModel(int activeProjectCount, int activeCommandCount = 0)
    {
        ActiveProjectCount = Math.Max(0, activeProjectCount);
        ActiveCommandCount = Math.Max(0, activeCommandCount);
        ConfirmCommand = new RelayCommand(() => RequestClose?.Invoke(true));
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
    }
}
