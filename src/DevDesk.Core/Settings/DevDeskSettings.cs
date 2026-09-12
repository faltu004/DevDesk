namespace DevDesk.Core.Settings;

/// <summary>
/// Authoritative strongly typed DevDesk user preferences.
/// </summary>
public sealed record DevDeskSettings
{
    /// <summary>
    /// Optional absolute path to Code.exe overriding auto-detection.
    /// Empty or null indicates automatic discovery.
    /// </summary>
    public string? VsCodeExecutableOverride { get; init; }

    /// <summary>
    /// Preferred interactive terminal shell.
    /// </summary>
    public PreferredTerminal PreferredTerminal { get; init; } = PreferredTerminal.Auto;

    /// <summary>
    /// Live refresh interval used by Dashboard and Processes.
    /// Does not affect Ports.
    /// </summary>
    public MonitorRefreshInterval MonitorRefreshInterval { get; init; } = MonitorRefreshInterval.Seconds1_5;
}
