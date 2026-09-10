using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using DevDesk.Core.Detection;

namespace DevDesk.Infrastructure.Detection.Detectors;

/// <summary>
/// Detects Node.js, JavaScript, TypeScript, Next.js, Vite, and React projects by inspecting package.json,
/// lockfiles, and tsconfig.json without spawning external processes.
/// </summary>
public sealed class NodeProjectDetector : IProjectDetector
{
    private const long MaxManifestSizeBytes = 1_048_576; // 1 MB safety limit
    private readonly ILogger<NodeProjectDetector> _logger;

    public int Order => 10; // High precedence

    public NodeProjectDetector(ILogger<NodeProjectDetector> logger)
    {
        _logger = logger;
    }

    public async Task<ProjectDetectionResult?> DetectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            return null;
        }

        var packageJsonPath = Path.Combine(projectPath, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            return null;
        }

        var markers = new List<string> { "package.json" };

        JsonDocument document;
        try
        {
            var fileInfo = new FileInfo(packageJsonPath);
            if (fileInfo.Length > MaxManifestSizeBytes)
            {
                _logger.LogWarning("Skipping package.json detection in '{Path}' because file size ({Size} bytes) exceeds limit", projectPath, fileInfo.Length);
                return null;
            }

            var content = await File.ReadAllTextAsync(packageJsonPath, cancellationToken);
            document = JsonDocument.Parse(content);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read or parse package.json at '{Path}'", packageJsonPath);
            // Malformed manifest must not crash the application
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            var dependencies = ExtractDependencies(root);

            // 1. Framework Detection (Precedence: Next.js > Vite > React > Node.js)
            string framework;
            if (dependencies.ContainsKey("next"))
            {
                framework = "Next.js";
                markers.Add("dep:next");
            }
            else if (dependencies.ContainsKey("vite"))
            {
                framework = "Vite";
                markers.Add("dep:vite");
            }
            else if (dependencies.ContainsKey("react"))
            {
                framework = "React";
                markers.Add("dep:react");
            }
            else
            {
                framework = "Node.js";
            }

            // 2. Language Detection
            var tsConfigPath = Path.Combine(projectPath, "tsconfig.json");
            string language;
            if (File.Exists(tsConfigPath) || dependencies.ContainsKey("typescript"))
            {
                language = "TypeScript";
                if (File.Exists(tsConfigPath))
                {
                    markers.Add("tsconfig.json");
                }
                else
                {
                    markers.Add("dep:typescript");
                }
            }
            else
            {
                language = "JavaScript";
            }

            // 3. Package Manager Detection
            var (packageManager, lockMarker) = DetectPackageManager(projectPath);
            if (!string.IsNullOrEmpty(lockMarker))
            {
                markers.Add(lockMarker);
            }

            // 4. Command Suggestions from scripts
            var scripts = ExtractScripts(root);
            string? suggestedRun = null;
            string? suggestedBuild = null;
            string? suggestedTest = null;

            if (scripts.TryGetValue("dev", out _))
            {
                suggestedRun = FormatScriptCommand(packageManager, "dev");
            }
            else if (scripts.TryGetValue("start", out _))
            {
                suggestedRun = FormatScriptCommand(packageManager, "start");
            }

            if (scripts.TryGetValue("build", out _))
            {
                suggestedBuild = FormatScriptCommand(packageManager, "build");
            }

            if (scripts.TryGetValue("test", out _))
            {
                suggestedTest = FormatTestCommand(packageManager);
            }

            return new ProjectDetectionResult
            {
                Framework = framework,
                Language = language,
                PackageManager = packageManager,
                SuggestedRunCommand = suggestedRun,
                SuggestedBuildCommand = suggestedBuild,
                SuggestedTestCommand = suggestedTest,
                SuggestedDefaultPort = null, // Do NOT assume default ports
                DetectionMarkers = markers
            };
        }
    }

    private static (string PackageManager, string? LockMarker) DetectPackageManager(string projectPath)
    {
        if (File.Exists(Path.Combine(projectPath, "pnpm-lock.yaml")))
        {
            return ("pnpm", "pnpm-lock.yaml");
        }

        if (File.Exists(Path.Combine(projectPath, "yarn.lock")))
        {
            return ("Yarn", "yarn.lock");
        }

        if (File.Exists(Path.Combine(projectPath, "bun.lock")) || File.Exists(Path.Combine(projectPath, "bun.lockb")))
        {
            var marker = File.Exists(Path.Combine(projectPath, "bun.lock")) ? "bun.lock" : "bun.lockb";
            return ("Bun", marker);
        }

        if (File.Exists(Path.Combine(projectPath, "package-lock.json")))
        {
            return ("npm", "package-lock.json");
        }

        // Documented fallback for Node ecosystem without lockfile
        return ("npm", null);
    }

    private static string FormatScriptCommand(string packageManager, string scriptName) => packageManager switch
    {
        "pnpm" => $"pnpm {scriptName}",
        "Yarn" => $"yarn {scriptName}",
        "Bun" => $"bun run {scriptName}",
        _ => $"npm run {scriptName}"
    };

    private static string FormatTestCommand(string packageManager) => packageManager switch
    {
        "pnpm" => "pnpm test",
        "Yarn" => "yarn test",
        "Bun" => "bun run test",
        _ => "npm test"
    };

    private static Dictionary<string, string> ExtractDependencies(JsonElement root)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddProps(root, "dependencies", dict);
        AddProps(root, "devDependencies", dict);

        return dict;

        static void AddProps(JsonElement element, string propName, Dictionary<string, string> target)
        {
            if (element.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.Object)
            {
                foreach (var child in prop.EnumerateObject())
                {
                    target[child.Name] = child.Value.GetString() ?? string.Empty;
                }
            }
        }
    }

    private static Dictionary<string, string> ExtractScripts(JsonElement root)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in scripts.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
        }

        return dict;
    }
}
