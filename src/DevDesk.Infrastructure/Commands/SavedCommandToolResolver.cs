using System.IO;

namespace DevDesk.Infrastructure.Commands;

/// <summary>
/// Hardened resolver locating executables and trusted package-manager .cmd shims.
/// Enforces shell-safety boundaries: rejects direct cmd/PowerShell invocation,
/// validates shim arguments against shell metacharacters, rejects arbitrary .bat/.cmd scripts,
/// and performs PATH lookup without probing the command working directory.
/// </summary>
public sealed class SavedCommandToolResolver : ISavedCommandToolResolver
{
    private static readonly char[] ForbiddenShimShellChars = ['&', '|', '<', '>', '^', '%', '!', ';', '\n', '\r'];

    private static readonly HashSet<string> ProhibitedShellTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "cmd.exe", "powershell", "powershell.exe", "pwsh", "pwsh.exe"
    };

    private static readonly HashSet<string> TrustedShimToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "npm", "npx", "pnpm", "yarn", "corepack"
    };

    public ResolvedCommandTool ResolveTool(string rawExecutable, IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(rawExecutable))
        {
            return ResolvedCommandTool.Failed("Executable name or path is empty.");
        }

        var trimmed = rawExecutable.Trim();
        var fileName = Path.GetFileName(trimmed);
        var baseNameWithoutExt = Path.GetFileNameWithoutExtension(trimmed);

        // 1. Prohibit raw shell invocation
        if (ProhibitedShellTools.Contains(fileName) || ProhibitedShellTools.Contains(baseNameWithoutExt))
        {
            return ResolvedCommandTool.Failed(
                "Direct shell invocation ('cmd', 'powershell', 'pwsh') is prohibited. Specify the target developer tool directly.");
        }

        bool isExplicitPath = trimmed.Contains(Path.DirectorySeparatorChar) ||
                              trimmed.Contains(Path.AltDirectorySeparatorChar) ||
                              Path.IsPathRooted(trimmed);

        if (isExplicitPath)
        {
            return ResolveExplicitPath(trimmed, baseNameWithoutExt, arguments);
        }

        return ResolveBareTool(trimmed, baseNameWithoutExt, arguments);
    }

    private static ResolvedCommandTool ResolveExplicitPath(string explicitPath, string baseNameWithoutExt, IReadOnlyList<string> arguments)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(explicitPath);
        }
        catch (Exception ex)
        {
            return ResolvedCommandTool.Failed($"Invalid executable path '{explicitPath}': {ex.Message}");
        }

        if (!File.Exists(fullPath))
        {
            return ResolvedCommandTool.Failed($"Executable file does not exist on disk: {fullPath}");
        }

        var ext = Path.GetExtension(fullPath);
        bool isScript = ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".bat", StringComparison.OrdinalIgnoreCase);

        if (isScript)
        {
            if (!TrustedShimToolNames.Contains(baseNameWithoutExt))
            {
                return ResolvedCommandTool.Failed(
                    $"Execution of arbitrary .bat/.cmd scripts ('{Path.GetFileName(fullPath)}') is not permitted. Use direct executables or trusted tools.");
            }

            var shimValidation = ValidateShimArguments(arguments);
            if (!shimValidation.IsValid)
            {
                return ResolvedCommandTool.Failed(shimValidation.ErrorMessage!);
            }

            return ResolvedCommandTool.Succeeded(fullPath, isCmdShim: true);
        }

        return ResolvedCommandTool.Succeeded(fullPath, isCmdShim: false);
    }

    private static ResolvedCommandTool ResolveBareTool(string toolName, string baseNameWithoutExt, IReadOnlyList<string> arguments)
    {
        bool isTrustedShim = TrustedShimToolNames.Contains(baseNameWithoutExt);

        if (isTrustedShim)
        {
            var shimValidation = ValidateShimArguments(arguments);
            if (!shimValidation.IsValid)
            {
                return ResolvedCommandTool.Failed(shimValidation.ErrorMessage!);
            }

            // Look for .cmd shim or native .exe
            string? shimPath = FindInKnownLocations(baseNameWithoutExt, isCmdShim: true) ??
                               FindInPath(baseNameWithoutExt, isCmdShim: true);

            if (shimPath is not null)
            {
                return ResolvedCommandTool.Succeeded(shimPath, isCmdShim: true);
            }

            // Fallback: check if native .exe exists for trusted tool (e.g. npm.exe if custom bundled)
            string? exePath = FindInKnownLocations(baseNameWithoutExt, isCmdShim: false) ??
                              FindInPath(baseNameWithoutExt, isCmdShim: false);

            if (exePath is not null)
            {
                return ResolvedCommandTool.Succeeded(exePath, isCmdShim: false);
            }

            return ResolvedCommandTool.Failed(
                $"Could not locate '{baseNameWithoutExt}.cmd' or '{baseNameWithoutExt}.exe' in system PATH or standard locations.");
        }

        // Native executable lookup only (never resolve arbitrary .cmd / .bat from PATH for non-trusted tools)
        string? nativePath = FindInKnownLocations(baseNameWithoutExt, isCmdShim: false) ??
                             FindInPath(baseNameWithoutExt, isCmdShim: false);

        if (nativePath is not null)
        {
            return ResolvedCommandTool.Succeeded(nativePath, isCmdShim: false);
        }

        return ResolvedCommandTool.Failed(
            $"Could not locate executable '{baseNameWithoutExt}' in system PATH or standard locations.");
    }

    private static (bool IsValid, string? ErrorMessage) ValidateShimArguments(IReadOnlyList<string> arguments)
    {
        foreach (var arg in arguments)
        {
            if (arg.IndexOfAny(ForbiddenShimShellChars) >= 0)
            {
                return (false,
                    "Command contains forbidden shell metacharacters (&, |, <, >, ^, %, !, ;) in arguments for .cmd shim.");
            }
        }

        return (true, null);
    }

    private static string? FindInPath(string toolName, bool isCmdShim)
    {
        var targetFile = isCmdShim ? $"{toolName}.cmd" : $"{toolName}.exe";

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var dirs = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var dir in dirs)
        {
            try
            {
                var candidate = Path.Combine(dir, targetFile);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore invalid PATH entries
            }
        }

        return null;
    }

    private static string? FindInKnownLocations(string toolName, bool isCmdShim)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return (toolName.ToLowerInvariant(), isCmdShim) switch
        {
            ("dotnet", false) => Path.Combine(programFiles, "dotnet", "dotnet.exe"),
            ("git", false) => Path.Combine(programFiles, "Git", "cmd", "git.exe"),
            ("bun", false) => Path.Combine(userProfile, ".bun", "bin", "bun.exe"),
            ("npm", true) => Path.Combine(programFiles, "nodejs", "npm.cmd"),
            ("npx", true) => Path.Combine(programFiles, "nodejs", "npx.cmd"),
            ("pnpm", true) => Path.Combine(localAppData, "pnpm", "pnpm.cmd"),
            ("yarn", true) => Path.Combine(appData, "npm", "yarn.cmd"),
            _ => null
        };
    }
}
