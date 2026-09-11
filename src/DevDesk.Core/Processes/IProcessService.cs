namespace DevDesk.Core.Processes;

/// <summary>
/// Service contract for non-destructive, read-only Windows process inspection,
/// truthful CPU sampling, and authoritative DevDesk runner ownership correlation.
/// </summary>
public interface IProcessService
{
    /// <summary>
    /// Asynchronously captures an immutable snapshot of visible Windows processes,
    /// calculates monotonic CPU deltas against verified baselines, and links active DevDesk runner sessions.
    /// </summary>
    Task<ProcessSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lazily resolves the executable path for a specific process identity without elevation.
    /// Cached strictly by verified ProcessKey; never cached for unverified/recycled PIDs.
    /// Returns null if access is denied or process has exited.
    /// </summary>
    Task<string?> ResolveExecutablePathAsync(ProcessKey key, CancellationToken cancellationToken = default);
}
