namespace DevDesk.Core.Git;

/// <summary>
/// Error codes for Git repository inspection failures.
/// </summary>
public enum GitErrorCode
{
    None = 0,
    NotAGitRepository,
    GitNotInstalled,
    PathNotFound,
    AccessDenied,
    SafeDirectoryViolation,
    LockedOrBusy,
    Timeout,
    Cancelled,
    OutputTooLarge,
    ExecutionFailed
}
