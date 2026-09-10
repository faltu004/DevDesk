using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using DevDesk.Core.Common;
using DevDesk.Core.Detection;

namespace DevDesk.Infrastructure.Detection.Detectors;

/// <summary>
/// Detects .NET, C#, and F# solutions and projects by inspecting static *.sln, *.slnx, and *.csproj files.
/// Understands solution project references without recursive filesystem scans.
/// Conservative command generation avoids guessing when multiple or zero runnable projects exist.
/// </summary>
public sealed class DotNetProjectDetector : IProjectDetector
{
    private const long MaxProjectFileSizeBytes = 524_288; // 512 KB
    private const long MaxSolutionFileSizeBytes = 2_097_152; // 2 MB

    private static readonly Regex SlnProjectRegex = new(
        @"^\s*Project\s*\([^)]*\)\s*=\s*""[^""]*""\s*,\s*""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private readonly ILogger<DotNetProjectDetector> _logger;

    public int Order => 20;

    public DotNetProjectDetector(ILogger<DotNetProjectDetector> logger)
    {
        _logger = logger;
    }

    public Task<ProjectDetectionResult?> DetectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            return Task.FromResult<ProjectDetectionResult?>(null);
        }

        try
        {
            var slnFiles = Directory.GetFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(projectPath, "*.slnx", SearchOption.TopDirectoryOnly))
                .ToArray();

