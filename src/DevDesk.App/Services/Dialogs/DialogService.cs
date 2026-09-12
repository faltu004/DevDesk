using System.Windows;
using DevDesk.App.ViewModels.Commands;
using DevDesk.App.ViewModels.Projects;
using DevDesk.App.Views.Commands;
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

    public bool ShowAddEditCommandDialog(AddEditCommandViewModel viewModel)
    {
        var dialog = new AddEditCommandDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    public bool ShowConfirmDeleteCommandDialog(ConfirmDeleteCommandViewModel viewModel)
    {
        var dialog = new ConfirmDeleteCommandDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    public bool ShowConfirmShutdownDialog(int activeProjectCount, int activeCommandCount = 0)
    {
        var vm = new DevDesk.App.ViewModels.Shell.ConfirmShutdownViewModel(activeProjectCount, activeCommandCount);
        var dialog = new DevDesk.App.Views.Shell.ConfirmShutdownDialog(vm)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    public string? ShowFilePicker(string? filter = null, string? title = null)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title ?? "Select Executable",
            Filter = filter ?? "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName)
            ? dialog.FileName
            : null;
    }

    public bool ShowConfirmationDialog(string title, string message, string confirmButtonText = "Confirm")
    {
        var result = MessageBox.Show(
            Application.Current?.MainWindow ?? null!,
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
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
