namespace DevDesk.Infrastructure.Launchers;

/// <summary>
/// Terminal execution shell type for determining launch argument conventions.
/// </summary>
internal enum TerminalType
{
    None,
    WindowsTerminal,
    PowerShell7,
    WindowsPowerShell
}

/// <summary>
/// Resolved terminal executable target and its associated shell type.
/// </summary>
internal sealed record TerminalLaunchTarget(string ExecutablePath, TerminalType Type);

/// <summary>
/// Internal abstraction for deterministic discovery of external developer tools, editors, and shells.
/// </summary>
internal interface IExternalToolLocator
{
    /// <summary>
    /// Discovers the absolute path to the real Code.exe binary, or null if VS Code is not installed.
    /// Never returns code.cmd or shell scripts.
    /// </summary>
    string? FindVsCodeExecutable();

    /// <summary>
    /// Discovers the absolute path to the system explorer.exe binary.
    /// </summary>
    string? FindExplorerExecutable();

    /// <summary>
    /// Discovers the preferred terminal executable in precedence order:
    /// Windows Terminal (wt.exe) -> PowerShell 7 (pwsh.exe) -> Windows PowerShell (powershell.exe).
    /// </summary>
    TerminalLaunchTarget? FindPreferredTerminal();
}
