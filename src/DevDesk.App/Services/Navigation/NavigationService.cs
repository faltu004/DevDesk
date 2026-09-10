using Microsoft.Extensions.DependencyInjection;
using DevDesk.App.ViewModels.Dashboard;

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
            case NavigationItem.Processes:
            case NavigationItem.Ports:
            case NavigationItem.Commands:
            case NavigationItem.Settings:
                // Reserved for subsequent phases
                throw new NotSupportedException($"Navigation to {item} is not yet available in the foundation phase.");

            default:
                throw new ArgumentOutOfRangeException(nameof(item), item, "Unknown navigation item destination.");
        }
    }
}
