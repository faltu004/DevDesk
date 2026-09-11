namespace DevDesk.Core.Git;

/// <summary>
/// Service providing safe, read-only Git repository inspection and status queries.
/// </summary>
public interface IGitService
{
    /// <summary>
    /// Checks whether git.exe is installed and available.
    /// </summary>
    Task<GitAvailability> CheckGitAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Authoritatively inspects the Git repository at or enclosing the specified project directory.
    /// </summary>
    Task<GitRepositoryStatus> GetRepositoryStatusAsync(string projectPath, CancellationToken cancellationToken = default);
}
