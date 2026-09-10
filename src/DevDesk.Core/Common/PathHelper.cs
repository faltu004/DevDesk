namespace DevDesk.Core.Common;

/// <summary>
/// Provides path normalization and validation helpers for local projects and workspaces.
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// Normalizes a filesystem path by resolving full path semantics and trimming any trailing directory separators.
    /// </summary>
    /// <param name="path">The input directory or file path.</param>
    /// <returns>A standardized, normalized absolute path string.</returns>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(path.Trim());
        return Path.TrimEndingDirectorySeparator(fullPath);
    }
}
