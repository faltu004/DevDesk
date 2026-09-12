using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DevDesk.App.Services.Navigation;
using DevDesk.App.ViewModels.Dashboard;
using DevDesk.App.ViewModels.Shell;
using DevDesk.App.Views.Shell;
using DevDesk.Infrastructure.Commands;
using DevDesk.Infrastructure.Git;
using DevDesk.Infrastructure.Launchers;
using DevDesk.Infrastructure.Persistence;
using DevDesk.Infrastructure.Persistence.Database;
using DevDesk.Infrastructure.Ports;
using DevDesk.Infrastructure.Processes;
using DevDesk.Infrastructure.Runner;

namespace DevDesk.App;

/// <summary>
/// Application entry point and composition root configuring Microsoft.Extensions.Hosting, Dependency Injection, and Persistence.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    public static bool IsSystemSessionEnding { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder(e.Args);

        // Persistence & Infrastructure Services
        builder.Services.AddDevDeskPersistence();
        builder.Services.AddDevDeskLaunchers();
        builder.Services.AddDevDeskPorts();
        builder.Services.AddDevDeskProcesses();
        builder.Services.AddDevDeskRunner();
        builder.Services.AddDevDeskGit();
        builder.Services.AddDevDeskSavedCommands();

        // Services, Navigation & Dialogs
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<DevDesk.App.Services.Dialogs.IDialogService, DevDesk.App.Services.Dialogs.DialogService>();

        // ViewModels
        builder.Services.AddSingleton<ShellViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddSingleton<DevDesk.App.ViewModels.Projects.ProjectLogsViewModel>();
        builder.Services.AddSingleton<DevDesk.App.ViewModels.Projects.ProjectGitViewModel>();
        builder.Services.AddSingleton<DevDesk.App.ViewModels.Projects.ProjectsViewModel>();
        builder.Services.AddSingleton<DevDesk.App.ViewModels.Commands.CommandsViewModel>();
        builder.Services.AddSingleton<DevDesk.App.ViewModels.Processes.ProcessesViewModel>(sp => new DevDesk.App.ViewModels.Processes.ProcessesViewModel(
            sp.GetRequiredService<DevDesk.Core.Processes.IProcessService>(),
            sp.GetRequiredService<DevDesk.Core.Launchers.ILauncherService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DevDesk.App.ViewModels.Processes.ProcessesViewModel>>(),
            action =>
            {
                if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(action);
                }
                else
                {
                    action();
                }
            }));
        builder.Services.AddSingleton<DevDesk.App.ViewModels.Ports.PortsViewModel>();

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

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsSystemSessionEnding = true;
        base.OnSessionEnding(e);
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
