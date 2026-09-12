using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using DevDesk.App.Services.Dialogs;
using DevDesk.App.ViewModels.Common;
using DevDesk.Core.Commands;
using DevDesk.Core.Models;
using DevDesk.Core.Repositories;
using DevDesk.Core.Services;

namespace DevDesk.App.ViewModels.Commands;

public enum CommandScopeFilter
{
    ProjectCommands,
    GlobalCommands
}

public sealed record ProjectCommandGroup(string ProjectName, ObservableCollection<SavedCommandItemViewModel> Commands)
{
    public int Count => Commands.Count;
    public string HeaderText => $"{ProjectName} ({Count} command{(Count == 1 ? string.Empty : "s")})";
}

/// <summary>
/// Root ViewModel coordinating the Saved Commands screen, filtering, real-time output inspection,
/// and application lifecycle rehydration.
/// </summary>
public sealed partial class CommandsViewModel : ViewModelBase, IAsyncInitializable, IAsyncDeactivatable
{
    private readonly ISavedCommandService _commandService;
    private readonly ISavedCommandExecutor _executor;
    private readonly IProjectRepository _projectRepository;
    private readonly IDialogService _dialogService;
    private readonly ILogger<CommandsViewModel> _logger;

    private readonly ObservableCollection<SavedCommandItemViewModel> _allCommands = new();
    private bool _isSubscribed;

    [ObservableProperty]
    private CommandScopeFilter _selectedScope = CommandScopeFilter.ProjectCommands;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedProjectFilter = "All Projects";

    [ObservableProperty]
    private string _selectedCategoryFilter = "All Categories";

    [ObservableProperty]
    private SavedCommandItemViewModel? _selectedCommand;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _infoMessage;

    public ObservableCollection<SavedCommandItemViewModel> FilteredCommands { get; } = new();
    public ObservableCollection<ProjectCommandGroup> GroupedProjectCommands { get; } = new();
    public ObservableCollection<string> AvailableProjects { get; } = new();
    public ObservableCollection<string> AvailableCategories { get; } = new();
    public ObservableCollection<SavedCommandOutputEvent> RecentOutputLines { get; } = new();

    public bool IsProjectScopeSelected => SelectedScope == CommandScopeFilter.ProjectCommands;
    public bool IsGlobalScopeSelected => SelectedScope == CommandScopeFilter.GlobalCommands;
    public bool HasSelectedCommand => SelectedCommand is not null;
    public bool HasRecentOutput => RecentOutputLines.Count > 0;

    private readonly Action<Action> _dispatchOnUiThread;

    public CommandsViewModel(
        ISavedCommandService commandService,
        ISavedCommandExecutor executor,
        IProjectRepository projectRepository,
        IDialogService dialogService,
        ILogger<CommandsViewModel> logger,
        Action<Action>? uiDispatcher = null)
    {
        _commandService = commandService;
        _executor = executor;
        _projectRepository = projectRepository;
        _dialogService = dialogService;
        _logger = logger;
        _dispatchOnUiThread = uiDispatcher ?? DefaultDispatchOnUiThread;
    }

    public async Task InitializeAsync()
    {
        EnsureSubscribed();
        await LoadCommandsAsync();
    }

    public Task DeactivateAsync()
    {
        // Detach UI event handlers so background events don't attempt UI dispatch while inactive.
        // Background processes continue running in the application-scoped ISavedCommandExecutor.
        DetachSubscribed();
        return Task.CompletedTask;
    }

    private void EnsureSubscribed()
    {
        if (_isSubscribed)
        {
            return;
        }

        _executor.SessionChanged += OnSessionChanged;
        _executor.OutputReceived += OnOutputReceived;
        _isSubscribed = true;
    }

    private void DetachSubscribed()
    {
        if (!_isSubscribed)
        {
            return;
        }

        _executor.SessionChanged -= OnSessionChanged;
        _executor.OutputReceived -= OnOutputReceived;
        _isSubscribed = false;
    }

    public async Task LoadCommandsAsync(Guid? selectCommandId = null)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var commands = await _commandService.GetAllCommandsAsync();
            var projects = await _projectRepository.GetAllAsync();
            var projectLookup = projects.ToDictionary(p => p.Id, p => p.Name);

