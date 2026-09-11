namespace DevDesk.Core.Processes;

/// <summary>
/// Authoritative ownership boundary for a process inspected by DevDesk.
/// </summary>
public enum ProcessOwnership
{
    /// <summary>
    /// An external Windows process not spawned or owned by DevDesk.
    /// </summary>
    External = 0,

    /// <summary>
    /// A process owned by an active DevDesk Project Runner session (either root or Job Object descendant).
    /// </summary>
    Managed = 1
}
