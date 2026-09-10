using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DevDesk.Core.Detection;
using DevDesk.Core.Repositories;
using DevDesk.Core.Services;
using DevDesk.Infrastructure.Detection;
using DevDesk.Infrastructure.Detection.Detectors;
using DevDesk.Infrastructure.Persistence.Database;
using DevDesk.Infrastructure.Persistence.Repositories;
using DevDesk.Infrastructure.Services;

namespace DevDesk.Infrastructure.Persistence;

/// <summary>
/// Extension methods for registering DevDesk persistence and infrastructure services into the dependency injection container.
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

        // Auto-detection services & detectors
        services.AddSingleton<IProjectDetector, NodeProjectDetector>();
        services.AddSingleton<IProjectDetector, DotNetProjectDetector>();
        services.AddSingleton<IProjectDetector, PythonProjectDetector>();
        services.AddSingleton<IProjectDetectionService, ProjectDetectionService>();

        // Project lifecycle service
        services.AddSingleton<IProjectService, ProjectService>();

        return services;
    }
}
