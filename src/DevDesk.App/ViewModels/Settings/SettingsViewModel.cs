using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using DevDesk.App.Services.Dialogs;
using DevDesk.App.ViewModels.Common;
using DevDesk.Core.Git;
using DevDesk.Core.Launchers;
using DevDesk.Core.Services;
using DevDesk.Core.Settings;
using DevDesk.Infrastructure.Persistence.Database;

namespace DevDesk.App.ViewModels.Settings;

public sealed record TerminalOptionItem(PreferredTerminal Value, string DisplayName);
public sealed record RefreshIntervalOptionItem(MonitorRefreshInterval Value, string DisplayName);

/// <summary>
/// Root ViewModel coordinating DevDesk preferences, validation, live status probes, and persistence.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase, IAsyncInitializable
{
    private readonly ISettingsService _settingsService;
    private readonly IDatabasePathProvider _databasePathProvider;
    private readonly ILauncherService _launcherService;
    private readonly IDialogService _dialogService;
    private readonly IGitService? _gitService;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly Action<Action> _uiDispatcher;

    private DevDeskSettings _baselineSettings = new();

    [ObservableProperty]
    private string _title = "Settings";

    [ObservableProperty]
    private string _subtitle = "Configure DevDesk integrations and local preferences.";

    [ObservableProperty]
    private string _themeDisplay = "Dark";

    [ObservableProperty]
    private string _vsCodeExecutableOverride = string.Empty;

    [ObservableProperty]
    private string? _vsCodePathErrorMessage;

    [ObservableProperty]
    private bool _hasVsCodePathError;

    [ObservableProperty]
    private TerminalOptionItem _selectedTerminalOption;

    [ObservableProperty]
    private RefreshIntervalOptionItem _selectedRefreshIntervalOption;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isStatusSuccess;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private string _selectedCategoryId = "General";

    // --- Status & Diagnostics Observable Properties ---

    [ObservableProperty]
    private string _devDeskVersion = "1.0.0";

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _databaseHealthStatus = "Healthy (Connected)";

    [ObservableProperty]
    private string _gitStatusDisplay = "Checking...";

    [ObservableProperty]
    private bool _isGitAvailable;

    [ObservableProperty]
    private string _nodeStatusDisplay = "Checking...";

    [ObservableProperty]
    private bool _isNodeAvailable;

    [ObservableProperty]
    private string _platformDisplay = "Windows";

    public ObservableCollection<SettingsCategoryItem> Categories { get; } = new();

    public IReadOnlyList<TerminalOptionItem> TerminalOptions { get; } = new List<TerminalOptionItem>
    {
        new(PreferredTerminal.Auto, "Auto (Safe Fallback)"),
        new(PreferredTerminal.WindowsTerminal, "Windows Terminal"),
        new(PreferredTerminal.PowerShell7, "PowerShell 7"),
        new(PreferredTerminal.WindowsPowerShell, "Windows PowerShell"),
        new(PreferredTerminal.CommandPrompt, "Command Prompt")
    };

    public IReadOnlyList<RefreshIntervalOptionItem> RefreshIntervalOptions { get; } = new List<RefreshIntervalOptionItem>
    {
        new(MonitorRefreshInterval.Seconds1_5, "1.5 seconds (Default)"),
        new(MonitorRefreshInterval.Seconds2, "2 seconds"),
        new(MonitorRefreshInterval.Seconds5, "5 seconds"),
        new(MonitorRefreshInterval.Seconds10, "10 seconds")
    };

    public bool CanSave => IsDirty && !IsSaving && !HasVsCodePathError;

    public SettingsViewModel(
        ISettingsService settingsService,
        IDatabasePathProvider databasePathProvider,
        ILauncherService launcherService,
        IDialogService dialogService,
        ILogger<SettingsViewModel> logger,
        IGitService? gitService = null,
        Action<Action>? uiDispatcher = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _databasePathProvider = databasePathProvider ?? throw new ArgumentNullException(nameof(databasePathProvider));
        _launcherService = launcherService ?? throw new ArgumentNullException(nameof(launcherService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gitService = gitService;
        _uiDispatcher = uiDispatcher ?? (Action<Action>)(action =>
        {
            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        });

        _selectedTerminalOption = TerminalOptions[0];
        _selectedRefreshIntervalOption = RefreshIntervalOptions[0];

        // Initialize Categories
        Categories.Add(new SettingsCategoryItem
        {
            Id = "General",
            Title = "General",
            Subtitle = "Basic settings and behavior",
            IconKind = "General",
            IconData = "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z",
            IsSelected = true
        });
        Categories.Add(new SettingsCategoryItem
        {
            Id = "Editors",
            Title = "Editors & Terminals",
            Subtitle = "Development tools",
            IconKind = "Code",
            IconData = "M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0l4.6-4.6-4.6-4.6L16 6l6 6-6 6-1.4-1.4z"
        });
        Categories.Add(new SettingsCategoryItem
        {
            Id = "Monitoring",
            Title = "Monitoring",
            Subtitle = "Processes and system telemetry",
            IconKind = "Activity",
            IconData = "M16 6l2.29 2.29-4.88 4.88-4-4L2 16.59 3.41 18l6-6 4 4 6.3-6.29L22 12V6z"
        });
        Categories.Add(new SettingsCategoryItem
        {
            Id = "Data",
            Title = "Data & About",
            Subtitle = "Storage and application details",
            IconKind = "Database",
            IconData = "M12 2C6.48 2 2 4.01 2 6.5v11C2 19.99 6.48 22 12 22s10-2.01 10-4.5v-11C22 4.01 17.52 2 12 2zm0 2c4.42 0 8 1.57 8 2.5S16.42 9 12 9 4 7.43 4 6.5 7.58 4 12 4zm8 13.5c0 .93-3.58 2.5-8 2.5s-8-1.57-8-2.5V14.2c2.05 1.1 5.04 1.8 8 1.8s5.95-.7 8-1.8v3.3zm0-5c0 .93-3.58 2.5-8 2.5s-8-1.57-8-2.5V9.2c2.05 1.1 5.04 1.8 8 1.8s5.95-.7 8-1.8v3.3z"
        });

        // Derive version from Assembly metadata
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        DevDeskVersion = version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";

        // Read-only database path
        DatabasePath = _databasePathProvider.GetDatabasePath();

        // Platform info
        PlatformDisplay = $"{RuntimeInformation.OSDescription.Trim()} (Build {Environment.OSVersion.Version.Build})";
    }

    public async Task InitializeAsync()
    {
        await LoadSettingsAsync();
        _ = ProbeSystemIntegrationsAsync();
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            _baselineSettings = settings;

            VsCodeExecutableOverride = settings.VsCodeExecutableOverride ?? string.Empty;
            SelectedTerminalOption = TerminalOptions.FirstOrDefault(t => t.Value == settings.PreferredTerminal) ?? TerminalOptions[0];
            SelectedRefreshIntervalOption = RefreshIntervalOptions.FirstOrDefault(r => r.Value == settings.MonitorRefreshInterval) ?? RefreshIntervalOptions[0];

            ValidateVsCodePath();
            IsDirty = false;
            ClearStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings in SettingsViewModel");
            SetStatus("Failed to load settings from storage.", isError: true);
        }
    }

    public event EventHandler<string>? RequestScrollToCategory;

    [RelayCommand]
    public void SelectCategory(SettingsCategoryItem category)
    {
        if (category is null) return;
        SelectedCategoryId = category.Id;
        foreach (var c in Categories)
        {
            c.IsSelected = string.Equals(c.Id, category.Id, StringComparison.OrdinalIgnoreCase);
        }
        RequestScrollToCategory?.Invoke(this, category.Id);
    }

    public void UpdateSelectedCategoryFromScroll(string categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId)) return;
        if (string.Equals(SelectedCategoryId, categoryId, StringComparison.OrdinalIgnoreCase)) return;

        SelectedCategoryId = categoryId;
        foreach (var c in Categories)
        {
            c.IsSelected = string.Equals(c.Id, categoryId, StringComparison.OrdinalIgnoreCase);
        }
    }

    partial void OnVsCodeExecutableOverrideChanged(string value)
    {
        ValidateVsCodePath();
        CheckDirtyState();
        ClearStatus();
    }

    partial void OnSelectedTerminalOptionChanged(TerminalOptionItem value)
    {
        CheckDirtyState();
        ClearStatus();
    }

    partial void OnSelectedRefreshIntervalOptionChanged(RefreshIntervalOptionItem value)
    {
        CheckDirtyState();
        ClearStatus();
    }

    private void ValidateVsCodePath()
    {
        if (string.IsNullOrWhiteSpace(VsCodeExecutableOverride))
        {
            VsCodePathErrorMessage = null;
            HasVsCodePathError = false;
            return;
        }

        if (!ExecutablePathValidator.TryValidate(VsCodeExecutableOverride, out _, out var error))
        {
            VsCodePathErrorMessage = error;
            HasVsCodePathError = true;
        }
        else
        {
            VsCodePathErrorMessage = null;
            HasVsCodePathError = false;
        }

        OnPropertyChanged(nameof(CanSave));
    }

    private void CheckDirtyState()
    {
        string currentOverride = VsCodeExecutableOverride?.Trim() ?? string.Empty;
        string baselineOverride = _baselineSettings.VsCodeExecutableOverride?.Trim() ?? string.Empty;

        bool overrideChanged = !string.Equals(currentOverride, baselineOverride, StringComparison.OrdinalIgnoreCase);
        bool terminalChanged = SelectedTerminalOption?.Value != _baselineSettings.PreferredTerminal;
        bool refreshChanged = SelectedRefreshIntervalOption?.Value != _baselineSettings.MonitorRefreshInterval;

        IsDirty = overrideChanged || terminalChanged || refreshChanged;
        OnPropertyChanged(nameof(CanSave));
    }

    [RelayCommand]
    public void BrowseVsCodePath()
    {
        var selected = _dialogService.ShowFilePicker(
            filter: "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            title: "Select Visual Studio Code Executable (Code.exe)");

        if (!string.IsNullOrWhiteSpace(selected))
        {
            VsCodeExecutableOverride = selected;
        }
    }

    [RelayCommand]
    public void ResetVsCodeOverride()
    {
        VsCodeExecutableOverride = string.Empty;
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        if (!CanSave)
        {
            return;
        }

        try
        {
            IsSaving = true;
            OnPropertyChanged(nameof(CanSave));
            ClearStatus();

            string? validatedOverride = null;
            if (!string.IsNullOrWhiteSpace(VsCodeExecutableOverride))
            {
                if (!ExecutablePathValidator.TryValidate(VsCodeExecutableOverride, out validatedOverride, out var error))
                {
                    VsCodePathErrorMessage = error;
                    HasVsCodePathError = true;
                    SetStatus(error ?? "Invalid VS Code executable path.", isError: true);
                    return;
                }
            }

            var updatedSettings = new DevDeskSettings
            {
                VsCodeExecutableOverride = validatedOverride,
                PreferredTerminal = SelectedTerminalOption.Value,
                MonitorRefreshInterval = SelectedRefreshIntervalOption.Value
            };

            await _settingsService.SaveSettingsAsync(updatedSettings);

            _baselineSettings = updatedSettings;
            IsDirty = false;
            SetStatus("Settings saved successfully.", isError: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist settings changes");
            SetStatus("Failed to save settings: " + ex.Message, isError: true);
        }
        finally
        {
            IsSaving = false;
            OnPropertyChanged(nameof(CanSave));
        }
    }

    [RelayCommand]
    public async Task ResetToDefaultsAsync()
    {
        bool confirmed = _dialogService.ShowConfirmationDialog(
            "Reset Settings to Defaults",
            "Are you sure you want to reset all preferences to their default values?\n\nThis resets only application settings and will NOT delete projects, saved commands, or local database history.",
            "Reset to Defaults");

        if (!confirmed)
        {
            return;
        }

        try
        {
            IsSaving = true;
            OnPropertyChanged(nameof(CanSave));
            ClearStatus();

            await _settingsService.ResetToDefaultsAsync();

            _baselineSettings = new DevDeskSettings();
            VsCodeExecutableOverride = string.Empty;
            SelectedTerminalOption = TerminalOptions[0];
            SelectedRefreshIntervalOption = RefreshIntervalOptions[0];

            ValidateVsCodePath();
            IsDirty = false;
            SetStatus("Settings restored to defaults.", isError: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset settings to defaults");
            SetStatus("Failed to reset settings: " + ex.Message, isError: true);
        }
        finally
        {
            IsSaving = false;
            OnPropertyChanged(nameof(CanSave));
        }
    }

    [RelayCommand]
    public async Task OpenDataFolderAsync()
    {
        try
        {
            var dbPath = _databasePathProvider.GetDatabasePath();
            var directory = Path.GetDirectoryName(dbPath);

            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                await _launcherService.OpenInExplorerAsync(directory);
            }
            else
            {
                _dialogService.ShowMessage("Folder Not Found", $"Data directory does not exist yet:\n{directory}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open data folder");
            _dialogService.ShowMessage("Error", "Could not open data folder: " + ex.Message);
        }
    }

    [RelayCommand]
    public async Task RunDiagnosticsAsync()
    {
        try
        {
            ClearStatus();
            await ProbeSystemIntegrationsAsync();
            SetStatus("System diagnostics completed.", isError: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diagnostics routine encountered an error");
            SetStatus("Diagnostics encountered an issue: " + ex.Message, isError: true);
        }
    }

    private async Task ProbeSystemIntegrationsAsync()
    {
        await ProbeGitInternalAsync();
        ProbeNodeInternal();
    }

    private async Task ProbeGitInternalAsync()
    {
        if (_gitService is null)
        {
            _uiDispatcher(() =>
            {
                GitStatusDisplay = "Unavailable";
                IsGitAvailable = false;
            });
            return;
        }

        try
        {
            var avail = await _gitService.CheckGitAvailabilityAsync();
            _uiDispatcher(() =>
            {
                if (avail.IsAvailable)
                {
                    GitStatusDisplay = $"Git {avail.Version}";
                    IsGitAvailable = true;
                }
                else
                {
                    GitStatusDisplay = "Not Found";
                    IsGitAvailable = false;
                }
            });
        }
        catch
        {
            _uiDispatcher(() =>
            {
                GitStatusDisplay = "Error";
                IsGitAvailable = false;
            });
        }
    }

    private void ProbeNodeInternal()
    {
        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            bool found = false;
            if (!string.IsNullOrWhiteSpace(pathEnv))
            {
                var dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                foreach (var dir in dirs)
                {
                    if (File.Exists(Path.Combine(dir, "node.exe")))
                    {
                        found = true;
                        break;
                    }
                }
            }

            _uiDispatcher(() =>
            {
                if (found)
                {
                    NodeStatusDisplay = "Available (PATH)";
                    IsNodeAvailable = true;
                }
                else
                {
                    NodeStatusDisplay = "Not Found";
                    IsNodeAvailable = false;
                }
            });
        }
        catch
        {
            _uiDispatcher(() =>
            {
                NodeStatusDisplay = "Not Checked";
                IsNodeAvailable = false;
            });
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
        IsStatusSuccess = !isError;
    }

    private void ClearStatus()
    {
        StatusMessage = null;
        IsStatusError = false;
        IsStatusSuccess = false;
    }
}
