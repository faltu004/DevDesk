using Microsoft.Extensions.DependencyInjection;
using DevDesk.App.ViewModels.Common;
using DevDesk.App.ViewModels.Dashboard;
using DevDesk.App.ViewModels.Ports;
using DevDesk.App.ViewModels.Projects;

namespace DevDesk.App.Services.Navigation;

/// <summary>
/// Implements application navigation using dependency injection for ViewModel resolution.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private object? _currentViewModel;
    private NavigationItem _currentItem = NavigationItem.Dashboard;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object? CurrentViewModel => _currentViewModel;

    public NavigationItem CurrentItem => _currentItem;

    public event Action? CurrentViewModelChanged;

    public void NavigateTo<TViewModel>() where TViewModel : class
    {
        var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
        _currentViewModel = viewModel;
        CurrentViewModelChanged?.Invoke();

        if (viewModel is IAsyncInitializable initializable)
        {
            _ = SafeInitializeAsync(initializable);
        }
    }

    private static async Task SafeInitializeAsync(IAsyncInitializable initializable)
    {
        try
        {
            await initializable.InitializeAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation
        }
        catch (Exception ex)
        {
            // Observed safely; prevents unhandled task exception from crashing dispatcher
            System.Diagnostics.Debug.WriteLine($"Error during async initialization of {initializable.GetType().Name}: {ex}");
        }
    }

    public void NavigateTo(NavigationItem item)
    {
        switch (item)
        {
            case NavigationItem.Dashboard:
                _currentItem = NavigationItem.Dashboard;
                NavigateTo<DashboardViewModel>();
                break;

            case NavigationItem.Projects:
                _currentItem = NavigationItem.Projects;
                NavigateTo<ProjectsViewModel>();
                break;

            case NavigationItem.Ports:
                _currentItem = NavigationItem.Ports;
                NavigateTo<PortsViewModel>();
                break;

            case NavigationItem.Processes:
            case NavigationItem.Commands:
            case NavigationItem.Settings:
                // Reserved for subsequent phases
                throw new NotSupportedException($"Navigation to {item} is not yet available.");

            default:
                throw new ArgumentOutOfRangeException(nameof(item), item, "Unknown navigation item destination.");
        }
    }
}
