using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DevDesk.Infrastructure.Runner.Native;

/// <summary>
/// Safe Windows Job Object wrapper ensuring strict process-tree ownership and atomic termination
/// of all descendant processes spawned during a project run session (e.g. dotnet run -> app.exe).
/// </summary>
internal sealed class WindowsJobObject : IDisposable
{
    private IntPtr _jobHandle;
    private bool _disposed;

    private const int JobObjectBasicAccountingInformation = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        IntPtr hJob,
        int JobObjectInformationClass,
        IntPtr lpJobObjectInformation,
        uint cbJobObjectInformationLength,
        out uint lpReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(
        IntPtr ProcessHandle,
        IntPtr JobHandle,
        [MarshalAs(UnmanagedType.Bool)] out bool Result);

    private WindowsJobObject(IntPtr handle)
    {
        _jobHandle = handle;
    }

    public static WindowsJobObject? Create()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        return new WindowsJobObject(handle);
    }

    public bool AssignProcess(Process process)
    {
        if (_disposed || _jobHandle == IntPtr.Zero || !OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            if (process.HasExited)
            {
                return false;
            }

            return AssignProcessToJobObject(_jobHandle, process.Handle);
        }
        catch
        {
            return false;
        }
    }

    public bool AssignProcessHandle(IntPtr hProcess)
    {
        if (_disposed || _jobHandle == IntPtr.Zero || !OperatingSystem.IsWindows() || hProcess == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return AssignProcessToJobObject(_jobHandle, hProcess);
        }
        catch
        {
            return false;
        }
    }

    public bool Terminate(uint exitCode = 1)
    {
        if (_disposed || _jobHandle == IntPtr.Zero || !OperatingSystem.IsWindows())
        {
            return false;
        }

        return TerminateJobObject(_jobHandle, exitCode);
    }

    public uint GetActiveProcessCount()
    {
        if (_disposed || _jobHandle == IntPtr.Zero || !OperatingSystem.IsWindows())
        {
            return 0;
        }

        int size = Marshal.SizeOf<JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (QueryInformationJobObject(_jobHandle, JobObjectBasicAccountingInformation, buffer, (uint)size, out _))
            {
                var info = Marshal.PtrToStructure<JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>(buffer);
                return info.ActiveProcesses;
            }

            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int JobObjectBasicProcessIdList = 3;
    private const int ERROR_MORE_DATA = 234;

    /// <summary>
    /// Enumerates the process IDs of all active processes currently assigned to this Job Object.
    /// Uses JOBOBJECT_BASIC_PROCESS_ID_LIST via QueryInformationJobObject.
    /// </summary>
    public IReadOnlyList<int> GetProcessIds()
    {
        if (_disposed || _jobHandle == IntPtr.Zero || !OperatingSystem.IsWindows())
        {
            return Array.Empty<int>();
        }

        int maxPids = 64;
        int bufferSize = sizeof(uint) * 2 + IntPtr.Size * maxPids;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            if (!QueryInformationJobObject(_jobHandle, JobObjectBasicProcessIdList, buffer, (uint)bufferSize, out _))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ERROR_MORE_DATA)
                {
                    uint assigned = (uint)Marshal.ReadInt32(buffer);
                    if (assigned > 0)
                    {
                        Marshal.FreeHGlobal(buffer);
                        maxPids = (int)assigned + 16;
                        bufferSize = sizeof(uint) * 2 + IntPtr.Size * maxPids;
                        buffer = Marshal.AllocHGlobal(bufferSize);

                        if (!QueryInformationJobObject(_jobHandle, JobObjectBasicProcessIdList, buffer, (uint)bufferSize, out _))
                        {
                            return Array.Empty<int>();
                        }
                    }
                    else
                    {
                        return Array.Empty<int>();
                    }
                }
                else
                {
                    return Array.Empty<int>();
                }
            }

            uint count = (uint)Marshal.ReadInt32(buffer, sizeof(uint));
            if (count == 0)
            {
                return Array.Empty<int>();
            }

            var result = new List<int>((int)count);
            IntPtr pList = IntPtr.Add(buffer, sizeof(uint) * 2);

            for (int i = 0; i < count; i++)
            {
                IntPtr pidPtr = Marshal.ReadIntPtr(pList, i * IntPtr.Size);
                result.Add((int)pidPtr);
            }

            return result;
        }
        catch
        {
            return Array.Empty<int>();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Verifies at the OS process handle level whether a specific process belongs to this Job Object.
    /// This eliminates TOCTOU races where a PID was enumerated from the Job member list, exited, and Windows
    /// reassigned that PID to an unrelated external process.
    /// </summary>
    public bool ContainsProcessHandle(IntPtr hProcess)
    {
        if (_disposed || _jobHandle == IntPtr.Zero || !OperatingSystem.IsWindows() || hProcess == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (IsProcessInJob(hProcess, _jobHandle, out bool isInJob) && isInJob)
            {
                return true;
            }
        }
        catch
        {
            // Best-effort handle verification
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_jobHandle != IntPtr.Zero)
        {
            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }
    }
}
