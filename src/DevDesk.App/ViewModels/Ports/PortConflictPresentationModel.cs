using DevDesk.Core.Ports;

namespace DevDesk.App.ViewModels.Ports;

/// <summary>
/// Presentation model wrapping a PortConflict for display in the PortsView conflict card.
/// </summary>
public sealed class PortConflictPresentationModel
{
    public PortConflict Conflict { get; }

    public int Port => Conflict.Port;

    public string PortDisplay => Port.ToString();

    public string Title => $"Port {Port} is already in use";

    public string Subtitle => Conflict.Description;

    public string ProcessName => Conflict.PrimaryOccupant.ProcessName;

    public int OwningProcessId => Conflict.PrimaryOccupant.OwningProcessId;

    public string ProcessIdDisplay => OwningProcessId.ToString();

    public string LocalAddress => Conflict.PrimaryOccupant.LocalAddress;

    public string AffectedProjectsDisplay => string.Join(", ", Conflict.AffectedProjectNames);

    public int AdditionalOccupantsCount => Conflict.AdditionalOccupantsCount;

    public bool HasAdditionalOccupants => AdditionalOccupantsCount > 0;

    public string AdditionalOccupantsText => HasAdditionalOccupants
        ? $"+{AdditionalOccupantsCount} other occupant(s)"
        : string.Empty;

    public PortConflictPresentationModel(PortConflict conflict)
    {
        Conflict = conflict;
    }
}
