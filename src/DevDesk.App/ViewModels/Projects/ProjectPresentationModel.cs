using DevDesk.App.ViewModels.Common;
using DevDesk.Core.Models;

namespace DevDesk.App.ViewModels.Projects;

/// <summary>
/// Presentation wrapper around a DeveloperProject entity providing formatted properties for WPF data binding.
/// Strictly presents persisted configuration without fabricated runtime state.
/// </summary>
public sealed class ProjectPresentationModel : ViewModelBase
{
    private readonly DeveloperProject _project;

    public ProjectPresentationModel(DeveloperProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
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
