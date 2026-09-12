using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DevDesk.Core.Models;
using DevDesk.Core.Settings;
using DevDesk.Infrastructure.Persistence;

namespace DevDesk.Infrastructure.Settings;

/// <summary>
/// SQLite-backed implementation of ISettingsService using the durable AppSettings table.
/// Provides isolated per-setting fallback and safe defaults upon storage anomalies.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    public static class Keys
    {
        public const string VsCodeExecutableOverride = "launcher.vscodeExecutableOverride";
        public const string PreferredTerminal = "launcher.preferredTerminal";
        public const string MonitorRefreshInterval = "monitor.refreshInterval";
    }

    private readonly IDbContextFactory<DevDeskDbContext> _contextFactory;
    private readonly ILogger<SettingsService> _logger;
    private readonly object _syncLock = new();
    private DevDeskSettings _cachedSettings = new();

    public event Action<DevDeskSettings>? SettingsChanged;

    public SettingsService(
        IDbContextFactory<DevDeskDbContext> contextFactory,
        ILogger<SettingsService> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DevDeskSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var records = await context.AppSettings
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var recordDict = records
                .Where(r => !string.IsNullOrWhiteSpace(r.Key))
                .ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

            string? vsCodeOverride = null;
            if (recordDict.TryGetValue(Keys.VsCodeExecutableOverride, out var vsCodeVal) && !string.IsNullOrWhiteSpace(vsCodeVal))
            {
                vsCodeOverride = vsCodeVal.Trim();
            }

            var preferredTerminal = PreferredTerminal.Auto;
            if (recordDict.TryGetValue(Keys.PreferredTerminal, out var termVal) && !string.IsNullOrWhiteSpace(termVal))
            {
                if (Enum.TryParse<PreferredTerminal>(termVal, true, out var parsedTerm))
                {
                    preferredTerminal = parsedTerm;
                }
                else
                {
                    _logger.LogWarning("Invalid preferred terminal setting '{Value}' stored; falling back to Auto", termVal);
                }
            }

            var monitorInterval = MonitorRefreshInterval.Seconds1_5;
            if (recordDict.TryGetValue(Keys.MonitorRefreshInterval, out var intervalVal) && !string.IsNullOrWhiteSpace(intervalVal))
            {
                if (Enum.TryParse<MonitorRefreshInterval>(intervalVal, true, out var parsedInterval))
                {
                    monitorInterval = parsedInterval;
                }
                else if (intervalVal.Contains("10", StringComparison.OrdinalIgnoreCase))
                {
                    monitorInterval = MonitorRefreshInterval.Seconds10;
                }
                else if (intervalVal.Contains("5", StringComparison.OrdinalIgnoreCase))
                {
                    monitorInterval = MonitorRefreshInterval.Seconds5;
                }
                else if (intervalVal.Contains("2", StringComparison.OrdinalIgnoreCase))
                {
                    monitorInterval = MonitorRefreshInterval.Seconds2;
                }
                else
                {
                    _logger.LogWarning("Invalid monitor refresh interval setting '{Value}' stored; falling back to 1.5s", intervalVal);
                }
            }

            var resolved = new DevDeskSettings
            {
                VsCodeExecutableOverride = vsCodeOverride,
                PreferredTerminal = preferredTerminal,
                MonitorRefreshInterval = monitorInterval
            };

            lock (_syncLock)
            {
                _cachedSettings = resolved;
            }

            return resolved;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings from SQLite persistence; utilizing fallback defaults");
            lock (_syncLock)
            {
                return _cachedSettings;
            }
        }
    }

    public DevDeskSettings GetCurrentSettings()
    {
        lock (_syncLock)
        {
            return _cachedSettings;
        }
    }

    public async Task SaveSettingsAsync(DevDeskSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Security path validation
        if (!string.IsNullOrWhiteSpace(settings.VsCodeExecutableOverride))
        {
            if (!ExecutablePathValidator.TryValidate(settings.VsCodeExecutableOverride, out var validatedPath, out var error))
            {
                _logger.LogWarning("Rejecting invalid VS Code executable override: {Error}", error);
                throw new ArgumentException(error ?? "Invalid VS Code executable override path.");
            }

            settings = settings with { VsCodeExecutableOverride = validatedPath };
        }

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            await UpsertSettingAsync(context, Keys.VsCodeExecutableOverride, settings.VsCodeExecutableOverride ?? string.Empty, cancellationToken);
            await UpsertSettingAsync(context, Keys.PreferredTerminal, settings.PreferredTerminal.ToString(), cancellationToken);
            await UpsertSettingAsync(context, Keys.MonitorRefreshInterval, settings.MonitorRefreshInterval.ToString(), cancellationToken);

            await context.SaveChangesAsync(cancellationToken);

            lock (_syncLock)
            {
                _cachedSettings = settings;
            }

            _logger.LogInformation("Successfully persisted updated DevDesk settings");
            SettingsChanged?.Invoke(settings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database failure while persisting settings to SQLite");
            throw;
        }
    }

    public async Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var phase13Keys = new[]
            {
                Keys.VsCodeExecutableOverride,
                Keys.PreferredTerminal,
                Keys.MonitorRefreshInterval
            };

            var toRemove = await context.AppSettings
                .Where(s => phase13Keys.Contains(s.Key))
                .ToListAsync(cancellationToken);

            if (toRemove.Count > 0)
            {
                context.AppSettings.RemoveRange(toRemove);
                await context.SaveChangesAsync(cancellationToken);
            }

            var defaults = new DevDeskSettings();
            lock (_syncLock)
            {
                _cachedSettings = defaults;
            }

            _logger.LogInformation("Reset Phase 13 settings to defaults in SQLite");
            SettingsChanged?.Invoke(defaults);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset settings to defaults in SQLite persistence");
            throw;
        }
    }

    private static async Task UpsertSettingAsync(
        DevDeskDbContext context,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        var existing = await context.AppSettings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (existing is not null)
        {
            existing.Value = value;
        }
        else
        {
            context.AppSettings.Add(new AppSetting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value
            });
        }
    }
}
