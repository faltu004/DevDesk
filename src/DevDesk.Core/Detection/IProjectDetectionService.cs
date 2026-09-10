namespace DevDesk.Core.Detection;

/// <summary>
/// Orchestration service coordinating project detectors and Git repository discovery.
/// </summary>
public interface IProjectDetectionService
{
    /// <summary>
    /// Evaluates the given project path against registered detectors and filesystem Git markers.
    /// </summary>
    Task<ProjectDetectionResult> DetectAsync(string projectPath, CancellationToken cancellationToken = default);
}
