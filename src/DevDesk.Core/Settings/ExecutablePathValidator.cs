using System.IO;

namespace DevDesk.Core.Settings;

/// <summary>
/// Validates external executable path overrides according to DevDesk security constraints.
/// Prohibits shell scripts (.cmd, .bat, .ps1, .vbs) and requires existing .exe binaries.
/// </summary>
public static class ExecutablePathValidator
{
    private static readonly HashSet<string> DisallowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cmd", ".bat", ".ps1", ".vbs", ".js", ".sh", ".com", ".pif", ".scr", ".wsf", ".msc"
    };

    public static bool TryValidate(string? path, out string? validatedPath, out string? errorMessage)
    {
        validatedPath = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            // Empty string indicates auto-detection preference
            return true;
        }

        string trimmed = path.Trim().Trim('\"');
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(trimmed);
        }
        catch (Exception ex)
        {
            errorMessage = $"Invalid path syntax: {ex.Message}";
            return false;
        }

        string ext = Path.GetExtension(fullPath);
        if (DisallowedExtensions.Contains(ext))
        {
            errorMessage = $"Shell or script extensions ({ext}) are prohibited for security. An existing .exe binary is required.";
            return false;
        }

        if (!string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Executable path must have a .exe extension.";
            return false;
        }

        if (!File.Exists(fullPath))
        {
            errorMessage = $"Executable not found at '{fullPath}'.";
            return false;
        }

        validatedPath = fullPath;
        return true;
    }
}
