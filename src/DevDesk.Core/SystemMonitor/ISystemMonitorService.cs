namespace DevDesk.Core.SystemMonitor;

/// <summary>
/// Service contract coordinating authoritative host system telemetry sampling,
/// baseline lifecycle resets, and immutable snapshot generation.
/// </summary>
public interface ISystemMonitorService
{
    Task<SystemSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default);
    void ResetBaselines();
    SystemSnapshot? CurrentSnapshot { get; }
    event EventHandler<SystemSnapshot>? SnapshotUpdated;
}
