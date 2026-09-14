using System.Windows.Controls;
using System.Windows.Input;

namespace DevDesk.App.Views.Commands;

public partial class CommandsView : UserControl
{
    public CommandsView()
    {
        InitializeComponent();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            CommandsSearchTextBox.Focus();
            CommandsSearchTextBox.SelectAll();
            e.Handled = true;
        }
    }
}

