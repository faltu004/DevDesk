namespace DevDesk.Core.Runner;

/// <summary>
/// Result of a project start or restart operation.
/// </summary>
public sealed record ProjectRunResult
{
    public required bool Success { get; init; }
    public ProjectRunSession? Session { get; init; }
    public string? ErrorMessage { get; init; }

    public static ProjectRunResult Succeeded(ProjectRunSession session) =>
        new() { Success = true, Session = session };

    public static ProjectRunResult Failed(string errorMessage, ProjectRunSession? session = null) =>
        new() { Success = false, ErrorMessage = errorMessage, Session = session };
}
