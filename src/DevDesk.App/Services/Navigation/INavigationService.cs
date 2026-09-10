namespace DevDesk.App.Services.Navigation;

/// <summary>
/// Service contract for handling view navigation within the application shell.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Gets the active ViewModel currently displayed in the shell content host.
    /// </summary>
    object? CurrentViewModel { get; }

    /// <summary>
    /// Gets the current navigation destination item.
    /// </summary>
    NavigationItem CurrentItem { get; }

    /// <summary>
    /// Event triggered whenever the current ViewModel or navigation destination changes.
    /// </summary>
    event Action? CurrentViewModelChanged;

    /// <summary>
    /// Navigates to a specific ViewModel type resolved from the DI container.
    /// </summary>
    void NavigateTo<TViewModel>() where TViewModel : class;

    /// <summary>
    /// Navigates to a specific navigation item destination.
    /// </summary>
    void NavigateTo(NavigationItem item);
}
