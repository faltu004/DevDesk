using CommunityToolkit.Mvvm.ComponentModel;
using DevDesk.Core.Commands;
using DevDesk.Core.Models;
using DevDesk.Infrastructure.Runner.Native;

namespace DevDesk.App.ViewModels.Commands;

/// <summary>
/// Presentation wrapper for a SavedCommand entity incorporating live runtime execution state.
/// </summary>
public sealed partial class SavedCommandItemViewModel : ObservableObject
{
    private readonly SavedCommand _model;
    private readonly string _projectName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(StatusBadgeForeground))]
    private SavedCommandExecutionState _executionState = SavedCommandExecutionState.Idle;

    [ObservableProperty]
    private int? _processId;

    [ObservableProperty]
    private int? _exitCode;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _lastRunText = "Never";

    public SavedCommand Model => _model;
    public Guid Id => _model.Id;
    public string Name => _model.Name;
    public string? Description => _model.Description;
    public string Executable => _model.Executable;
    public IReadOnlyList<string> Arguments => _model.Arguments;
    public string? WorkingDirectory => _model.WorkingDirectory;
    public string? Category => _model.Category;
    public bool IsEnabled => _model.IsEnabled;
    public Guid? ProjectId => _model.ProjectId;
    public string ProjectName => _projectName;
    public bool IsGlobal => !_model.ProjectId.HasValue;

    public string DisplayCommandLine =>
        WindowsCommandLineSerializer.FormatCommandLine(_model.Executable, _model.Arguments);

    public bool IsActive => ExecutionState is SavedCommandExecutionState.Starting or SavedCommandExecutionState.Running;
    public bool CanRun => !IsActive && IsEnabled;
    public bool CanStop => IsActive;

    public string StatusBadgeBackground => ExecutionState switch
    {
        SavedCommandExecutionState.Running => "#132D21",
        SavedCommandExecutionState.Starting => "#2A2312",
        SavedCommandExecutionState.Succeeded => "#132D21",
        SavedCommandExecutionState.Failed => "#341818",
        SavedCommandExecutionState.Cancelled => "#232936",
        _ => "Transparent"
    };

    public string StatusBadgeForeground => ExecutionState switch
    {
        SavedCommandExecutionState.Running => "#4ADE80",
        SavedCommandExecutionState.Starting => "#FBBF24",
        SavedCommandExecutionState.Succeeded => "#4ADE80",
        SavedCommandExecutionState.Failed => "#F87171",
        SavedCommandExecutionState.Cancelled => "#94A3B8",
        _ => "#64748B"
    };

    public SavedCommandItemViewModel(SavedCommand model, string? projectName = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _projectName = !string.IsNullOrWhiteSpace(projectName)
            ? projectName
            : (model.Project?.Name ?? (model.ProjectId.HasValue ? "Project" : "Global"));
    }

    public void UpdateFromSession(SavedCommandRunSession session)
    {
        ExecutionState = session.State;
        ProcessId = session.ProcessId;
        ExitCode = session.ExitCode;
        ErrorMessage = session.ErrorMessage;

        LastRunText = session.State switch
        {
            SavedCommandExecutionState.Starting => "Starting...",
            SavedCommandExecutionState.Running => $"Running (PID {session.ProcessId})",
            SavedCommandExecutionState.Succeeded => "Succeeded (exit 0)",
            SavedCommandExecutionState.Failed => session.ExitCode.HasValue ? $"Failed (exit {session.ExitCode})" : "Launch Failed",
            SavedCommandExecutionState.Cancelled => "Cancelled",
            _ => "Never"
        };
    }
}
