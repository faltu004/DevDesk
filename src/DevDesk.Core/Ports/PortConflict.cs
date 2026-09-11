namespace DevDesk.Core.Ports;

/// <summary>
/// Represents an identified process occupying a TCP port.
/// </summary>
public sealed class PortOccupant
{
    public int OwningProcessId { get; init; }

    public string ProcessName { get; init; } = "Unknown";

    public string LocalAddress { get; init; } = string.Empty;
}

/// <summary>
/// Represents a potential conflict where one or more registered projects have a DefaultPort that is currently occupied on the machine.
/// </summary>
public sealed class PortConflict
{
    public int Port { get; init; }

    public IReadOnlyList<Guid> AffectedProjectIds { get; init; } = Array.Empty<Guid>();

    public IReadOnlyList<string> AffectedProjectNames { get; init; } = Array.Empty<string>();

    public PortOccupant PrimaryOccupant { get; init; } = null!;

    public IReadOnlyList<PortOccupant> AllOccupants { get; init; } = Array.Empty<PortOccupant>();

    public int AdditionalOccupantsCount => Math.Max(0, AllOccupants.Count - 1);

    public string Description { get; init; } = string.Empty;
}
