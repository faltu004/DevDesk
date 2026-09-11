namespace DevDesk.Infrastructure.Git;

/// <summary>
/// Result of executing a Git command.
/// </summary>
public sealed record GitProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool IsTimedOut = false,
    bool IsOutputTruncated = false)
{
    public bool IsSuccess => ExitCode == 0 && !IsTimedOut && !IsOutputTruncated;
}
