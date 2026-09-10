using System.IO;

namespace DevDesk.Infrastructure.Detection;

/// <summary>
/// Provides read-only filesystem detection of Git repository roots, worktrees, and submodules.
/// Does not invoke git.exe or read internal commit/branch logs.
/// </summary>
public static class GitDetector
{
    private const int MaxParentTraversalLevels = 32;

    /// <summary>
    /// Checks whether the specified directory or any of its ancestors up to 32 levels is a Git repository.
    /// Supports both .git directories and .git pointer files (used by git worktree and submodules).
    /// </summary>
    public static bool DetectIsGitRepository(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        try
        {
            var current = new DirectoryInfo(directoryPath);
            var levels = 0;

            while (current != null && levels < MaxParentTraversalLevels)
            {
                var gitPath = Path.Combine(current.FullName, ".git");

                // Check for .git directory or .git file (worktree/submodule)
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                {
                    return true;
                }

                current = current.Parent;
                levels++;
            }
        }
        catch
        {
            // Inaccessible directories or paths do not cause failure; treat as non-git.
            return false;
        }

        return false;
    }
}
