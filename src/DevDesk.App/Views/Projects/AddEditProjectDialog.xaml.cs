using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DevDesk.App.ViewModels.Projects;

namespace DevDesk.App.Views.Projects;

/// <summary>
/// Interaction logic for AddEditProjectDialog.xaml
/// </summary>
public partial class AddEditProjectDialog : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public AddEditProjectDialog(AddEditProjectViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += OnRequestClose;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDarkThemeTitleBar();
    }

    private void OnRequestClose(bool result)
    {
        DialogResult = result;
        Close();
    }

    private void ApplyDarkThemeTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int darkMode = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
        }
        catch
        {
            // Fall back gracefully if DWM call is unavailable
        }
    }
}
