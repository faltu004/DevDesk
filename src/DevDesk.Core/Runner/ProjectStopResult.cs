namespace DevDesk.Core.Runner;

/// <summary>
/// Result of a project stop operation.
/// </summary>
public sealed record ProjectStopResult
{
    public required bool Success { get; init; }
    public Guid ProjectId { get; init; }
    public string? ErrorMessage { get; init; }

    public static ProjectStopResult Succeeded(Guid projectId) =>
        new() { Success = true, ProjectId = projectId };

    public static ProjectStopResult Failed(Guid projectId, string errorMessage) =>
        new() { Success = false, ProjectId = projectId, ErrorMessage = errorMessage };
}
