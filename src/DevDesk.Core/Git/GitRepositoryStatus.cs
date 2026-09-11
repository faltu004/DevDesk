namespace DevDesk.Core.Git;

/// <summary>
/// Authoritative immutable snapshot of a project's Git repository status.
/// </summary>
public sealed class GitRepositoryStatus
{
    public bool IsGitRepository { get; init; }
    public string? RepositoryRoot { get; init; }
    public GitBranchInfo? Branch { get; init; }
    public GitWorkingTreeSummary? WorkingTree { get; init; }
    public GitCommitInfo? LastCommit { get; init; }
    public GitErrorCode ErrorCode { get; init; } = GitErrorCode.None;
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public static GitRepositoryStatus NotGit(string? errorMessage = null) => new()
    {
        IsGitRepository = false,
        ErrorCode = GitErrorCode.NotAGitRepository,
        ErrorMessage = errorMessage ?? "Not a Git repository"
    };

    public static GitRepositoryStatus Error(GitErrorCode errorCode, string message, string? root = null) => new()
    {
        IsGitRepository = false,
        RepositoryRoot = root,
        ErrorCode = errorCode,
        ErrorMessage = message
    };
}
