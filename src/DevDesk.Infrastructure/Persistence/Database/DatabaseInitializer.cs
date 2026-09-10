using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevDesk.Infrastructure.Persistence.Database;

/// <summary>
/// Implements database startup verification and Entity Framework migration execution.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<DevDeskDbContext> _contextFactory;
    private readonly IDatabasePathProvider _pathProvider;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IDbContextFactory<DevDeskDbContext> contextFactory,
        IDatabasePathProvider pathProvider,
        ILogger<DatabaseInitializer> logger)
    {
        _contextFactory = contextFactory;
        _pathProvider = pathProvider;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Ensuring database directory exists at {Path}", _pathProvider.GetDatabasePath());
            _pathProvider.EnsureDirectoryExists();

            _logger.LogInformation("Applying database migrations...");
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);

            _logger.LogInformation("Database migration complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initializing or migrating the DevDesk SQLite database.");
            throw;
        }
    }
}
