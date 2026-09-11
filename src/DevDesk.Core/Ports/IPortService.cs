namespace DevDesk.Core.Ports;

/// <summary>
/// Service contract for non-destructive, read-only TCP listener inspection and project port conflict analysis.
/// </summary>
public interface IPortService
{
    /// <summary>
    /// Asynchronously captures a read-only snapshot of all active TCP listening ports and detects potential project port conflicts.
    /// </summary>
    Task<PortSnapshot> GetPortSnapshotAsync(CancellationToken cancellationToken = default);
}
