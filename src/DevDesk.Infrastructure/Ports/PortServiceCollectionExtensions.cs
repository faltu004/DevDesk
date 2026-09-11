using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DevDesk.Core.Ports;
using DevDesk.Core.Services;

namespace DevDesk.Infrastructure.Ports;

/// <summary>
/// Extension methods for registering DevDesk port engine services into DI.
/// </summary>
public static class PortServiceCollectionExtensions
{
    public static IServiceCollection AddDevDeskPorts(this IServiceCollection services)
    {
        services.AddSingleton<IWindowsPortTableProvider, WindowsPortTableProvider>();
        services.AddSingleton<IProcessMetadataProvider, SystemProcessMetadataProvider>();
        services.AddSingleton<IPortService>(sp => new PortService(
            sp.GetRequiredService<IWindowsPortTableProvider>(),
            sp.GetRequiredService<IProcessMetadataProvider>(),
            sp.GetRequiredService<IProjectService>(),
            sp.GetRequiredService<ILogger<PortService>>()));

        return services;
    }
}
