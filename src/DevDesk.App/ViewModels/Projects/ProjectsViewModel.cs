using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using DevDesk.App.Services.Dialogs;
using DevDesk.App.ViewModels.Common;
using DevDesk.Core.Launchers;
using DevDesk.Core.Models;
using DevDesk.Core.Runner;
using DevDesk.Core.Services;

namespace DevDesk.App.ViewModels.Projects;

/// <summary>
/// Root ViewModel for the Projects view coordinating project lists, search filtering, auto-detection, details panel, management operations, and runner execution.
/// </summary>
public sealed partial class ProjectsViewModel : ViewModelBase, IDisposable
{
    private readonly IProjectService _projectService;
    private readonly ILauncherService _launcherService;
    private readonly IProjectRunnerService _runnerService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<ProjectsViewModel> _logger;

    [ObservableProperty]
    private string _title = "Projects";

    [ObservableProperty]
    private string _subtitle = "Manage your registered development workspaces.";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _searchPlaceholder = "Search projects by name, path, framework, or tag...";

    [ObservableProperty]
    private string _searchShortcut = "Ctrl + K";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _infoMessage;

    [ObservableProperty]
    private ProjectPresentationModel? _selectedProject;

    public ObservableCollection<ProjectPresentationModel> Projects { get; } = new();

    public ObservableCollection<ProjectPresentationModel> FilteredProjects { get; } = new();

    public int TotalCount => Projects.Count;

    public bool HasProjects => Projects.Count > 0;

    public bool HasFilteredProjects => FilteredProjects.Count > 0;

    public bool HasSelectedProject => SelectedProject is not null;

