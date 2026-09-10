using System.Diagnostics;

namespace DevDesk.Infrastructure.Launchers;

/// <summary>
/// Default production process runner delegating to System.Diagnostics.Process.Start.
/// </summary>
internal sealed class SystemProcessRunner : IProcessRunner
{
    public bool Start(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        return process is not null;
    }
}
