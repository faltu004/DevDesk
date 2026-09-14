using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace DevDesk.App.Views.Dashboard;

/// <summary>
/// Interaction logic for DashboardView.xaml
/// </summary>
public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void OnOverflowButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == System.Windows.Input.Key.K && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            DashboardSearchTextBox?.Focus();
            DashboardSearchTextBox?.SelectAll();
            e.Handled = true;
        }
    }
}
