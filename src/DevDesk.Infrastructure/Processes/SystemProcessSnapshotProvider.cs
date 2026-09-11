using System.ComponentModel;
using System.Diagnostics;
using DevDesk.Core.Processes;

namespace DevDesk.Infrastructure.Processes;

/// <summary>
/// Production implementation of IProcessSnapshotProvider using .NET System.Diagnostics.Process APIs.
/// Safely insulates against access-denied exceptions on protected processes, handles process exits,
/// and guarantees prompt handle disposal for every enumerated Process object.
/// </summary>
public sealed class SystemProcessSnapshotProvider : IProcessSnapshotProvider
{
    private long _ephemeralCounter;

    public IReadOnlyList<RawProcessEntry> EnumerateProcesses()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return Array.Empty<RawProcessEntry>();
        }

        var results = new List<RawProcessEntry>(processes.Length);

        foreach (var p in processes)
        {
            try
            {
                int pid = p.Id;
                string name;
                try
                {
                    name = p.ProcessName;
                }
                catch
                {
                    name = "Unknown";
                }

                // Query StartTime safely
                DateTimeOffset? startTimeUtc = null;
                ProcessKey key;
                try
                {
                    var st = p.StartTime;
                    startTimeUtc = new DateTimeOffset(st.ToUniversalTime(), TimeSpan.Zero);
                    key = ProcessKey.CreateVerified(pid, startTimeUtc.Value);
                }
                catch
                {
                    long ephemeral = Interlocked.Increment(ref _ephemeralCounter);
                    key = ProcessKey.CreateUnverified(pid, ephemeral);
                }

                // Query TotalProcessorTime safely
                TimeSpan? totalProcessorTime = null;
                try
                {
                    totalProcessorTime = p.TotalProcessorTime;
                }
                catch
                {
                    // Access denied or process exited
                }

                // Query WorkingSet64 safely
                long? workingSetBytes = null;
                try
                {
                    workingSetBytes = p.WorkingSet64;
                }
                catch
                {
                    // Access denied or process exited
                }

                // Query Responding safely (only meaningful for GUI windows)
                bool? isResponding = null;
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        isResponding = p.Responding;
                    }
                }
                catch
                {
                    // Access denied or not applicable
                }

                results.Add(new RawProcessEntry
                {
                    Key = key,
                    ProcessId = pid,
                    ProcessName = name,
                    TotalProcessorTime = totalProcessorTime,
                    WorkingSetBytes = workingSetBytes,
                    StartTime = startTimeUtc,
                    IsResponding = isResponding,
                    ExecutablePath = null // Resolved lazily for details/selection
                });
            }
            catch
            {
                // Process may have exited between array allocation and inspection; skip cleanly
            }
            finally
            {
                p.Dispose();
            }
        }

        return results;
    }

    public string? TryResolveExecutablePath(int processId, DateTimeOffset? expectedStartTimeUtc)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);

            // If we have an expected start time, verify it to prevent PID recycling mismatch
            if (expectedStartTimeUtc.HasValue)
            {
                try
                {
                    var actualStartUtc = process.StartTime.ToUniversalTime();
                    var diff = (actualStartUtc - expectedStartTimeUtc.Value.UtcDateTime).Duration();
                    if (diff > TimeSpan.FromSeconds(2))
                    {
                        // PID was recycled by a different process
                        return null;
                    }
                }
                catch
                {
                    return null;
                }
            }

            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }
}
