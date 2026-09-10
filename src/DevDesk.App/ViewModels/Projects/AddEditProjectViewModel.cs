using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevDesk.App.ViewModels.Common;
using DevDesk.Core.Common;
using DevDesk.Core.Models;

namespace DevDesk.App.ViewModels.Projects;

/// <summary>
/// ViewModel coordinating input and validation for adding or editing a developer project.
/// </summary>
public sealed partial class AddEditProjectViewModel : ViewModelBase
{
    private readonly Guid _projectId;
    private readonly bool _isEditMode;

    [ObservableProperty]
    private string _dialogTitle = "Add Project";

    [ObservableProperty]
    private string _projectPath = string.Empty;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _runCommand = string.Empty;

    [ObservableProperty]
    private string _buildCommand = string.Empty;

    [ObservableProperty]
    private string _testCommand = string.Empty;

    [ObservableProperty]
    private string _portText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsEditMode => _isEditMode;

    public event Action<bool>? RequestClose;

    public AddEditProjectViewModel(string initialPath)
    {
        _isEditMode = false;
        _projectId = Guid.NewGuid();
        _dialogTitle = "Add Project";

        _projectPath = PathHelper.NormalizePath(initialPath);
        _projectName = !string.IsNullOrWhiteSpace(_projectPath)
            ? System.IO.Path.GetFileName(_projectPath)
            : string.Empty;
    }

    public AddEditProjectViewModel(DeveloperProject existingProject)
    {
        ArgumentNullException.ThrowIfNull(existingProject);

        _isEditMode = true;
        _projectId = existingProject.Id;
        _dialogTitle = "Edit Project";

        _projectPath = existingProject.Path;
        _projectName = existingProject.Name;
        _runCommand = existingProject.RunCommand ?? string.Empty;
        _buildCommand = existingProject.BuildCommand ?? string.Empty;
        _testCommand = existingProject.TestCommand ?? string.Empty;
        _portText = existingProject.DefaultPort?.ToString() ?? string.Empty;
    }

    [RelayCommand]
    private void BrowsePath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Project Folder",
            Multiselect = false,
            InitialDirectory = !string.IsNullOrWhiteSpace(ProjectPath) && Directory.Exists(ProjectPath)
                ? ProjectPath
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            ProjectPath = PathHelper.NormalizePath(dialog.FolderName);
            if (string.IsNullOrWhiteSpace(ProjectName) || !_isEditMode)
            {
                ProjectName = System.IO.Path.GetFileName(ProjectPath);
            }
        }
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            ErrorMessage = "Project path is required.";
            return;
        }

        var normalizedPath = PathHelper.NormalizePath(ProjectPath);
        if (!Directory.Exists(normalizedPath))
        {
            ErrorMessage = "The selected project folder could not be found.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            ErrorMessage = "Project name is required.";
            return;
        }

        int? port = null;
        if (!string.IsNullOrWhiteSpace(PortText))
        {
            if (!int.TryParse(PortText.Trim(), out var parsedPort) || parsedPort is < 1 or > 65535)
            {
                ErrorMessage = "Default port must be between 1 and 65535.";
                return;
            }
            port = parsedPort;
        }

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }

    public DeveloperProject ToProject()
    {
        int? port = null;
        if (!string.IsNullOrWhiteSpace(PortText) && int.TryParse(PortText.Trim(), out var parsedPort))
        {
            port = parsedPort;
        }

        return new DeveloperProject
        {
            Id = _projectId,
            Name = ProjectName.Trim(),
            Path = PathHelper.NormalizePath(ProjectPath),
            RunCommand = string.IsNullOrWhiteSpace(RunCommand) ? null : RunCommand.Trim(),
            BuildCommand = string.IsNullOrWhiteSpace(BuildCommand) ? null : BuildCommand.Trim(),
            TestCommand = string.IsNullOrWhiteSpace(TestCommand) ? null : TestCommand.Trim(),
            DefaultPort = port
        };
    }
}
