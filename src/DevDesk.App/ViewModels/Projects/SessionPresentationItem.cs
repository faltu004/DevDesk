using DevDesk.Core.Runner;

namespace DevDesk.App.ViewModels.Projects;

/// <summary>
/// Presentation wrapper around a ProjectRunSession entity providing formatted properties for WPF data binding.
/// Encapsulates local-time presentation formatting cleanly within the App presentation layer.
/// </summary>
public sealed class SessionPresentationItem
{
    public ProjectRunSession Session { get; }

    public SessionPresentationItem(ProjectRunSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public Guid SessionId => Session.SessionId;

    public Guid ProjectId => Session.ProjectId;

    public string ProjectName => Session.ProjectName;

    public int? ProcessId => Session.ProcessId;

    public ProjectRunState State => Session.State;

    public DateTimeOffset StartedAt => Session.StartedAt;

    public DateTimeOffset LocalStartedAt => Session.StartedAt.ToLocalTime();

    public string FormattedStartedAt => Session.StartedAt.ToLocalTime().ToString("HH:mm:ss");

    public override string ToString() => $"{State} {FormattedStartedAt}";
}
