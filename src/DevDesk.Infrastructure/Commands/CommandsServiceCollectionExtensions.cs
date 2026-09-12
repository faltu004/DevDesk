using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DevDesk.Core.Commands;
using DevDesk.Core.Repositories;
using DevDesk.Core.Services;
using DevDesk.Infrastructure.Persistence.Repositories;
using DevDesk.Infrastructure.Runner;
using DevDesk.Infrastructure.Services;

namespace DevDesk.Infrastructure.Commands;

public static class CommandsServiceCollectionExtensions
{
    public static IServiceCollection AddDevDeskSavedCommands(this IServiceCollection services)
    {
        services.AddSingleton<ISavedCommandRepository, SavedCommandRepository>();
        services.AddSingleton<ISavedCommandService, SavedCommandService>();
        services.AddSingleton<ISavedCommandToolResolver, SavedCommandToolResolver>();
        services.AddSingleton<ISavedCommandExecutor>(sp => new SavedCommandExecutor(
            sp.GetRequiredService<ISavedCommandService>(),
            sp.GetRequiredService<ISavedCommandToolResolver>(),
            sp.GetRequiredService<IProcessLauncher>(),
            sp.GetRequiredService<ILogger<SavedCommandExecutor>>()));

        return services;
    }
}
