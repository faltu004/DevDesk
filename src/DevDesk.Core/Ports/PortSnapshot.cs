namespace DevDesk.Core.Ports;

/// <summary>
/// Point-in-time snapshot of machine TCP listeners, factual metrics, and potential project port conflicts.
/// </summary>
public sealed class PortSnapshot
{
    public IReadOnlyList<PortEntry> ListeningPorts { get; init; } = Array.Empty<PortEntry>();

    public IReadOnlyList<PortConflict> PotentialConflicts { get; init; } = Array.Empty<PortConflict>();

    /// <summary>
    /// Total count of unique TCP listening ports discovered on the local machine.
    /// </summary>
    public int TotalListeningPortsCount { get; init; }

    /// <summary>
    /// Total count of registered projects configured with a DefaultPort.
    /// </summary>
    public int ConfiguredProjectPortsCount { get; init; }

    /// <summary>
    /// Count of configured project ports currently occupied on the machine.
    /// </summary>
    public int InUseProjectPortsCount { get; init; }

    /// <summary>
    /// Count of configured project ports that are currently free and unassigned to any listener.
    /// </summary>
    public int FreeConfiguredPortsCount { get; init; }

    public bool Success { get; init; } = true;

    public bool HasPartialFailure { get; init; }

    public string? WarningMessage { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
