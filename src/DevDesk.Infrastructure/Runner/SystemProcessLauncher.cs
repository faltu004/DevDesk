using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using DevDesk.Infrastructure.Runner.Native;

namespace DevDesk.Infrastructure.Runner;

/// <summary>
/// Managed process instance. Supports both race-free Win32 suspended creation with Job Object assignment
/// and fallback System.Diagnostics.Process execution.
/// </summary>
internal sealed class SystemManagedProcess : IManagedProcess
{
    private IntPtr _hProcess;
    private readonly int _processId;
    private readonly WindowsJobObject? _jobObject;
    private readonly SafeFileHandle? _safeOutHandle;
    private readonly SafeFileHandle? _safeErrHandle;
    private readonly StreamReader? _stdoutReader;
    private readonly StreamReader? _stderrReader;
    private readonly Process? _fallbackProcess;

    private int? _cachedExitCode;
    private bool _rootExited;
    private bool _disposed;

    public int ProcessId => _processId;

    public bool HasExited
    {
        get
        {
            if (_disposed)
            {
                return true;
            }

            if (_fallbackProcess != null)
            {
                try
                {
                    if (!_fallbackProcess.HasExited)
                    {
                        return false;
                    }
                }
                catch
                {
                    // Fallback process handle may be closed
                }
            }
            else
            {
                if (!IsRootExited())
                {
                    return false;
                }
            }

            // If root exited but Job Object still reports active descendant processes, the owned session is still alive
            if (_jobObject != null && _jobObject.GetActiveProcessCount() > 0)
            {
                return false;
            }

            return true;
        }
    }

    public int? ExitCode
    {
        get
        {
            if (_cachedExitCode.HasValue)
            {
                return _cachedExitCode.Value;
            }

            if (_fallbackProcess != null)
            {
                try
                {
                    if (_fallbackProcess.HasExited)
                    {
                        _cachedExitCode = _fallbackProcess.ExitCode;
                        return _cachedExitCode;
                    }
                }
                catch
                {
                    // Catch handle unavailability
                }
            }
            else
            {
                if (IsRootExited())
                {
                    return _cachedExitCode;
                }
            }

            return _cachedExitCode;
        }
    }

    public event EventHandler<string>? StandardOutputReceived;
    public event EventHandler<string>? StandardErrorReceived;
    public event EventHandler<int>? Exited;

    private readonly Task? _stdoutTask;
    private readonly Task? _stderrTask;

