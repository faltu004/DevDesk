namespace DevDesk.Infrastructure.Persistence.Database;

/// <summary>
/// Resolves the default per-user application data location for the SQLite database.
/// </summary>
public sealed class DatabasePathProvider : IDatabasePathProvider
{
    private readonly string _databasePath;

    public DatabasePathProvider(string? customDatabasePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customDatabasePath))
        {
            _databasePath = Path.GetFullPath(customDatabasePath);
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _databasePath = Path.Combine(localAppData, "DevDesk", "Data", "devdesk.db");
        }
    }

    public string GetDatabasePath() => _databasePath;

    public string GetConnectionString() => $"Data Source={_databasePath}";

    public void EnsureDirectoryExists()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
