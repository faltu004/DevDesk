using DevDesk.App.ViewModels.Common;
using DevDesk.Core.Models;
using DevDesk.Core.Runner;

namespace DevDesk.App.ViewModels.Projects;

/// <summary>
/// Presentation wrapper around a DeveloperProject entity providing formatted properties for WPF data binding.
/// Strictly presents persisted configuration and truthful DevDesk-owned runtime state.
/// </summary>
public sealed class ProjectPresentationModel : ViewModelBase
{
    private readonly DeveloperProject _project;
    private ProjectRunSession? _activeSession;

    public ProjectPresentationModel(DeveloperProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public ProjectRunSession? ActiveSession => _activeSession;

    public ProjectRunState? RunState => _activeSession?.State;

    public bool HasRunState => _activeSession is not null;

    public bool IsRunning => _activeSession?.State == ProjectRunState.Running;

    public bool IsStarting => _activeSession?.State == ProjectRunState.Starting;

    public bool IsStopping => _activeSession?.State == ProjectRunState.Stopping;

    public bool IsActive => _activeSession is not null && _activeSession.IsActive;

    public int? ActiveProcessId => _activeSession?.ProcessId;

    public string? RunStateDisplay => _activeSession?.State switch
    {
        ProjectRunState.Starting => "Starting...",
        ProjectRunState.Running => _activeSession.ProcessId.HasValue
            ? $"Running (PID {_activeSession.ProcessId.Value})"
            : "Running",
        ProjectRunState.Stopping => "Stopping...",
        ProjectRunState.Exited => _activeSession.TerminationReason switch
        {
            ProjectTerminationReason.StoppedByDevDesk => "Stopped by DevDesk",
            _ => _activeSession.ExitCode.HasValue
                ? $"Exited (code {_activeSession.ExitCode.Value})"
                : "Exited"
        },
        ProjectRunState.Failed => _activeSession.TerminationReason == ProjectTerminationReason.LaunchFailed
            ? "Failed to start"
            : "Failed",
        _ => null
    };

    public void UpdateSession(ProjectRunSession? session)
    {
        // Stale session guard: prevent an older or superseded session from overwriting a newer session
        if (session is not null && _activeSession is not null)
        {
            if (session.StartedAt < _activeSession.StartedAt)
            {
                return;
            }
        }

        _activeSession = session;
        OnPropertyChanged(nameof(ActiveSession));
        OnPropertyChanged(nameof(RunState));
        OnPropertyChanged(nameof(HasRunState));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(IsStopping));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ActiveProcessId));
        OnPropertyChanged(nameof(RunStateDisplay));
    }

    public DeveloperProject Project => _project;

    public Guid Id => _project.Id;

    public string Name => _project.Name;

    public string Path => _project.Path;

    public string? Framework => _project.Framework;

    public string FrameworkDisplay => string.IsNullOrWhiteSpace(_project.Framework) ? "—" : _project.Framework;

    public string? Language => _project.Language;

    public string? PackageManager => _project.PackageManager;

    public string LanguageDisplay => string.IsNullOrWhiteSpace(_project.Language) ? "—" : _project.Language;

    public string PackageManagerDisplay => string.IsNullOrWhiteSpace(_project.PackageManager) ? "—" : _project.PackageManager;

    public bool HasPackageManager => !string.IsNullOrWhiteSpace(_project.PackageManager);

    public string? RunCommand => _project.RunCommand;

    public string RunCommandDisplay => string.IsNullOrWhiteSpace(_project.RunCommand) ? "—" : _project.RunCommand;

    public string? BuildCommand => _project.BuildCommand;

    public string BuildCommandDisplay => string.IsNullOrWhiteSpace(_project.BuildCommand) ? "—" : _project.BuildCommand;

    public string? TestCommand => _project.TestCommand;

    public string TestCommandDisplay => string.IsNullOrWhiteSpace(_project.TestCommand) ? "—" : _project.TestCommand;

    public int? DefaultPort => _project.DefaultPort;

    public string PortDisplay => _project.DefaultPort.HasValue ? _project.DefaultPort.Value.ToString() : "—";

    public bool IsGitRepository => _project.IsGitRepository;

    public DateTimeOffset CreatedAt => _project.CreatedAt;

    public DateTimeOffset? LastOpenedAt => _project.LastOpenedAt;

    public string LastOpenedDisplay => FormatRelativeTime(_project.LastOpenedAt ?? _project.CreatedAt);

    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_project.Name))
            {
                return "PR";
            }

            var parts = _project.Name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
            }

            return _project.Name.Length >= 2
                ? _project.Name[..2].ToUpperInvariant()
                : _project.Name.ToUpperInvariant();
        }
    }

    private static string FormatRelativeTime(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.UtcNow - timestamp;

        if (elapsed.TotalMinutes < 1)
        {
            return "Just now";
        }
        if (elapsed.TotalMinutes < 60)
        {
            var minutes = (int)elapsed.TotalMinutes;
            return $"{minutes} {(minutes == 1 ? "minute" : "minutes")} ago";
        }
        if (elapsed.TotalHours < 24)
        {
            var hours = (int)elapsed.TotalHours;
            return $"{hours} {(hours == 1 ? "hour" : "hours")} ago";
        }
        if (elapsed.TotalDays < 30)
        {
            var days = (int)elapsed.TotalDays;
            return $"{days} {(days == 1 ? "day" : "days")} ago";
        }

        return timestamp.ToString("MMM d, yyyy");
    }
}
