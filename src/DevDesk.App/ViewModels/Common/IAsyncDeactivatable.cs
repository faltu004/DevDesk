namespace DevDesk.App.ViewModels.Common;

/// <summary>
/// Lifecycle interface for ViewModels that manage background timers, polling loops,
/// or external resources that must be cleanly suspended or disposed when navigating away.
/// </summary>
public interface IAsyncDeactivatable
{
    /// <summary>
    /// Invoked when the user navigates away from this ViewModel's destination.
    /// </summary>
    Task DeactivateAsync();
}
