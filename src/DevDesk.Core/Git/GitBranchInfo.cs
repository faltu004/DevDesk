namespace DevDesk.Core.Git;

/// <summary>
/// Immutable snapshot of active branch and tracking upstream information.
/// </summary>
public sealed record GitBranchInfo(
    string BranchName,
    string? UpstreamBranch = null,
    int? AheadCount = null,
    int? BehindCount = null,
    bool IsDetached = false,
    bool IsUnborn = false)
{
    public bool HasUpstream => !string.IsNullOrEmpty(UpstreamBranch);
}
