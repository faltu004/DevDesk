using System.Windows.Controls;
using System.Windows.Input;

namespace DevDesk.App.Views.Ports;

/// <summary>
/// Interaction logic for PortsView.xaml
/// </summary>
public partial class PortsView : UserControl
{
    public PortsView()
    {
        InitializeComponent();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            PortsSearchTextBox.Focus();
            PortsSearchTextBox.SelectAll();
            e.Handled = true;
        }
    }
}
