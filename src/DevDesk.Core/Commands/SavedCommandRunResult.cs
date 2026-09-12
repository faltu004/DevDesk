namespace DevDesk.Core.Commands;

/// <summary>
/// Outcome of initiating execution of a saved command.
/// </summary>
public sealed record SavedCommandRunResult
{
    public required bool Success { get; init; }
    public Guid? SessionId { get; init; }
    public int? ProcessId { get; init; }
    public string? ErrorMessage { get; init; }

    public static SavedCommandRunResult Succeeded(Guid sessionId, int processId) =>
        new() { Success = true, SessionId = sessionId, ProcessId = processId };

    public static SavedCommandRunResult Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}
