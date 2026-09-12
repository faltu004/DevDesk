using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DevDesk.App.Services.Dialogs;
using DevDesk.App.ViewModels.Shell;
using DevDesk.Core.Commands;
using DevDesk.Core.Runner;

namespace DevDesk.App.Views.Shell;

/// <summary>
/// Interaction logic for MainWindow.xaml shell window.
/// Enforces unified application shutdown policy confirming active project runs and running saved commands.
/// </summary>
public partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;

    private readonly IProjectRunnerService _runnerService;
    private readonly ISavedCommandExecutor? _commandExecutor;
    private readonly IDialogService _dialogService;
    private bool _isShutdownConfirmed;

    public MainWindow(
        ShellViewModel viewModel,
        IProjectRunnerService runnerService,
        IDialogService dialogService,
        ISavedCommandExecutor? commandExecutor = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        _runnerService = runnerService;
        _dialogService = dialogService;
        _commandExecutor = commandExecutor;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_isShutdownConfirmed)
        {
            base.OnClosing(e);
            return;
        }

        // Do not unnecessarily block Windows system shutdown/logoff
        if (App.IsSystemSessionEnding)
        {
            base.OnClosing(e);
            return;
        }

        var activeProjects = _runnerService.GetActiveSessions();
        var activeCommands = _commandExecutor?.GetActiveSessions() ?? Array.Empty<SavedCommandRunSession>();
        if (activeProjects.Count == 0 && activeCommands.Count == 0)
        {
            base.OnClosing(e);
            return;
        }

        // Cancel initial close to prompt user confirmation
        e.Cancel = true;

        bool confirmed = _dialogService.ShowConfirmShutdownDialog(activeProjects.Count, activeCommands.Count);
        if (confirmed)
        {
            _isShutdownConfirmed = true;
            try
            {
                var stopTasks = new List<Task> { _runnerService.StopAllAsync() };
                if (_commandExecutor is not null)
                {
                    stopTasks.Add(_commandExecutor.StopAllAsync());
                }
                await Task.WhenAll(stopTasks);
            }
            catch
            {
                // Ensure close proceeds even on partial cleanup failure
            }

            Close();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDarkTitleBarTheme();
    }

    private void ApplyDarkTitleBarTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // Enable Windows 10/11 Immersive Dark Mode for standard title bar
        int useDarkMode = 1;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

        // Set caption color to match DevDesk root background (#0B111B = 0x001B110B in COLORREF format)
        int captionColor = 0x001B110B;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
