namespace DevDesk.Core.Settings;

/// <summary>
/// Specifies the preferred terminal application to use when launching developer terminals.
/// </summary>
public enum PreferredTerminal
{
    Auto = 0,
    WindowsTerminal = 1,
    PowerShell7 = 2,
    WindowsPowerShell = 3,
    CommandPrompt = 4
}
