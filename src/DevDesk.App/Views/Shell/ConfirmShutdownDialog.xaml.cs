using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DevDesk.App.ViewModels.Shell;

namespace DevDesk.App.Views.Shell;

/// <summary>
/// Interaction logic for ConfirmShutdownDialog.xaml.
/// </summary>
public partial class ConfirmShutdownDialog : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;

    public ConfirmShutdownDialog(ConfirmShutdownViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += result =>
        {
            DialogResult = result;
            Close();
        };
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

        int useDarkMode = 1;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

        int captionColor = 0x001B110B;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
