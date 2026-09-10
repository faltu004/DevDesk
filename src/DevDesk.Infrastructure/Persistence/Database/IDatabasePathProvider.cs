namespace DevDesk.Infrastructure.Persistence.Database;

/// <summary>
/// Provides resolution and filesystem initialization for the local DevDesk SQLite database.
/// </summary>
public interface IDatabasePathProvider
{
    /// <summary>
    /// Gets the full absolute path to the SQLite database file.
    /// </summary>
    string GetDatabasePath();

    /// <summary>
    /// Gets the SQLite connection string.
    /// </summary>
    string GetConnectionString();

    /// <summary>
    /// Ensures that the parent directory containing the SQLite database file exists.
    /// </summary>
    void EnsureDirectoryExists();
}
