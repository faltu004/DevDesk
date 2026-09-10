using System.Windows;
using DevDesk.App.ViewModels.Shell;

namespace DevDesk.App.Views.Shell;

/// <summary>
/// Interaction logic for MainWindow.xaml shell window.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
