namespace DevDesk.Core.Runner;

/// <summary>
/// Truthful termination reason of a completed or failed DevDesk run session.
/// </summary>
public enum ProjectTerminationReason
{
    None,
    NaturalExit,
    StoppedByDevDesk,
    LaunchFailed
}