            var topCsprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly);
            var topFsprojFiles = Directory.GetFiles(projectPath, "*.fsproj", SearchOption.TopDirectoryOnly);

            var referencedProjectPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // If solution files exist, parse referenced project files directly from them
            if (slnFiles.Length > 0)
            {
                foreach (var sln in slnFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var refs = ParseSolutionProjectReferences(sln, projectPath);
                    foreach (var r in refs)
                    {
                        referencedProjectPaths.Add(r);
                    }
                }
            }

            // Also include any top-level project files
            foreach (var cp in topCsprojFiles) referencedProjectPaths.Add(cp);
            foreach (var fp in topFsprojFiles) referencedProjectPaths.Add(fp);

            // If no sln or top projects found, inspect immediate 1-level child directories
            if (slnFiles.Length == 0 && referencedProjectPaths.Count == 0)
            {
                var immediateCsproj = SafeGetFiles(projectPath, "*.csproj");
                var immediateFsproj = SafeGetFiles(projectPath, "*.fsproj");
                foreach (var cp in immediateCsproj) referencedProjectPaths.Add(cp);
                foreach (var fp in immediateFsproj) referencedProjectPaths.Add(fp);
            }

            if (slnFiles.Length == 0 && referencedProjectPaths.Count == 0)
            {
                return Task.FromResult<ProjectDetectionResult?>(null);
            }

            var markers = new List<string>();
            foreach (var sln in slnFiles) markers.Add(Path.GetFileName(sln));

            var normalizedRoot = PathHelper.NormalizePath(projectPath);
            var runnableRelativePaths = new List<string>();
            var hasTestProject = false;
            var validProjectCount = 0;
            string framework = ".NET";
            var hasFSharp = false;

            foreach (var fullProjPath in referencedProjectPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (fullProjPath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
                {
                    hasFSharp = true;
                }

                markers.Add(Path.GetFileName(fullProjPath));

                if (InspectProjectFile(fullProjPath, ref framework, out var isRunnable, out var isTest))
                {
                    validProjectCount++;
                    if (isTest)
                    {
                        hasTestProject = true;
                    }

                    if (isRunnable)
                    {
                        var rel = Path.GetRelativePath(normalizedRoot, fullProjPath);
                        runnableRelativePaths.Add(rel);
                    }
                }
            }

            // If there are no solution files and all project files failed parsing, return null
            if (slnFiles.Length == 0 && validProjectCount == 0)
            {
                return Task.FromResult<ProjectDetectionResult?>(null);
            }

            var language = hasFSharp && !referencedProjectPaths.Any(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                ? "F#"
                : "C#";
            var packageManager = "NuGet";

            // Determine suggested run command: only if exactly ONE runnable project is found
            string? suggestedRun = null;
            if (runnableRelativePaths.Count == 1)
            {
                var rel = runnableRelativePaths[0];
                suggestedRun = rel.Contains(Path.DirectorySeparatorChar) || rel.Contains('/')
                    ? $"dotnet run --project \"{rel}\""
                    : "dotnet run";
            }

            string? suggestedBuild = "dotnet build";
            string? suggestedTest = hasTestProject ? "dotnet test" : null;

            var result = new ProjectDetectionResult
            {
                Framework = framework,
                Language = language,
                PackageManager = packageManager,
                SuggestedRunCommand = suggestedRun,
                SuggestedBuildCommand = suggestedBuild,
                SuggestedTestCommand = suggestedTest,
                SuggestedDefaultPort = null,
                DetectionMarkers = markers.Distinct().ToList()
            };

            return Task.FromResult<ProjectDetectionResult?>(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect .NET project files in '{Path}'", projectPath);
            return Task.FromResult<ProjectDetectionResult?>(null);
        }
    }

    private IEnumerable<string> ParseSolutionProjectReferences(string solutionFilePath, string projectRoot)
    {
        var resolvedPaths = new List<string>();
        try
        {
            var fileInfo = new FileInfo(solutionFilePath);
            if (fileInfo.Length > MaxSolutionFileSizeBytes)
            {
                return resolvedPaths;
            }

            var normalizedRoot = PathHelper.NormalizePath(projectRoot);
            var isSlnx = solutionFilePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

            IEnumerable<string> rawPaths = isSlnx
                ? ParseSlnx(solutionFilePath)
                : ParseSln(solutionFilePath);

            foreach (var raw in rawPaths)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                // Filter extensions early
                if (!raw.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) &&
                    !raw.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var solutionDir = Path.GetDirectoryName(solutionFilePath) ?? normalizedRoot;
                    var normalizedRel = raw.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    var fullPath = Path.GetFullPath(Path.Combine(solutionDir, normalizedRel));

                    // Security: Ensure path stays strictly within projectRoot
                    if (!fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Skipping solution reference '{Ref}' because it points outside project root", raw);
                        continue;
                    }

                    // Security: Reject bin, obj, or .git directories
                    var parts = fullPath.Split(Path.DirectorySeparatorChar);
                    if (parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                       p.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                                       p.Equals(".git", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (File.Exists(fullPath))
                    {
                        resolvedPaths.Add(fullPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to resolve project reference '{Ref}' in solution '{Sln}'", raw, solutionFilePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read solution file '{Path}'", solutionFilePath);
        }

        return resolvedPaths;
    }

    private static IEnumerable<string> ParseSln(string slnPath)
    {
        var paths = new List<string>();
        var content = File.ReadAllText(slnPath);
        var matches = SlnProjectRegex.Matches(content);
        foreach (Match m in matches)
        {
            if (m.Success && m.Groups.Count > 1)
            {
                paths.Add(m.Groups[1].Value.Trim());
            }
        }
        return paths;
    }

    private static IEnumerable<string> ParseSlnx(string slnxPath)
    {
        var paths = new List<string>();
        var doc = XDocument.Load(slnxPath);
        var projects = doc.Descendants().Where(e => e.Name.LocalName.Equals("Project", StringComparison.OrdinalIgnoreCase));
        foreach (var p in projects)
        {
            var pathAttr = p.Attribute("Path")?.Value?.Trim();
            if (!string.IsNullOrEmpty(pathAttr))
            {
                paths.Add(pathAttr);
            }
        }
        return paths;
    }

    private bool InspectProjectFile(string projFilePath, ref string framework, out bool isRunnable, out bool isTest)
    {
        isRunnable = false;
        isTest = false;

        try
        {
            var fileInfo = new FileInfo(projFilePath);
            if (fileInfo.Length > MaxProjectFileSizeBytes)
            {
                return false;
            }

            var doc = XDocument.Load(projFilePath);
            var root = doc.Root;
            if (root == null)
            {
                return false;
            }

            var sdkAttr = root.Attribute("Sdk")?.Value ?? string.Empty;
            if (sdkAttr.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
            {
                framework = "ASP.NET Core";
                isRunnable = true;
            }
            else if (sdkAttr.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase))
            {
                isRunnable = true;
            }

            // Check TargetFramework
            var tf = root.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            if (!string.IsNullOrEmpty(tf))
            {
                framework = FormatTargetFramework(tf);
            }

            // Check OutputType: Exe or WinExe are runnable
            var outputType = root.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("OutputType", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase))
            {
                isRunnable = true;
            }

            // Check for Test SDK packages
            var packageRefs = root.Descendants().Where(e => e.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase))
                .Select(pr => pr.Attribute("Include")?.Value ?? string.Empty);

            foreach (var pkg in packageRefs)
            {
                if (pkg.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase) ||
                    pkg.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                    pkg.Contains("nunit", StringComparison.OrdinalIgnoreCase) ||
                    pkg.Contains("mstest", StringComparison.OrdinalIgnoreCase))
                {
                    isTest = true;
                    break;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse .NET project XML at '{Path}'", projFilePath);
            return false;
        }
    }

    private static string FormatTargetFramework(string tf)
    {
        if (tf.StartsWith("net", StringComparison.OrdinalIgnoreCase) && tf.Length >= 4 && char.IsDigit(tf[3]))
        {
            var ver = tf[3..];
            if (ver.Contains('.'))
            {
                return $".NET {ver}";
            }
            if (double.TryParse(ver, out var d))
            {
                return $".NET {d:0.0}";
            }
        }

        return tf;
    }

    private static string[] SafeGetFiles(string basePath, string searchPattern)
    {
        var results = new List<string>();
        try
        {
            foreach (var dir in Directory.GetDirectories(basePath))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith('.') || dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) || dirName.Equals("obj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.AddRange(Directory.GetFiles(dir, searchPattern, SearchOption.TopDirectoryOnly));
            }
        }
        catch
        {
            // Ignore directory read errors
        }

        return results.ToArray();
    }
}
