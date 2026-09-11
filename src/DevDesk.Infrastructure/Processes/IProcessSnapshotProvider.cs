namespace DevDesk.Infrastructure.Processes;

/// <summary>
/// Abstraction isolating direct operating system process enumeration and module queries.
/// Allows deterministic unit and regression testing without touching real Windows kernel processes.
/// </summary>
public interface IProcessSnapshotProvider
{
    /// <summary>
    /// Enumerates currently active processes from the operating system, capturing cheap polling telemetry.
    /// Every underlying system process handle must be promptly disposed.
    /// </summary>
    IReadOnlyList<RawProcessEntry> EnumerateProcesses();

    /// <summary>
    /// Attempts to query the executable file path for a process without elevation.
    /// Verifies start time when available to ensure the query targets the intended process generation.
    /// Returns null if access is denied or the process has exited.
    /// </summary>
    string? TryResolveExecutablePath(int processId, DateTimeOffset? expectedStartTimeUtc);
}
