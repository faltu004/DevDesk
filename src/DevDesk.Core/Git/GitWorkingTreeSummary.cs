namespace DevDesk.Core.Git;

/// <summary>
/// Aggregated counts of modified, staged, untracked, and conflicted files in the repository working tree.
/// </summary>
public sealed record GitWorkingTreeSummary(
    int StagedCount = 0,
    int UnstagedCount = 0,
    int UntrackedCount = 0,
    int ConflictedCount = 0)
{
    public bool HasModifications => StagedCount > 0 || UnstagedCount > 0 || UntrackedCount > 0 || ConflictedCount > 0;
    public bool IsClean => !HasModifications;
}
