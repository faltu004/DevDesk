namespace DevDesk.Core.Settings;

/// <summary>
/// Service contract for loading, persisting, and observing DevDesk application settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Loads settings asynchronously from SQLite persistence with fallback to safe defaults.
    /// </summary>
    Task<DevDeskSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active in-memory settings snapshot synchronously.
    /// </summary>
    DevDeskSettings GetCurrentSettings();

    /// <summary>
    /// Validates and saves updated settings to SQLite persistence.
    /// </summary>
    Task SaveSettingsAsync(DevDeskSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets only Phase 13 preference keys in persistence without touching projects, saved commands, or database data.
    /// </summary>
    Task ResetToDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when settings have been updated or reset.
    /// </summary>
    event Action<DevDeskSettings>? SettingsChanged;
}
