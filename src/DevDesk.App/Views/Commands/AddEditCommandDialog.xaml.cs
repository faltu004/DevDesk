using System.Windows;
using DevDesk.App.ViewModels.Commands;

namespace DevDesk.App.Views.Commands;

public partial class AddEditCommandDialog : Window
{
    public AddEditCommandDialog(AddEditCommandViewModel viewModel)
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
