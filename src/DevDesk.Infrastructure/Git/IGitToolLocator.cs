namespace DevDesk.Infrastructure.Git;

/// <summary>
/// Service that discovers the location of git.exe on the local Windows machine.
/// </summary>
public interface IGitToolLocator
{
    /// <summary>
    /// Attempts to find the full path to git.exe.
    /// </summary>
    bool TryLocateGit(out string gitPath);
}
