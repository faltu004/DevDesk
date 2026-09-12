using System.Windows;
using DevDesk.App.ViewModels.Commands;

namespace DevDesk.App.Views.Commands;

public partial class ConfirmDeleteCommandDialog : Window
{
    public ConfirmDeleteCommandDialog(ConfirmDeleteCommandViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += result =>
        {
            DialogResult = result;
            Close();
        };
    }
}