            _allCommands.Clear();
            foreach (var cmd in commands)
            {
                string? projName = cmd.ProjectId.HasValue && projectLookup.TryGetValue(cmd.ProjectId.Value, out var name)
                    ? name
                    : null;

                var item = new SavedCommandItemViewModel(cmd, projName);

                // Rehydrate session snapshot from executor (never from persisted fake state)
                var session = _executor.GetLatestSession(cmd.Id);
                if (session is not null)
                {
                    item.UpdateFromSession(session);
                }

                _allCommands.Add(item);
            }

            // Populate project and category filter options
            AvailableProjects.Clear();
            AvailableProjects.Add("All Projects");
            foreach (var p in projects.OrderBy(p => p.Name))
            {
                AvailableProjects.Add(p.Name);
            }

            AvailableCategories.Clear();
            AvailableCategories.Add("All Categories");
            var distinctCategories = commands
                .Where(c => !string.IsNullOrWhiteSpace(c.Category))
                .Select(c => c.Category!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c);

            foreach (var cat in distinctCategories)
            {
                AvailableCategories.Add(cat);
            }

            ApplyFilter(selectCommandId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load saved commands");
            ErrorMessage = $"Failed to load commands: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedScopeChanged(CommandScopeFilter value)
    {
        OnPropertyChanged(nameof(IsProjectScopeSelected));
        OnPropertyChanged(nameof(IsGlobalScopeSelected));
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedProjectFilterChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedCategoryFilterChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedCommandChanged(SavedCommandItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedCommand));
        RehydrateOutputForSelectedCommand();
    }