    public ProjectLogsViewModel LogsViewModel { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsLogsTabSelected))]
    private string _selectedDetailsTab = "Overview";

    public bool IsOverviewTabSelected => SelectedDetailsTab == "Overview";
    public bool IsLogsTabSelected => SelectedDetailsTab == "Logs";

    [RelayCommand]
    public void SelectDetailsTab(string tabName)
    {
        SelectedDetailsTab = tabName;
        if (tabName == "Logs")
        {
            LogsViewModel.Activate();
        }
        else
        {
            LogsViewModel.Deactivate();
        }
    }

    public ProjectsViewModel(
        IProjectService projectService,
        ILauncherService launcherService,
        IProjectRunnerService runnerService,
        IDialogService dialogService,
        ProjectLogsViewModel logsViewModel,
        ILogger<ProjectsViewModel> logger)
    {
        _projectService = projectService;
        _launcherService = launcherService;
        _runnerService = runnerService;
        _dialogService = dialogService;
        LogsViewModel = logsViewModel ?? throw new ArgumentNullException(nameof(logsViewModel));
        _logger = logger;

        _runnerService.SessionChanged += OnSessionChanged;

        _ = LoadProjectsAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedProjectChanged(ProjectPresentationModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedProject));
        if (value is not null)
        {
            LogsViewModel.SetProject(value.Id, value.ActiveSession?.SessionId);
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadProjectsAsync();
    }

    [RelayCommand]
    public async Task AddProjectAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            InfoMessage = null;

            var selectedPath = _dialogService.ShowFolderPicker(title: "Select Project Directory");
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            var addVm = new AddEditProjectViewModel(selectedPath);
            if (_dialogService.ShowAddEditProjectDialog(addVm))
            {
                var newProject = addVm.ToProject();
                var saved = await _projectService.AddProjectAsync(newProject);

                await LoadProjectsAsync(saved.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add project");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task EditProjectAsync(ProjectPresentationModel? projectModel)
    {
        if (IsBusy)
        {
            return;
        }

        var target = projectModel ?? SelectedProject;
        if (target is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            InfoMessage = null;

            var editVm = new AddEditProjectViewModel(target.Project);
            if (_dialogService.ShowAddEditProjectDialog(editVm))
            {
                var updatedProject = editVm.ToProject();
                var saved = await _projectService.UpdateProjectAsync(updatedProject);

                await LoadProjectsAsync(saved.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update project {Id}", target.Id);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RemoveProjectAsync(ProjectPresentationModel? projectModel)
    {
        if (IsBusy)
        {
            return;
        }

        var target = projectModel ?? SelectedProject;
        if (target is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            InfoMessage = null;

            var confirmVm = new ConfirmDeleteViewModel(target.Name, target.Path);
            if (_dialogService.ShowConfirmDeleteDialog(confirmVm))
            {
                await _projectService.RemoveProjectAsync(target.Id);
                await LoadProjectsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove project {Id}", target.Id);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DetectProjectAsync(ProjectPresentationModel? projectModel)
    {
        if (IsBusy)
        {
            return;
        }

        var target = projectModel ?? SelectedProject;
        if (target is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            InfoMessage = null;

            var result = await _projectService.DetectAndApplyAsync(target.Id);
            await LoadProjectsAsync(target.Id);

            if (!result.IsRecognized)
            {
                InfoMessage = "DevDesk could not identify a supported project type. The project remains registered and can still be configured manually.";
            }
            else
            {
                _logger.LogInformation("Detection succeeded for project {Name}: Framework={Framework}, Language={Language}, PM={PackageManager}",
                    target.Name, result.Framework, result.Language, result.PackageManager);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect project {Id}", target.Id);
            ErrorMessage = $"Project detection failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task OpenInVsCodeAsync(ProjectPresentationModel? projectModel)
    {
        if (IsBusy)
        {
            return;
        }

        var target = projectModel ?? SelectedProject;
        if (target is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            InfoMessage = null;

            var result = await _launcherService.OpenInVsCodeAsync(target.Path);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error launching VS Code for project {Name}", target.Name);
            ErrorMessage = "Failed to launch Visual Studio Code. Please check system permissions.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task OpenInExplorerAsync(ProjectPresentationModel? projectModel)
    {
        if (IsBusy)
        {
            return;
        }

        var target = projectModel ?? SelectedProject;
        if (target is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            InfoMessage = null;

            var result = await _launcherService.OpenInExplorerAsync(target.Path);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening Explorer for project {Name}", target.Name);
            ErrorMessage = "Failed to open Windows Explorer. Please check system permissions.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task OpenTerminalAsync(ProjectPresentationModel? projectModel)
    {
        if (IsBusy)
        {
            return;
        }

        var target = projectModel ?? SelectedProject;
        if (target is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            InfoMessage = null;

            var result = await _launcherService.OpenTerminalAsync(target.Path);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening terminal for project {Name}", target.Name);
            ErrorMessage = "Failed to open the terminal. Please check system permissions.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void DismissNotification()
    {
        ErrorMessage = null;
        InfoMessage = null;
    }

    [RelayCommand]
    public void SelectProject(ProjectPresentationModel? project)
    {
        if (project is not null)
        {
            SelectedProject = project;
        }
    }

    [RelayCommand]
    public void CopyToClipboard(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard access might rarely fail if another app locks it
        }
    }

    [RelayCommand]
    public async Task RunProjectAsync(ProjectPresentationModel? projectModel)
    {
        if (IsBusy)
        {
            return;
        }

        var target = projectModel ?? SelectedProject;
        if (target is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            InfoMessage = null;

            var result = await _runnerService.StartProjectAsync(target.Id);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run project {Id}", target.Id);
            ErrorMessage = $"Failed to run project: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task StopProjectAsync(ProjectPresentationModel? projectModel)
    {
        if (IsBusy)
        {
            return;
        }

        var target = projectModel ?? SelectedProject;
        if (target is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            InfoMessage = null;

            var result = await _runnerService.StopProjectAsync(target.Id);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop project {Id}", target.Id);
            ErrorMessage = $"Failed to stop project: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RestartProjectAsync(ProjectPresentationModel? projectModel)
    {
        if (IsBusy)
        {
            return;
        }

        var target = projectModel ?? SelectedProject;
        if (target is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            InfoMessage = null;

            var result = await _runnerService.RestartProjectAsync(target.Id);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart project {Id}", target.Id);
            ErrorMessage = $"Failed to restart project: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadProjectsAsync(Guid? selectProjectId = null)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var previousSelectedId = selectProjectId ?? SelectedProject?.Id;

            var entities = await _projectService.GetProjectsAsync();

            Projects.Clear();
            foreach (var entity in entities)
            {
                var model = new ProjectPresentationModel(entity);
                var existingSession = _runnerService.GetSession(entity.Id);
                if (existingSession is not null)
                {
                    model.UpdateSession(existingSession);
                }
                Projects.Add(model);
            }

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(HasProjects));

            ApplyFilter();

            if (previousSelectedId.HasValue)
            {
                SelectedProject = Projects.FirstOrDefault(p => p.Id == previousSelectedId.Value)
                                  ?? FilteredProjects.FirstOrDefault();
            }
            else
            {
                SelectedProject = FilteredProjects.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load projects from persistence");
            ErrorMessage = "DevDesk could not load registered projects.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnSessionChanged(object? sender, ProjectRunSession session)
    {
        Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            var project = Projects.FirstOrDefault(p => p.Id == session.ProjectId);
            project?.UpdateSession(session);
        });
    }

    public void Dispose()
    {
        _runnerService.SessionChanged -= OnSessionChanged;
        LogsViewModel.Dispose();
    }

    private void ApplyFilter()
    {
        FilteredProjects.Clear();

        var query = SearchText?.Trim();

        foreach (var project in Projects)
        {
            // Filter by search query
            if (!string.IsNullOrWhiteSpace(query))
            {
                var matchesName = project.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                var matchesPath = project.Path.Contains(query, StringComparison.OrdinalIgnoreCase);
                var matchesFramework = project.Framework?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false;
                var matchesLanguage = project.Language?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false;
                var matchesPackageManager = project.PackageManager?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false;

                if (!matchesName && !matchesPath && !matchesFramework && !matchesLanguage && !matchesPackageManager)
                {
                    continue;
                }
            }

            FilteredProjects.Add(project);
        }

        OnPropertyChanged(nameof(HasFilteredProjects));

        if (SelectedProject is not null && !FilteredProjects.Contains(SelectedProject))
        {
            SelectedProject = FilteredProjects.FirstOrDefault();
        }
        else if (SelectedProject is null && FilteredProjects.Count > 0)
        {
            SelectedProject = FilteredProjects.First();
        }
    }
}
