using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DevDesk.Core.Repositories;
using DevDesk.Infrastructure.Persistence.Database;
using DevDesk.Infrastructure.Persistence.Repositories;

namespace DevDesk.Infrastructure.Persistence;

/// <summary>
/// Extension methods for registering DevDesk persistence services into the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDevDeskPersistence(
        this IServiceCollection services,
        string? customDatabasePath = null)
    {
        services.AddSingleton<IDatabasePathProvider>(_ => new DatabasePathProvider(customDatabasePath));

        services.AddDbContextFactory<DevDeskDbContext>((serviceProvider, options) =>
        {
            var pathProvider = serviceProvider.GetRequiredService<IDatabasePathProvider>();
            options.UseSqlite(pathProvider.GetConnectionString());
        });

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IProjectRepository, ProjectRepository>();

        return services;
    }
}
