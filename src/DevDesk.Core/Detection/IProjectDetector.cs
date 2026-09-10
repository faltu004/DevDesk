namespace DevDesk.Core.Detection;

/// <summary>
/// Contract for an ecosystem-specific project detector that inspects static filesystem markers.
/// </summary>
public interface IProjectDetector
{
    /// <summary>
    /// Gets the evaluation order for this detector. Lower numbers execute first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Detects project characteristics from filesystem markers in a single read-only operation.
    /// Returns null or an empty result if the project ecosystem is not recognized.
    /// </summary>
    Task<ProjectDetectionResult?> DetectAsync(string projectPath, CancellationToken cancellationToken = default);
}
