using DevDesk.Core.Ports;

namespace DevDesk.App.ViewModels.Ports;

/// <summary>
/// Presentation model wrapping a PortEntry for data-bound display in the PortsView table.
/// </summary>
public sealed class PortItemPresentationModel
{
    public PortEntry Entry { get; }

    public int Port => Entry.Port;

    public string PortDisplay => Port.ToString();

    public string Protocol => Entry.Protocol;

    public string LocalAddress => Entry.LocalAddress;

    public int OwningProcessId => Entry.OwningProcessId;

    public string ProcessIdDisplay => OwningProcessId.ToString();

    public string ProcessName => Entry.ProcessName;

    public string State => Entry.State;

    public bool IsConflict => Entry.HasPotentialConflict;

    public string StateDisplay => IsConflict ? "In Use" : "Listening";

    public bool HasConfiguredProjects => Entry.ConfiguredProjectNames.Count > 0;

    public string ProjectDisplay => HasConfiguredProjects
        ? string.Join(", ", Entry.ConfiguredProjectNames)
        : "—";

    public string ProcessInitials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ProcessName) || ProcessName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return "?";
            }

            var clean = ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? ProcessName[..^4]
                : ProcessName;

            return clean.Length <= 2 ? clean.ToUpperInvariant() : clean[..2].ToUpperInvariant();
        }
    }

    public PortItemPresentationModel(PortEntry entry)
    {
        Entry = entry;
    }
}