    /// <summary>
    /// Native Win32 suspended-process constructor ensuring race-free Job Object assignment.
    /// </summary>
    public SystemManagedProcess(
        IntPtr hProcess,
        int processId,
        IntPtr hStdOutRead,
        IntPtr hStdErrRead,
        WindowsJobObject jobObject)
    {
        _hProcess = hProcess;
        _processId = processId;
        _jobObject = jobObject;

        if (hStdOutRead != IntPtr.Zero)
        {
            _safeOutHandle = new SafeFileHandle(hStdOutRead, ownsHandle: true);
            var outStream = new FileStream(_safeOutHandle, FileAccess.Read, 4096, isAsync: false);
            _stdoutReader = new StreamReader(outStream, Encoding.UTF8);

            _stdoutTask = Task.Run(async () =>
            {
                try
                {
                    while (await _stdoutReader.ReadLineAsync().ConfigureAwait(false) is { } line)
                    {
                        StandardOutputReceived?.Invoke(this, line);
                    }
                }
                catch
                {
                    // Pipe closed
                }
            });
        }

        if (hStdErrRead != IntPtr.Zero)
        {
            _safeErrHandle = new SafeFileHandle(hStdErrRead, ownsHandle: true);
            var errStream = new FileStream(_safeErrHandle, FileAccess.Read, 4096, isAsync: false);
            _stderrReader = new StreamReader(errStream, Encoding.UTF8);

            _stderrTask = Task.Run(async () =>
            {
                try
                {
                    while (await _stderrReader.ReadLineAsync().ConfigureAwait(false) is { } line)
                    {
                        StandardErrorReceived?.Invoke(this, line);
                    }
                }
                catch
                {
                    // Pipe closed
                }
            });
        }

        // Background monitor for root exit and Job Object descendant cleanup
        _ = Task.Run(async () =>
        {
            while (!IsRootExited())
            {
                await Task.Delay(50).ConfigureAwait(false);
                if (_disposed)
                {
                    return;
                }
            }

            // Root exited. If child processes remain in the Job Object, keep waiting
            if (_jobObject != null)
            {
                while (_jobObject.GetActiveProcessCount() > 0)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    if (_disposed)
                    {
                        return;
                    }
                }
            }

            // Final output drain: ensure pipe readers complete before firing exit event
            try
            {
                using var drainCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                var drainTasks = new List<Task>();
                if (_stdoutTask != null) drainTasks.Add(_stdoutTask);
                if (_stderrTask != null) drainTasks.Add(_stderrTask);
                if (drainTasks.Count > 0)
                {
                    await Task.WhenAll(drainTasks).WaitAsync(drainCts.Token).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            if (!_disposed)
            {
                Exited?.Invoke(this, _cachedExitCode ?? 0);
            }
        });
    }

    /// <summary>
    /// Fallback constructor wrapping System.Diagnostics.Process (e.g. for testing or non-Windows).
    /// </summary>
    public SystemManagedProcess(Process process, WindowsJobObject? jobObject = null)
    {
        _fallbackProcess = process;
        _jobObject = jobObject;
        _processId = process.Id;

        _fallbackProcess.EnableRaisingEvents = true;
        _fallbackProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                StandardOutputReceived?.Invoke(this, e.Data);
            }
        };
        _fallbackProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                StandardErrorReceived?.Invoke(this, e.Data);
            }
        };
        _fallbackProcess.Exited += (_, _) =>
        {
            try
            {
                _cachedExitCode = _fallbackProcess.ExitCode;
            }
            catch
            {
                // Process handle may be closed
            }

            if (_jobObject != null && _jobObject.GetActiveProcessCount() > 0)
            {
                _ = Task.Run(async () =>
                {
                    while (!_disposed && _jobObject.GetActiveProcessCount() > 0)
                    {
                        await Task.Delay(100);
                    }

                    if (!_disposed)
                    {
                        Exited?.Invoke(this, _cachedExitCode ?? 0);
                    }
                });
                return;
            }

            Exited?.Invoke(this, _cachedExitCode ?? 0);
        };

        _fallbackProcess.BeginOutputReadLine();
        _fallbackProcess.BeginErrorReadLine();
    }

    private bool IsRootExited()
    {
        if (_rootExited)
        {
            return true;
        }

        if (_hProcess == IntPtr.Zero)
        {
            return true;
        }

        uint waitRes = WindowsProcessApi.WaitForSingleObject(_hProcess, 0);
        if (waitRes == WindowsProcessApi.WAIT_OBJECT_0)
        {
            _rootExited = true;
            if (WindowsProcessApi.GetExitCodeProcess(_hProcess, out uint code))
            {
                _cachedExitCode = (int)code;
            }

            return true;
        }

        return false;
    }

    public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        if (_fallbackProcess != null)
        {
            try
            {
                if (!_fallbackProcess.HasExited)
                {
                    await _fallbackProcess.WaitForExitAsync(cancellationToken);
                }
            }
            catch (InvalidOperationException)
            {
                // Already exited
            }
        }
        else
        {
            while (!IsRootExited())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(50, cancellationToken);
            }
        }

        if (_jobObject != null)
        {
            while (_jobObject.GetActiveProcessCount() > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(50, cancellationToken);
            }
        }

        // Drain remaining output from stdout and stderr pipes
        try
        {
            using var drainCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(drainCts.Token, cancellationToken);
            var drainTasks = new List<Task>();
            if (_stdoutTask != null) drainTasks.Add(_stdoutTask);
            if (_stderrTask != null) drainTasks.Add(_stderrTask);
            if (drainTasks.Count > 0)
            {
                await Task.WhenAll(drainTasks).WaitAsync(linked.Token).ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }

    public void KillEntireProcessTree()
    {
        // 1. Terminate Job Object: kernel terminates all processes in the job atomically
        if (_jobObject != null)
        {
            try
            {
                _jobObject.Terminate(1);
            }
            catch
            {
                // Best effort
            }
        }

        // 2. Also terminate root process directly if fallback or handle available
        if (_fallbackProcess != null)
        {
            try
            {
                if (!_fallbackProcess.HasExited)
                {
                    _fallbackProcess.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Already terminated
            }
        }
        else if (!IsRootExited() && _hProcess != IntPtr.Zero)
        {
            try
            {
                WindowsProcessApi.TerminateProcess(_hProcess, 1);
            }
            catch
            {
                // Already terminated
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _stdoutReader?.Dispose();
        }
        catch
        {
        }

        try
        {
            _stderrReader?.Dispose();
        }
        catch
        {
        }

        try
        {
            _safeOutHandle?.Dispose();
        }
        catch
        {
        }

        try
        {
            _safeErrHandle?.Dispose();
        }
        catch
        {
        }

        if (_hProcess != IntPtr.Zero)
        {
            WindowsProcessApi.CloseHandle(_hProcess);
            _hProcess = IntPtr.Zero;
        }

        try
        {
            _fallbackProcess?.Dispose();
        }
        catch
        {
        }

        try
        {
            _jobObject?.Dispose();
        }
        catch
        {
        }
    }
}

/// <summary>
/// Production launcher spawning processes within a dedicated Windows Job Object.
/// Implements suspended process creation to guarantee race-free Job Object membership before main thread execution.
/// Uses STARTUPINFOEX and PROC_THREAD_ATTRIBUTE_HANDLE_LIST to strictly limit handle inheritance to stdout/stderr pipes.
/// </summary>
internal sealed class SystemProcessLauncher : IProcessLauncher
{
    public IManagedProcess Launch(ProcessLaunchConfiguration config)
    {
        if (OperatingSystem.IsWindows())
        {
            return LaunchWindowsSuspended(config);
        }

        return LaunchFallback(config);
    }

    private static IManagedProcess LaunchWindowsSuspended(ProcessLaunchConfiguration config)
    {
        var jobObject = WindowsJobObject.Create()
            ?? throw new InvalidOperationException("Failed to create Windows Job Object for process session.");

        IntPtr hStdOutRead = IntPtr.Zero;
        IntPtr hStdOutWrite = IntPtr.Zero;
        IntPtr hStdErrRead = IntPtr.Zero;
        IntPtr hStdErrWrite = IntPtr.Zero;

        try
        {
            var sa = new WindowsProcessApi.SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf<WindowsProcessApi.SECURITY_ATTRIBUTES>(),
                bInheritHandle = true
            };

            if (!WindowsProcessApi.CreatePipe(out hStdOutRead, out hStdOutWrite, ref sa, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create stdout pipe.");
            }
            WindowsProcessApi.SetHandleInformation(hStdOutRead, WindowsProcessApi.HANDLE_FLAG_INHERIT, 0);

            if (!WindowsProcessApi.CreatePipe(out hStdErrRead, out hStdErrWrite, ref sa, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create stderr pipe.");
            }
            WindowsProcessApi.SetHandleInformation(hStdErrRead, WindowsProcessApi.HANDLE_FLAG_INHERIT, 0);

            string commandLine = config.IsCmdShim
                ? WindowsCommandLineSerializer.FormatCmdShimCommandLine(
                    Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    config.ExecutablePath,
                    config.Arguments)
                : WindowsCommandLineSerializer.FormatCommandLine(
                    config.ExecutablePath,
                    config.Arguments);

            var siEx = new WindowsProcessApi.STARTUPINFOEX();
            siEx.StartupInfo.cb = Marshal.SizeOf<WindowsProcessApi.STARTUPINFOEX>();
            siEx.StartupInfo.dwFlags = (int)WindowsProcessApi.STARTF_USESTDHANDLES;
            siEx.StartupInfo.hStdOutput = hStdOutWrite;
            siEx.StartupInfo.hStdError = hStdErrWrite;
            siEx.StartupInfo.hStdInput = IntPtr.Zero;

            // Restrict child process handle inheritance STRICTLY to only [hStdOutWrite, hStdErrWrite]
            IntPtr lpSize = IntPtr.Zero;
            WindowsProcessApi.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);
            IntPtr pAttributeList = Marshal.AllocHGlobal(lpSize);

            WindowsProcessApi.PROCESS_INFORMATION pi;
            bool created;
            int createProcessError = 0;

            try
            {
                if (!WindowsProcessApi.InitializeProcThreadAttributeList(pAttributeList, 1, 0, ref lpSize))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to initialize process attribute list.");
                }

                IntPtr[] handlesToInherit = [hStdOutWrite, hStdErrWrite];
                var pinnedHandles = GCHandle.Alloc(handlesToInherit, GCHandleType.Pinned);
                try
                {
                    if (!WindowsProcessApi.UpdateProcThreadAttribute(
                            pAttributeList,
                            0,
                            WindowsProcessApi.PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                            pinnedHandles.AddrOfPinnedObject(),
                            (IntPtr)(handlesToInherit.Length * IntPtr.Size),
                            IntPtr.Zero,
                            IntPtr.Zero))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set process handle inheritance list.");
                    }

                    siEx.lpAttributeList = pAttributeList;

                    uint creationFlags = WindowsProcessApi.CREATE_SUSPENDED
                        | WindowsProcessApi.CREATE_NO_WINDOW
                        | WindowsProcessApi.EXTENDED_STARTUPINFO_PRESENT;

                    var saProcess = default(WindowsProcessApi.SECURITY_ATTRIBUTES);
                    var saThread = default(WindowsProcessApi.SECURITY_ATTRIBUTES);

                    created = WindowsProcessApi.CreateProcessW(
                        null,
                        commandLine,
                        ref saProcess,
                        ref saThread,
                        bInheritHandles: true,
                        creationFlags,
                        IntPtr.Zero,
                        config.WorkingDirectory,
                        ref siEx,
                        out pi);

                    if (!created)
                    {
                        createProcessError = Marshal.GetLastWin32Error();
                    }
                }
                finally
                {
                    pinnedHandles.Free();
                    WindowsProcessApi.DeleteProcThreadAttributeList(pAttributeList);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pAttributeList);
            }

            // Always close parent write handles immediately so EOF is observed when child closes its ends
            WindowsProcessApi.CloseHandle(hStdOutWrite);
            hStdOutWrite = IntPtr.Zero;
            WindowsProcessApi.CloseHandle(hStdErrWrite);
            hStdErrWrite = IntPtr.Zero;

            if (!created)
            {
                throw new Win32Exception(createProcessError, $"Failed to create process for '{config.ExecutablePath}'.");
            }

            // STEP 1: Assign process to the Job Object while still SUSPENDED
            bool assigned = jobObject.AssignProcessHandle(pi.hProcess);
            if (!assigned)
            {
                // CRITICAL: Immediately terminate suspended process so no unmanaged process survives
                WindowsProcessApi.TerminateProcess(pi.hProcess, 1);
                WindowsProcessApi.CloseHandle(pi.hThread);
                WindowsProcessApi.CloseHandle(pi.hProcess);
                throw new InvalidOperationException($"Failed to assign process (PID {pi.dwProcessId}) to Windows Job Object.");
            }

            // STEP 2: Resume main thread only after successful Job Object assignment
            uint resumeRes = WindowsProcessApi.ResumeThread(pi.hThread);
            WindowsProcessApi.CloseHandle(pi.hThread);

            if (resumeRes == uint.MaxValue)
            {
                WindowsProcessApi.TerminateProcess(pi.hProcess, 1);
                WindowsProcessApi.CloseHandle(pi.hProcess);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to resume process main thread.");
            }

            return new SystemManagedProcess(pi.hProcess, pi.dwProcessId, hStdOutRead, hStdErrRead, jobObject);
        }
        catch
        {
            if (hStdOutWrite != IntPtr.Zero) WindowsProcessApi.CloseHandle(hStdOutWrite);
            if (hStdErrWrite != IntPtr.Zero) WindowsProcessApi.CloseHandle(hStdErrWrite);
            if (hStdOutRead != IntPtr.Zero) WindowsProcessApi.CloseHandle(hStdOutRead);
            if (hStdErrRead != IntPtr.Zero) WindowsProcessApi.CloseHandle(hStdErrRead);
            jobObject.Dispose();
            throw;
        }
    }

    private static IManagedProcess LaunchFallback(ProcessLaunchConfiguration config)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = config.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        if (config.IsCmdShim)
        {
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(config.ExecutablePath);

            foreach (var arg in config.Arguments)
            {
                psi.ArgumentList.Add(arg);
            }
        }
        else
        {
            psi.FileName = config.ExecutablePath;
            foreach (var arg in config.Arguments)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to spawn process for '{config.ExecutablePath}'.");

        return new SystemManagedProcess(process);
    }
}
