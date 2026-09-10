namespace DevDesk.Core.Detection;

/// <summary>
/// Immutable typed result of static project auto-detection.
/// </summary>
public sealed record ProjectDetectionResult
{
    public string? Framework { get; init; }
    public string? Language { get; init; }
    public string? PackageManager { get; init; }
    public bool IsGitRepository { get; init; }

    public string? SuggestedRunCommand { get; init; }
    public string? SuggestedBuildCommand { get; init; }
    public string? SuggestedTestCommand { get; init; }
    public int? SuggestedDefaultPort { get; init; }

    public IReadOnlyList<string> DetectionMarkers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Indicates whether a supported development project ecosystem was recognized.
    /// Notice: Git repository presence alone does NOT mark a project as a recognized framework/type.
    /// </summary>
    public bool IsRecognized =>
        !string.IsNullOrWhiteSpace(Framework) ||
        !string.IsNullOrWhiteSpace(Language) ||
        !string.IsNullOrWhiteSpace(PackageManager);

    public static ProjectDetectionResult Empty => new();
}
