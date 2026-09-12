using DevDesk.App.ViewModels.Commands;
using DevDesk.App.ViewModels.Projects;

namespace DevDesk.App.Services.Dialogs;

/// <summary>
/// Service contract for presenting application modal dialogs and picker windows.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Displays a folder picker dialog to select a local directory.
    /// </summary>
    string? ShowFolderPicker(string? initialDirectory = null, string? title = null);

    /// <summary>
    /// Displays the Add / Edit Project dialog window.
    /// </summary>
    bool ShowAddEditProjectDialog(AddEditProjectViewModel viewModel);

    /// <summary>
    /// Displays the project removal confirmation safety dialog.
    /// </summary>
    bool ShowConfirmDeleteDialog(ConfirmDeleteViewModel viewModel);

    /// <summary>
    /// Displays the Add / Edit Saved Command dialog window.
    /// </summary>
    bool ShowAddEditCommandDialog(AddEditCommandViewModel viewModel);

    /// <summary>
    /// Displays the saved command removal confirmation safety dialog.
    /// </summary>
    bool ShowConfirmDeleteCommandDialog(ConfirmDeleteCommandViewModel viewModel);

    /// <summary>
    /// Displays the application exit confirmation dialog when projects or saved commands are running.
    /// </summary>
    bool ShowConfirmShutdownDialog(int activeProjectCount, int activeCommandCount = 0);

    /// <summary>
    /// Displays an error or notification message to the user.
    /// </summary>
    void ShowMessage(string title, string message);
}
