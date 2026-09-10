namespace DevDesk.Core.Launchers;

/// <summary>
/// Provides secure operations to open project directories in external developer tools.
/// </summary>
public interface ILauncherService
{
    /// <summary>
    /// Opens the specified project folder in Visual Studio Code.
    /// </summary>
    Task<LaunchResult> OpenInVsCodeAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the specified project folder in Windows File Explorer.
    /// </summary>
    Task<LaunchResult> OpenInExplorerAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an interactive terminal session at the specified project directory without executing any commands.
    /// </summary>
    Task<LaunchResult> OpenTerminalAsync(string projectPath, CancellationToken cancellationToken = default);
}
