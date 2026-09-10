using System.Windows;
using DevDesk.App.ViewModels.Projects;
using DevDesk.App.Views.Projects;

namespace DevDesk.App.Services.Dialogs;

/// <summary>
/// Implements IDialogService for presenting modal windows and native pickers in DevDesk.
/// </summary>
public sealed class DialogService : IDialogService
{
    public string? ShowFolderPicker(string? initialDirectory = null, string? title = null)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title ?? "Select Project Folder",
            Multiselect = false,
            InitialDirectory = !string.IsNullOrWhiteSpace(initialDirectory) && System.IO.Directory.Exists(initialDirectory)
                ? initialDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName)
            ? dialog.FolderName
            : null;
    }

    public bool ShowAddEditProjectDialog(AddEditProjectViewModel viewModel)
    {
        var dialog = new AddEditProjectDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    public bool ShowConfirmDeleteDialog(ConfirmDeleteViewModel viewModel)
    {
        var dialog = new ConfirmDeleteDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    public void ShowMessage(string title, string message)
    {
        MessageBox.Show(
            Application.Current?.MainWindow ?? null!,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
