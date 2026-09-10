using System.Diagnostics;

namespace DevDesk.Infrastructure.Launchers;

/// <summary>
/// Internal boundary isolating Process.Start for automated testing without spawning real Windows processes.
/// </summary>
internal interface IProcessRunner
{
    bool Start(ProcessStartInfo startInfo);
}
