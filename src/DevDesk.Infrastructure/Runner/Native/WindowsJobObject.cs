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
