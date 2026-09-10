using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DevDesk.App.ViewModels.Shell;

namespace DevDesk.App.Views.Shell;

/// <summary>
/// Interaction logic for MainWindow.xaml shell window.
/// </summary>
public partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
