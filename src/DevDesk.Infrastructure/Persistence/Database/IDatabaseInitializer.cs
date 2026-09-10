namespace DevDesk.Infrastructure.Persistence.Database;

/// <summary>
/// Handles database directory validation and schema migration at application startup.
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Ensures database storage directories exist and applies any pending Entity Framework migrations.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