    private void ApplyFilter(Guid? preferredSelectId = null)
    {
        FilteredCommands.Clear();

        var query = _allCommands.AsEnumerable();

        // 1. Scope filter
        query = SelectedScope == CommandScopeFilter.ProjectCommands
            ? query.Where(c => !c.IsGlobal)
            : query.Where(c => c.IsGlobal);

        // 2. Search filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(c =>
                c.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (c.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                c.Executable.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                c.ProjectName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (c.Category?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // 3. Project filter
        if (SelectedScope == CommandScopeFilter.ProjectCommands &&
            !string.IsNullOrWhiteSpace(SelectedProjectFilter) &&
            SelectedProjectFilter != "All Projects")
        {
            query = query.Where(c => string.Equals(c.ProjectName, SelectedProjectFilter, StringComparison.OrdinalIgnoreCase));
        }

        // 4. Category filter
        if (!string.IsNullOrWhiteSpace(SelectedCategoryFilter) &&
            SelectedCategoryFilter != "All Categories")
        {
            query = query.Where(c => string.Equals(c.Category, SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        var results = query.ToList();
        foreach (var item in results)
        {
            FilteredCommands.Add(item);
        }

        // Build groups for Project Commands view
        GroupedProjectCommands.Clear();
        if (SelectedScope == CommandScopeFilter.ProjectCommands)
        {
            var groups = results
                .GroupBy(c => c.ProjectName)
                .OrderBy(g => g.Key);

            foreach (var g in groups)
            {
                GroupedProjectCommands.Add(new ProjectCommandGroup(g.Key, new ObservableCollection<SavedCommandItemViewModel>(g)));
            }
        }

        // Selection preservation
        if (preferredSelectId.HasValue)
        {
            SelectedCommand = FilteredCommands.FirstOrDefault(c => c.Id == preferredSelectId.Value);
        }
        else if (SelectedCommand is not null && !FilteredCommands.Contains(SelectedCommand))
        {
            SelectedCommand = FilteredCommands.FirstOrDefault();
        }
        else if (SelectedCommand is null && FilteredCommands.Count > 0)
        {
            SelectedCommand = FilteredCommands[0];
        }
    }

    private void RehydrateOutputForSelectedCommand()
    {
        RecentOutputLines.Clear();

        if (SelectedCommand is null)
        {
            OnPropertyChanged(nameof(HasRecentOutput));
            return;
        }

        var latest = _executor.GetLatestSession(SelectedCommand.Id);
        if (latest is not null)
        {
            var output = _executor.GetSessionOutput(SelectedCommand.Id, latest.SessionId);
            foreach (var line in output)
            {
                RecentOutputLines.Add(line);
            }
        }

        OnPropertyChanged(nameof(HasRecentOutput));
    }

    private void OnSessionChanged(object? sender, SavedCommandRunSession session)
    {
        DispatchOnUiThread(() =>
        {
            var target = _allCommands.FirstOrDefault(c => c.Id == session.CommandId);
            target?.UpdateFromSession(session);

            if (SelectedCommand is not null && SelectedCommand.Id == session.CommandId)
            {
                OnPropertyChanged(nameof(SelectedCommand));
            }
        });
    }

    private void OnOutputReceived(object? sender, SavedCommandOutputEvent evt)
    {
        DispatchOnUiThread(() =>
        {
            if (SelectedCommand is not null && SelectedCommand.Id == evt.CommandId)
            {
                RecentOutputLines.Add(evt);
                OnPropertyChanged(nameof(HasRecentOutput));
            }
        });
    }

    [RelayCommand]
    private void SelectScope(CommandScopeFilter scope)
    {
        SelectedScope = scope;
    }

    [RelayCommand]
    public async Task RunCommandAsync(SavedCommandItemViewModel? targetItem)
    {
        var target = targetItem ?? SelectedCommand;
        if (target is null || target.IsActive)
        {
            return;
        }

        try
        {
            ErrorMessage = null;
            var result = await _executor.RunCommandAsync(target.Id);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate execution for command '{Name}'", target.Name);
            ErrorMessage = $"Execution failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task StopCommandAsync(SavedCommandItemViewModel? targetItem)
    {
        var target = targetItem ?? SelectedCommand;
        if (target is null || !target.IsActive)
        {
            return;
        }

        try
        {
            await _executor.StopCommandAsync(target.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop execution for command '{Name}'", target.Name);
            ErrorMessage = $"Stop failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task AddCommandAsync()
    {
        try
        {
            var projects = await _projectRepository.GetAllAsync();
            Guid? preselectProjId = SelectedScope == CommandScopeFilter.ProjectCommands && SelectedCommand?.ProjectId is not null
                ? SelectedCommand.ProjectId
                : projects.FirstOrDefault()?.Id;

            var dialogVm = new AddEditCommandViewModel(projects, preselectProjId);
            if (_dialogService.ShowAddEditCommandDialog(dialogVm))
            {
                var newModel = dialogVm.ToModel();
                var saved = await _commandService.SaveCommandAsync(newModel);
                await LoadCommandsAsync(saved.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add saved command");
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    public async Task EditCommandAsync(SavedCommandItemViewModel? targetItem)
    {
        var target = targetItem ?? SelectedCommand;
        if (target is null)
        {
            return;
        }

        try
        {
            var projects = await _projectRepository.GetAllAsync();
            var dialogVm = new AddEditCommandViewModel(target.Model, projects);
            if (_dialogService.ShowAddEditCommandDialog(dialogVm))
            {
                var updatedModel = dialogVm.ToModel();
                var saved = await _commandService.SaveCommandAsync(updatedModel);
                await LoadCommandsAsync(saved.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to edit saved command {Id}", target.Id);
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    public async Task DeleteCommandAsync(SavedCommandItemViewModel? targetItem)
    {
        var target = targetItem ?? SelectedCommand;
        if (target is null)
        {
            return;
        }

        try
        {
            if (target.IsActive)
            {
                ErrorMessage = "Cannot delete a command while it is running. Stop the command first.";
                return;
            }

            var confirmVm = new ConfirmDeleteCommandViewModel(target.Name, target.DisplayCommandLine);
            if (_dialogService.ShowConfirmDeleteCommandDialog(confirmVm))
            {
                await _commandService.DeleteCommandAsync(target.Id);
                await LoadCommandsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete saved command {Id}", target.Id);
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void ClearOutput()
    {
        RecentOutputLines.Clear();
        OnPropertyChanged(nameof(HasRecentOutput));
    }

    [RelayCommand]
    private void CopyOutput()
    {
        if (RecentOutputLines.Count == 0)
        {
            return;
        }

        var text = string.Join(Environment.NewLine, RecentOutputLines.Select(l => l.Text));
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
        }
    }

    private void DispatchOnUiThread(Action action)
    {
        _dispatchOnUiThread(action);
    }

    private static void DefaultDispatchOnUiThread(Action action)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && dispatcher.Thread.IsAlive && !dispatcher.HasShutdownStarted && !dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(action);
        }
        else
        {
            action();
        }
    }
}
