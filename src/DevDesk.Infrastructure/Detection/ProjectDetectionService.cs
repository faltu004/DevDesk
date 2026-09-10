using Microsoft.Extensions.Logging;
using DevDesk.Core.Detection;

namespace DevDesk.Infrastructure.Detection;

/// <summary>
/// Orchestrates project detectors and Git repository discovery in a strictly read-only manner.
/// </summary>
public sealed class ProjectDetectionService : IProjectDetectionService
{
    private readonly IReadOnlyList<IProjectDetector> _detectors;
    private readonly ILogger<ProjectDetectionService> _logger;

    public ProjectDetectionService(
        IEnumerable<IProjectDetector> detectors,
        ILogger<ProjectDetectionService> logger)
    {
        _detectors = detectors.OrderBy(d => d.Order).ToList();
        _logger = logger;
    }

    public async Task<ProjectDetectionResult> DetectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return ProjectDetectionResult.Empty;
        }

        var isGit = GitDetector.DetectIsGitRepository(projectPath);

        foreach (var detector in _detectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await detector.DetectAsync(projectPath, cancellationToken);
                if (result != null && result.IsRecognized)
                {
                    _logger.LogInformation("Detector '{Detector}' matched project at '{Path}' as '{Framework}' ({Language})",
                        detector.GetType().Name, projectPath, result.Framework, result.Language);

                    return result with
                    {
                        IsGitRepository = isGit
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Detector '{Detector}' failed while inspecting '{Path}'",
                    detector.GetType().Name, projectPath);
            }
        }

        // Unknown/unrecognized development project folder (IsGitRepository is independent metadata)
        return new ProjectDetectionResult
        {
            IsGitRepository = isGit
        };
    }
}
