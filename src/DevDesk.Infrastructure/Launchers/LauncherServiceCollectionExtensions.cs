using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DevDesk.Core.Launchers;

namespace DevDesk.Infrastructure.Launchers;

/// <summary>
/// Extension methods for registering DevDesk launcher infrastructure services into DI.
/// </summary>
public static class LauncherServiceCollectionExtensions
{
    /// <summary>
    /// Registers launcher services, external tool locators, and process runners.
    /// </summary>
    public static IServiceCollection AddDevDeskLaunchers(this IServiceCollection services)
    {
        services.AddSingleton<IProcessRunner, SystemProcessRunner>();
        services.AddSingleton<IExternalToolLocator, WindowsExternalToolLocator>();
        services.AddSingleton<ILauncherService>(sp => new LauncherService(
            sp.GetRequiredService<IExternalToolLocator>(),
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<ILogger<LauncherService>>(),
            sp.GetService<DevDesk.Core.Settings.ISettingsService>()));
        return services;
    }
}
