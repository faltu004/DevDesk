namespace DevDesk.Core.Runner;

/// <summary>
/// Truthful runtime state of a DevDesk-managed project execution session.
/// </summary>
public enum ProjectRunState
{
    /// <summary>
    /// The process is being prepared and spawned.
    /// </summary>
    Starting,

    /// <summary>
    /// The process was successfully spawned by DevDesk and has an active PID.
    /// </summary>
    Running,

    /// <summary>
    /// Termination of the DevDesk-owned process tree has been initiated.
    /// </summary>
    Stopping,

    /// <summary>
    /// The process has terminated, either naturally or via stop request.
    /// </summary>
    Exited,

    /// <summary>
    /// The process failed to start or crashed on launch.
    /// </summary>
    Failed
}
