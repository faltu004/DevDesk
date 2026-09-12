using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevDesk.App.ViewModels.Common;
using DevDesk.Core.Models;
using DevDesk.Infrastructure.Commands;
using DevDesk.Infrastructure.Runner.Native;

namespace DevDesk.App.ViewModels.Commands;

public sealed record ProjectSelectionItem(Guid Id, string Name, string Path)
{
    public override string ToString() => Name;
}

/// <summary>
/// ViewModel coordinating input, validation, and real-time structured preview for adding or editing a saved command.
/// </summary>
public sealed partial class AddEditCommandViewModel : ViewModelBase
{
    private readonly Guid _commandId;
    private readonly bool _isEditMode;

    [ObservableProperty]
    private string _dialogTitle = "Add Command";

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGlobalScope))]
    [NotifyPropertyChangedFor(nameof(ResolvedWorkingDirectoryPreview))]
    private bool _isProjectScope = true;

    public bool IsGlobalScope
    {
        get => !IsProjectScope;
        set => IsProjectScope = !value;
    }

    [ObservableProperty]
    private ProjectSelectionItem? _selectedProject;

    [ObservableProperty]
    private string _executable = string.Empty;

    [ObservableProperty]
    private string _argumentsText = string.Empty;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsEditMode => _isEditMode;
    public Guid CommandId => _commandId;

    public ObservableCollection<ProjectSelectionItem> AvailableProjects { get; } = new();
    public ObservableCollection<string> ParsedArgumentsTokens { get; } = new();

    public string ResolvedWorkingDirectoryPreview
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(WorkingDirectory))
            {
                return WorkingDirectory.Trim();
            }

            if (IsProjectScope && SelectedProject is not null && !string.IsNullOrWhiteSpace(SelectedProject.Path))
            {
                return SelectedProject.Path;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    public event Action<bool>? RequestClose;

    public AddEditCommandViewModel(IEnumerable<DeveloperProject> projects, Guid? preselectedProjectId = null)
    {
        _isEditMode = false;
        _commandId = Guid.NewGuid();
        _dialogTitle = "Add Command";

        PopulateProjects(projects, preselectedProjectId);
        UpdateParsedArguments();
    }

    public AddEditCommandViewModel(SavedCommand existingCommand, IEnumerable<DeveloperProject> projects)
    {
        ArgumentNullException.ThrowIfNull(existingCommand);

        _isEditMode = true;
        _commandId = existingCommand.Id;
        _dialogTitle = "Edit Command";

        _name = existingCommand.Name;
        _description = existingCommand.Description ?? string.Empty;
        _isProjectScope = existingCommand.ProjectId.HasValue;
        _executable = existingCommand.Executable;
        _argumentsText = existingCommand.Arguments.Count > 0
            ? WindowsCommandLineSerializer.FormatCommandLine(string.Empty, existingCommand.Arguments).Trim()
            : string.Empty;
        _workingDirectory = existingCommand.WorkingDirectory ?? string.Empty;
        _category = existingCommand.Category ?? string.Empty;
        _isEnabled = existingCommand.IsEnabled;

        PopulateProjects(projects, existingCommand.ProjectId);
        UpdateParsedArguments();
    }

    private void PopulateProjects(IEnumerable<DeveloperProject> projects, Guid? targetProjectId)
    {
        AvailableProjects.Clear();
        foreach (var p in projects)
        {
            var item = new ProjectSelectionItem(p.Id, p.Name, p.Path);
            AvailableProjects.Add(item);
            if (targetProjectId.HasValue && p.Id == targetProjectId.Value)
            {
                SelectedProject = item;
                IsProjectScope = true;
            }
        }

        if (SelectedProject is null && AvailableProjects.Count > 0)
        {
            SelectedProject = AvailableProjects[0];
        }
    }

    partial void OnArgumentsTextChanged(string value)
    {
        UpdateParsedArguments();
    }

    partial void OnWorkingDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(ResolvedWorkingDirectoryPreview));
    }

    partial void OnSelectedProjectChanged(ProjectSelectionItem? value)
    {
        OnPropertyChanged(nameof(ResolvedWorkingDirectoryPreview));
    }

    partial void OnIsProjectScopeChanged(bool value)
    {
        OnPropertyChanged(nameof(ResolvedWorkingDirectoryPreview));
    }

    private void UpdateParsedArguments()
    {
        ParsedArgumentsTokens.Clear();
        var tokens = WindowsCommandLineParser.ParseArguments(ArgumentsText);
        foreach (var token in tokens)
        {
            ParsedArgumentsTokens.Add(token);
        }
    }

    [RelayCommand]
    private void BrowseExecutable()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Executable File",
            Filter = "Executable Files (*.exe;*.com)|*.exe;*.com|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FileName))
        {
            Executable = dialog.FileName;
            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    [RelayCommand]
    private void BrowseDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Working Directory",
            Multiselect = false,
            InitialDirectory = !string.IsNullOrWhiteSpace(WorkingDirectory) && Directory.Exists(WorkingDirectory)
                ? WorkingDirectory
                : ResolvedWorkingDirectoryPreview
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            WorkingDirectory = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Command name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Executable))
        {
            ErrorMessage = "Executable is required.";
            return;
        }

        if (IsProjectScope && SelectedProject is null)
        {
            ErrorMessage = "Please select an associated project for this project-scoped command.";
            return;
        }

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }

    public SavedCommand ToModel()
    {
        var tokens = WindowsCommandLineParser.ParseArguments(ArgumentsText);

        return new SavedCommand
        {
            Id = _commandId,
            Name = Name.Trim(),
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            Executable = Executable.Trim(),
            Arguments = tokens,
            WorkingDirectory = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory.Trim(),
            Category = string.IsNullOrWhiteSpace(Category) ? null : Category.Trim(),
            IsEnabled = IsEnabled,
            ProjectId = IsProjectScope ? SelectedProject?.Id : null
        };
    }
}
