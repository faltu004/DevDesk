using System.Windows.Controls;
using System.Windows.Input;

namespace DevDesk.App.Views.Processes;

/// <summary>
/// Interaction logic for ProcessesView.xaml.
/// Keeps presentation logic strictly inside the ViewModel.
/// </summary>
public partial class ProcessesView : UserControl
{
    public ProcessesView()
    {
        InitializeComponent();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ProcessesSearchTextBox.Focus();
            ProcessesSearchTextBox.SelectAll();
            e.Handled = true;
        }
    }
}

