using Microsoft.Extensions.Logging;
using DevDesk.Core.Ports;
using DevDesk.Core.Services;

namespace DevDesk.Infrastructure.Ports;

/// <summary>
/// Production service coordinating native TCP listener tables, safe process metadata resolution,
/// and registered project port conflict analysis.
/// </summary>
public sealed class PortService : IPortService
{
    private readonly IWindowsPortTableProvider _portTableProvider;
    private readonly IProcessMetadataProvider _processMetadataProvider;
    private readonly IProjectService _projectService;
    private readonly ILogger<PortService> _logger;

    internal PortService(
        IWindowsPortTableProvider portTableProvider,
        IProcessMetadataProvider processMetadataProvider,
        IProjectService projectService,
        ILogger<PortService> logger)
    {
        _portTableProvider = portTableProvider;
        _processMetadataProvider = processMetadataProvider;
        _projectService = projectService;
        _logger = logger;
    }

    public async Task<PortSnapshot> GetPortSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Ensure all native table querying, PID resolution, and project comparison
        // executes on a controlled background thread without blocking the WPF UI thread.
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Query registered projects for configured default ports
            var projects = await _projectService.GetProjectsAsync();
            var configuredProjectsByPort = new Dictionary<int, List<DevDesk.Core.Models.DeveloperProject>>();

            foreach (var proj in projects)
            {
                if (proj.DefaultPort.HasValue && proj.DefaultPort.Value > 0)
                {
                    if (!configuredProjectsByPort.TryGetValue(proj.DefaultPort.Value, out var list))
                    {
                        list = new List<DevDesk.Core.Models.DeveloperProject>();
                        configuredProjectsByPort[proj.DefaultPort.Value] = list;
                    }

                    list.Add(proj);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 2. Query Windows native TCP listener table
            var tableResult = _portTableProvider.GetListeners(cancellationToken);
            if (!tableResult.Success)
            {
                _logger.LogWarning("Failed to acquire Windows port table: {Error}", tableResult.ErrorMessage);
                return new PortSnapshot
                {
                    Success = false,
                    ErrorMessage = tableResult.ErrorMessage ?? "Failed to query Windows TCP listeners.",
                    ConfiguredProjectPortsCount = configuredProjectsByPort.Values.Sum(l => l.Count),
                    Timestamp = DateTimeOffset.UtcNow
                };
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Cache process name lookups across rows to minimize repeated process table queries
            var processNameCache = new Dictionary<int, string>();

            var portEntries = new List<PortEntry>(tableResult.Listeners.Count);
            var listenersByPort = new Dictionary<int, List<(RawTcpListener Listener, string ProcessName)>>();

            for (int i = 0; i < tableResult.Listeners.Count; i++)
            {
                if (i % 50 == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var raw = tableResult.Listeners[i];

                if (!processNameCache.TryGetValue(raw.OwningProcessId, out var procName))
                {
                    procName = _processMetadataProvider.GetProcessName(raw.OwningProcessId);
                    processNameCache[raw.OwningProcessId] = procName;
                }

                var hasConflict = configuredProjectsByPort.TryGetValue(raw.LocalPort, out var matchingProjects);
                var configuredNames = hasConflict && matchingProjects is not null
                    ? matchingProjects.Select(p => p.Name).ToList()
                    : (IReadOnlyList<string>)Array.Empty<string>();

                portEntries.Add(new PortEntry
                {
                    Port = raw.LocalPort,
                    Protocol = "TCP",
                    LocalAddress = raw.LocalAddress,
                    OwningProcessId = raw.OwningProcessId,
                    ProcessName = procName,
                    State = "Listening",
                    IsConfiguredProjectPort = hasConflict,
                    HasPotentialConflict = hasConflict,
                    ConfiguredProjectNames = configuredNames
                });

                if (!listenersByPort.TryGetValue(raw.LocalPort, out var listenerList))
                {
                    listenerList = new List<(RawTcpListener, string)>();
                    listenersByPort[raw.LocalPort] = listenerList;
                }

                listenerList.Add((raw, procName));
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 3. Evaluate potential conflicts for configured project ports
            var conflicts = new List<PortConflict>();
            int inUseCount = 0;

            foreach (var (configuredPort, matchingProjs) in configuredProjectsByPort)
            {
                if (listenersByPort.TryGetValue(configuredPort, out var portListeners) && portListeners.Count > 0)
                {
                    inUseCount++;

                    // Deduplicate occupants by PID and address
                    var occupants = portListeners
                        .Select(pl => new PortOccupant
                        {
                            OwningProcessId = pl.Listener.OwningProcessId,
                            ProcessName = pl.ProcessName,
                            LocalAddress = pl.Listener.LocalAddress
                        })
                        .DistinctBy(o => (o.OwningProcessId, o.LocalAddress))
                        .ToList();

                    var primary = occupants[0];
                    var additionalCount = occupants.Count - 1;

                    var description = additionalCount > 0
                        ? $"Port {configuredPort} is currently in use by {primary.ProcessName} (PID {primary.OwningProcessId}) and {additionalCount} other listener(s)."
                        : $"Port {configuredPort} is currently in use by {primary.ProcessName} (PID {primary.OwningProcessId}).";

                    conflicts.Add(new PortConflict
                    {
                        Port = configuredPort,
                        AffectedProjectIds = matchingProjs.Select(p => p.Id).ToList(),
                        AffectedProjectNames = matchingProjs.Select(p => p.Name).ToList(),
                        PrimaryOccupant = primary,
                        AllOccupants = occupants,
                        Description = description
                    });
                }
            }

            var uniqueListeningPortsCount = listenersByPort.Keys.Count;
            var totalConfiguredProjectsWithPorts = configuredProjectsByPort.Values.Sum(l => l.Count);
            var freePortsCount = Math.Max(0, configuredProjectsByPort.Count - inUseCount);

            return new PortSnapshot
            {
                ListeningPorts = portEntries,
                PotentialConflicts = conflicts,
                TotalListeningPortsCount = uniqueListeningPortsCount,
                ConfiguredProjectPortsCount = totalConfiguredProjectsWithPorts,
                InUseProjectPortsCount = inUseCount,
                FreeConfiguredPortsCount = freePortsCount,
                Success = true,
                HasPartialFailure = tableResult.HasPartialFailure,
                WarningMessage = tableResult.WarningMessage,
                Timestamp = DateTimeOffset.UtcNow
            };
        }, cancellationToken);
    }
}
