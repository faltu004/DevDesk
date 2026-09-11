using Microsoft.Extensions.DependencyInjection;
using DevDesk.Core.Processes;

namespace DevDesk.Infrastructure.Processes;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDevDeskProcesses(this IServiceCollection services)
    {
        services.AddSingleton<IProcessSnapshotProvider, SystemProcessSnapshotProvider>();
        services.AddSingleton<IProcessService, ProcessService>();
        return services;
    }
}
