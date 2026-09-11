namespace DevDesk.Core.Git;

/// <summary>
/// Summary of the last commit in the active branch.
/// </summary>
public sealed record GitCommitInfo(
    string Hash,
    string ShortHash,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset Timestamp,
    string Subject);
