using System.Windows.Controls;
using System.Windows.Input;

namespace DevDesk.App.Views.Projects;

/// <summary>
/// Interaction logic for ProjectsView.xaml
/// </summary>
public partial class ProjectsView : UserControl
{
    public ProjectsView()
    {
        InitializeComponent();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ProjectSearchTextBox.Focus();
            ProjectSearchTextBox.SelectAll();
            e.Handled = true;
        }
    }
}
