namespace DevDesk.Core.Launchers;

/// <summary>
/// Represents the typed outcome of an external developer launcher operation.
/// </summary>
public sealed class LaunchResult
{
    public bool Success { get; }

    public string? ErrorMessage { get; }

    public LaunchFailureReason? FailureReason { get; }

    private LaunchResult(bool success, string? errorMessage, LaunchFailureReason? failureReason)
    {
        Success = success;
        ErrorMessage = errorMessage;
        FailureReason = failureReason;
    }

    public static LaunchResult Ok() => new(true, null, null);

    public static LaunchResult Failed(LaunchFailureReason reason, string errorMessage) =>
        new(false, errorMessage, reason);
}

/// <summary>
/// Categorized reason why an external launcher operation failed.
/// </summary>
public enum LaunchFailureReason
{
    InvalidPath,
    DirectoryNotFound,
    ApplicationNotFound,
    ProcessStartFailed
}
