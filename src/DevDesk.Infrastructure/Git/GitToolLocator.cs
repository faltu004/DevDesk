using System;
using System.IO;

namespace DevDesk.Infrastructure.Git;

/// <summary>
/// Locates git.exe via PATH or standard Windows installation paths.
/// </summary>
public sealed class GitToolLocator : IGitToolLocator
{
    private string? _cachedGitPath;
    private bool _hasSearched;
    private readonly object _lock = new();

    public bool TryLocateGit(out string gitPath)
    {
        lock (_lock)
        {
            if (_hasSearched)
            {
                gitPath = _cachedGitPath ?? string.Empty;
                return _cachedGitPath is not null;
            }

            _hasSearched = true;

            // 1. Check PATH environment variable
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var dir in paths)
                {
                    try
                    {
                        var candidate = Path.Combine(dir, "git.exe");
                        if (File.Exists(candidate))
                        {
                            _cachedGitPath = candidate;
                            gitPath = candidate;
                            return true;
                        }
                    }
                    catch
                    {
                        // Ignore inaccessible directories
                    }
                }
            }

            // 2. Check standard Windows installation locations
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            string[] wellKnownCandidates =
            [
                Path.Combine(programFiles, "Git", "cmd", "git.exe"),
                Path.Combine(programFiles, "Git", "bin", "git.exe"),
                Path.Combine(localAppData, "Programs", "Git", "cmd", "git.exe"),
                Path.Combine(localAppData, "Programs", "Git", "bin", "git.exe"),
                Path.Combine(programFilesX86, "Git", "cmd", "git.exe")
            ];

            foreach (var candidate in wellKnownCandidates)
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        _cachedGitPath = candidate;
                        gitPath = candidate;
                        return true;
                    }
                }
                catch
                {
                    // Ignore inaccessible locations
                }
            }

            gitPath = string.Empty;
            return false;
        }
    }
}
