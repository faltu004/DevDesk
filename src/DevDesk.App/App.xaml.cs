using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DevDesk.App.Services.Navigation;
using DevDesk.App.ViewModels.Dashboard;
using DevDesk.App.ViewModels.Shell;
using DevDesk.App.Views.Shell;
using DevDesk.Infrastructure.Persistence;
using DevDesk.Infrastructure.Persistence.Database;

namespace DevDesk.App;

/// <summary>
/// Application entry point and composition root configuring Microsoft.Extensions.Hosting, Dependency Injection, and Persistence.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder(e.Args);

        // Persistence & Infrastructure Services
        builder.Services.AddDevDeskPersistence();

        // Services, Navigation & Dialogs
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<DevDesk.App.Services.Dialogs.IDialogService, DevDesk.App.Services.Dialogs.DialogService>();

        // ViewModels
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<DevDesk.App.ViewModels.Projects.ProjectsViewModel>();

        // Views / Shell Window
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();

        await _host.StartAsync();

        // Initialize and migrate the SQLite database safely before presenting UI
        var databaseInitializer = _host.Services.GetRequiredService<IDatabaseInitializer>();
        await databaseInitializer.InitializeAsync();

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
