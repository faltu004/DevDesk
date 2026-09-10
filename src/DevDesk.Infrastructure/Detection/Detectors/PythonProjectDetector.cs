using System.IO;
using Microsoft.Extensions.Logging;
using DevDesk.Core.Detection;

namespace DevDesk.Infrastructure.Detection.Detectors;

/// <summary>
/// Detects Python projects by inspecting static manifests (pyproject.toml, requirements.txt, Pipfile, lockfiles).
/// </summary>
public sealed class PythonProjectDetector : IProjectDetector
{
    private const long MaxFileSize = 524_288; // 512 KB
    private readonly ILogger<PythonProjectDetector> _logger;

    public int Order => 30;

    public PythonProjectDetector(ILogger<PythonProjectDetector> logger)
    {
        _logger = logger;
    }

    public async Task<ProjectDetectionResult?> DetectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            return null;
        }

        var pyprojectPath = Path.Combine(projectPath, "pyproject.toml");
        var requirementsPath = Path.Combine(projectPath, "requirements.txt");
        var pipfilePath = Path.Combine(projectPath, "Pipfile");
        var poetryLockPath = Path.Combine(projectPath, "poetry.lock");
        var uvLockPath = Path.Combine(projectPath, "uv.lock");
        var setupPyPath = Path.Combine(projectPath, "setup.py");

        var hasPyproject = File.Exists(pyprojectPath);
        var hasRequirements = File.Exists(requirementsPath);
        var hasPipfile = File.Exists(pipfilePath);
        var hasPoetryLock = File.Exists(poetryLockPath);
        var hasUvLock = File.Exists(uvLockPath);
        var hasSetupPy = File.Exists(setupPyPath);

        if (!hasPyproject && !hasRequirements && !hasPipfile && !hasPoetryLock && !hasUvLock && !hasSetupPy)
        {
            return null;
        }

        var markers = new List<string>();
        if (hasPyproject) markers.Add("pyproject.toml");
        if (hasRequirements) markers.Add("requirements.txt");
        if (hasPipfile) markers.Add("Pipfile");
        if (hasPoetryLock) markers.Add("poetry.lock");
        if (hasUvLock) markers.Add("uv.lock");
        if (hasSetupPy) markers.Add("setup.py");

        string? pyprojectText = null;
        if (hasPyproject)
        {
            try
            {
                var fi = new FileInfo(pyprojectPath);
                if (fi.Length <= MaxFileSize)
                {
                    pyprojectText = await File.ReadAllTextAsync(pyprojectPath, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read pyproject.toml at '{Path}'", pyprojectPath);
            }
        }

        // Package Manager Detection (uv > Poetry > Pipenv > pip)
        string packageManager;
        if (hasUvLock)
        {
            packageManager = "uv";
        }
        else if (hasPoetryLock || (pyprojectText != null && pyprojectText.Contains("[tool.poetry]", StringComparison.OrdinalIgnoreCase)))
        {
            packageManager = "Poetry";
        }
        else if (hasPipfile || File.Exists(Path.Combine(projectPath, "Pipfile.lock")))
        {
            packageManager = "Pipenv";
        }
        else
        {
            packageManager = "pip";
        }

        // Framework Detection (FastAPI > Django > Flask > Python)
        string framework = "Python";
        var contentToSearch = (pyprojectText ?? string.Empty);
        if (hasRequirements && contentToSearch.Length < 100_000)
        {
            try
            {
                contentToSearch += " " + await File.ReadAllTextAsync(requirementsPath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read requirements.txt at '{Path}'", requirementsPath);
            }
        }

        if (contentToSearch.Contains("fastapi", StringComparison.OrdinalIgnoreCase))
        {
            framework = "FastAPI";
        }
        else if (contentToSearch.Contains("django", StringComparison.OrdinalIgnoreCase))
        {
            framework = "Django";
        }
        else if (contentToSearch.Contains("flask", StringComparison.OrdinalIgnoreCase))
        {
            framework = "Flask";
        }

        return new ProjectDetectionResult
        {
            Framework = framework,
            Language = "Python",
            PackageManager = packageManager,
            SuggestedRunCommand = null,
            SuggestedBuildCommand = null,
            SuggestedTestCommand = null,
            SuggestedDefaultPort = null,
            DetectionMarkers = markers
        };
    }
}
