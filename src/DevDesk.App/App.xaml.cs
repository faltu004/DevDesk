using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DevDesk.App.Services.Navigation;
using DevDesk.App.ViewModels.Dashboard;
using DevDesk.App.ViewModels.Shell;
using DevDesk.App.Views.Shell;

namespace DevDesk.App;

/// <summary>
/// Application entry point and composition root configuring Microsoft.Extensions.Hosting and Dependency Injection.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder(e.Args);

        // Services & Navigation
        builder.Services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();

        // Views / Shell Window
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
