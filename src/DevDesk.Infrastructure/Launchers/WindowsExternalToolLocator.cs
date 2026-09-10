using System.IO;
using Microsoft.Extensions.Logging;

namespace DevDesk.Infrastructure.Launchers;

/// <summary>
/// Windows implementation for deterministic discovery of external developer tools, editors, and shells.
/// </summary>
internal sealed class WindowsExternalToolLocator : IExternalToolLocator
{
    private readonly ILogger<WindowsExternalToolLocator> _logger;

    public WindowsExternalToolLocator(ILogger<WindowsExternalToolLocator> logger)
    {
        _logger = logger;
    }

    public string? FindVsCodeExecutable()
    {
        try
        {
            // 1. Check known user install location (default for modern VS Code user installer)
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                var userCodePath = Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe");
                if (File.Exists(userCodePath))
                {
                    return Path.GetFullPath(userCodePath);
                }
            }

            // 2. Check known system 64-bit install location
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles))
            {
                var systemCodePath = Path.Combine(programFiles, "Microsoft VS Code", "Code.exe");
                if (File.Exists(systemCodePath))
                {
                    return Path.GetFullPath(systemCodePath);
                }
            }

            // 3. Check known system 32-bit install location
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFilesX86))
            {
                var systemX86CodePath = Path.Combine(programFilesX86, "Microsoft VS Code", "Code.exe");
                if (File.Exists(systemX86CodePath))
                {
                    return Path.GetFullPath(systemX86CodePath);
                }
            }

            // 4. Check PATH environment variable for Code.exe or code.cmd's parent directory
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(pathEnv))
            {
                var directories = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var dir in directories)
                {
                    if (!Directory.Exists(dir))
                    {
                        continue;
                    }

                    // Direct Code.exe on PATH
                    var directExe = Path.Combine(dir, "Code.exe");
                    if (File.Exists(directExe))
                    {
                        return Path.GetFullPath(directExe);
                    }

                    // If PATH exposes code.cmd in a \bin folder, use it strictly as evidence to locate parent Code.exe
                    var codeCmd = Path.Combine(dir, "code.cmd");
                    if (File.Exists(codeCmd))
                    {
                        var parentExe = Path.Combine(dir, "..", "Code.exe");
                        if (File.Exists(parentExe))
                        {
                            return Path.GetFullPath(parentExe);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error while probing for VS Code executable");
        }

        return null;
    }

    public string? FindExplorerExecutable()
    {
        try
        {
            // Prefer deterministic system path %WINDIR%\explorer.exe
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(winDir))
            {
                var explorerPath = Path.Combine(winDir, "explorer.exe");
                if (File.Exists(explorerPath))
                {
                    return Path.GetFullPath(explorerPath);
                }
            }

            // Secondary fallback: %SystemRoot%\explorer.exe
            var sysRoot = Environment.GetEnvironmentVariable("SystemRoot");
            if (!string.IsNullOrWhiteSpace(sysRoot))
            {
                var explorerPath = Path.Combine(sysRoot, "explorer.exe");
                if (File.Exists(explorerPath))
                {
                    return Path.GetFullPath(explorerPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error while probing for Explorer executable");
        }

        return null;
    }

    public TerminalLaunchTarget? FindPreferredTerminal()
    {
        try
        {
            // 1. Windows Terminal (wt.exe)
            var wtPath = FindWindowsTerminal();
            if (!string.IsNullOrEmpty(wtPath))
            {
                return new TerminalLaunchTarget(wtPath, TerminalType.WindowsTerminal);
            }

            // 2. PowerShell 7 (pwsh.exe)
            var pwshPath = FindPowerShell7();
            if (!string.IsNullOrEmpty(pwshPath))
            {
                return new TerminalLaunchTarget(pwshPath, TerminalType.PowerShell7);
            }

            // 3. Windows PowerShell fallback (powershell.exe)
            var psPath = FindWindowsPowerShell();
            if (!string.IsNullOrEmpty(psPath))
            {
                return new TerminalLaunchTarget(psPath, TerminalType.WindowsPowerShell);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error while probing for terminal executables");
        }

        return null;
    }

    private static string? FindWindowsTerminal()
    {
        // Check WindowsApps alias location
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            var wtAlias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "wt.exe");
            if (File.Exists(wtAlias))
            {
                return Path.GetFullPath(wtAlias);
            }
        }

        // Check PATH
        return FindOnPath("wt.exe");
    }

    private static string? FindPowerShell7()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
        {
            var pwshPath = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
            if (File.Exists(pwshPath))
            {
                return Path.GetFullPath(pwshPath);
            }
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
        {
            var pwshPath = Path.Combine(programFilesX86, "PowerShell", "7", "pwsh.exe");
            if (File.Exists(pwshPath))
            {
                return Path.GetFullPath(pwshPath);
            }
        }

        return FindOnPath("pwsh.exe");
    }

    private static string? FindWindowsPowerShell()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(winDir))
        {
            var sys32Ps = Path.Combine(winDir, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(sys32Ps))
            {
                return Path.GetFullPath(sys32Ps);
            }
        }

        return FindOnPath("powershell.exe");
    }

    private static string? FindOnPath(string executableName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return null;
        }

        var directories = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in directories)
        {
            try
            {
                var candidate = Path.Combine(dir, executableName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Ignore invalid path format entries on PATH
            }
        }

        return null;
    }
}
