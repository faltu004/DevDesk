using System.ComponentModel;
using System.Diagnostics;

namespace DevDesk.Infrastructure.Ports;

/// <summary>
/// Production process metadata provider inspecting only non-privileged Process.ProcessName.
/// </summary>
internal sealed class SystemProcessMetadataProvider : IProcessMetadataProvider
{
    public string GetProcessName(int pid)
    {
        if (pid == 0)
        {
            return "System Idle Process";
        }

        if (pid == 4)
        {
            return "System";
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            var name = process.ProcessName;
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Unknown";
            }

            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        }
        catch (ArgumentException)
        {
            // Process terminated between port snapshot and PID lookup
            return "Unknown";
        }
        catch (InvalidOperationException)
        {
            return "Unknown";
        }
        catch (Win32Exception)
        {
            // Access denied for protected/system process
            return "Protected Process";
        }
        catch (Exception)
        {
            return "Unknown";
        }
    }
}
