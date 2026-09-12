using DevDesk.Core.SystemMonitor;
using Microsoft.Extensions.DependencyInjection;

namespace DevDesk.Infrastructure.SystemMonitor;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDevDeskSystemMonitor(this IServiceCollection services)
    {
        services.AddSingleton<IMonotonicClock>(SystemMonotonicClock.Instance);
        services.AddSingleton<ISystemMetricsProvider, WindowsSystemMetricsProvider>();
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
        return services;
    }
}
