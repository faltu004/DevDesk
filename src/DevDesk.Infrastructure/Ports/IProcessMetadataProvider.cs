namespace DevDesk.Infrastructure.Ports;

/// <summary>
/// Internal abstraction isolating process identity resolution for unit testing.
/// </summary>
internal interface IProcessMetadataProvider
{
    /// <summary>
    /// Resolves the process name for the specified PID, safely falling back to "System" or "Unknown"
    /// without privileged queries.
    /// </summary>
    string GetProcessName(int pid);
}
