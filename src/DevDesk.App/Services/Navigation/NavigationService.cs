using Microsoft.Extensions.DependencyInjection;
using DevDesk.App.ViewModels.Commands;
using DevDesk.App.ViewModels.Common;
using DevDesk.App.ViewModels.Dashboard;
using DevDesk.App.ViewModels.Ports;
using DevDesk.App.ViewModels.Projects;
using DevDesk.App.ViewModels.Settings;

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
        if (_currentViewModel is IAsyncDeactivatable oldDeactivatable)
        {
            _ = SafeDeactivateAsync(oldDeactivatable);
        }

        var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
        _currentViewModel = viewModel;
        CurrentViewModelChanged?.Invoke();

        if (viewModel is IAsyncInitializable initializable)
        {
            _ = SafeInitializeAsync(initializable);
        }
    }

    private static async Task SafeDeactivateAsync(IAsyncDeactivatable deactivatable)
    {
        try
        {
            await deactivatable.DeactivateAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during async deactivation of {deactivatable.GetType().Name}: {ex}");
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

            case NavigationItem.Processes:
                _currentItem = NavigationItem.Processes;
                NavigateTo<DevDesk.App.ViewModels.Processes.ProcessesViewModel>();
                break;

            case NavigationItem.Ports:
                _currentItem = NavigationItem.Ports;
                NavigateTo<PortsViewModel>();
                break;

            case NavigationItem.Commands:
                _currentItem = NavigationItem.Commands;
                NavigateTo<CommandsViewModel>();
                break;

            case NavigationItem.Settings:
                _currentItem = NavigationItem.Settings;
                NavigateTo<SettingsViewModel>();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(item), item, "Unknown navigation item destination.");
        }
    }
}
