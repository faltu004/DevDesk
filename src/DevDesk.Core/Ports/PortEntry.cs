namespace DevDesk.Core.Ports;

/// <summary>
/// Represents an active TCP listening port enumerated from the local machine.
/// </summary>
public sealed class PortEntry
{
    public int Port { get; init; }

    public string Protocol { get; init; } = "TCP";

    public string LocalAddress { get; init; } = string.Empty;

    public int OwningProcessId { get; init; }

    public string ProcessName { get; init; } = "Unknown";

    public string State { get; init; } = "Listening";

    public bool IsConfiguredProjectPort { get; init; }

    public bool HasPotentialConflict { get; init; }

    public IReadOnlyList<string> ConfiguredProjectNames { get; init; } = Array.Empty<string>();
}
