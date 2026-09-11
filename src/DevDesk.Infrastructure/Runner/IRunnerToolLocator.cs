using System.IO;

namespace DevDesk.Infrastructure.Runner;

/// <summary>
/// Abstraction for resolving concrete file paths for runner tools and package manager shims.
/// </summary>
internal interface IRunnerToolLocator
{
    /// <summary>
    /// Resolves the absolute path to the tool or shim on disk, or null if not found.
    /// </summary>
    string? ResolveToolPath(string toolName, bool isCmdShim);
}

/// <summary>
/// Production implementation searching Windows PATH and known installation directories.
/// </summary>
internal sealed class WindowsRunnerToolLocator : IRunnerToolLocator
{
    public string? ResolveToolPath(string toolName, bool isCmdShim)
    {
        string fileName = isCmdShim ? $"{toolName}.cmd" : $"{toolName}.exe";

        // 1. Check known specific locations
        string? knownPath = GetKnownLocation(toolName, fileName);
        if (knownPath is not null && File.Exists(knownPath))
        {
            return knownPath;
        }

        // 2. Search PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathDirs = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var dir in pathDirs)
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                // If looking for native exe (e.g. bun on non-windows or bun without extension)
                if (!isCmdShim && !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var exeCandidate = Path.Combine(dir, $"{toolName}.exe");
                    if (File.Exists(exeCandidate))
                    {
                        return exeCandidate;
                    }
                }
            }
            catch
            {
                // Ignore invalid PATH entries
            }
        }

        return null;
    }

    private static string? GetKnownLocation(string toolName, string fileName)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return toolName switch
        {
            "dotnet" => Path.Combine(programFiles, "dotnet", "dotnet.exe"),
            "npm" => Path.Combine(programFiles, "nodejs", "npm.cmd"),
            "pnpm" => Path.Combine(localAppData, "pnpm", "pnpm.cmd"),
            "yarn" => Path.Combine(appData, "npm", "yarn.cmd"),
            "bun" => Path.Combine(userProfile, ".bun", "bin", "bun.exe"),
            _ => null
        };
    }
}
