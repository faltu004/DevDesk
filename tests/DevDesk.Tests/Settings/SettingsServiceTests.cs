using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.Core.Models;
using DevDesk.Core.Settings;
using DevDesk.Infrastructure.Persistence;
using DevDesk.Infrastructure.Persistence.Database;
using DevDesk.Infrastructure.Settings;

namespace DevDesk.Tests.Settings;

public class SettingsServiceTests : IDisposable
{
    private readonly string _testDbDirectory;
    private readonly string _testDbPath;
    private readonly IDbContextFactory<DevDeskDbContext> _factory;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _testDbDirectory = Path.Combine(Path.GetTempPath(), "DevDesk_SettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDirectory);
        _testDbPath = Path.Combine(_testDbDirectory, "settings_test.db");

        var pathProvider = new DatabasePathProvider(_testDbPath);
        var options = new DbContextOptionsBuilder<DevDeskDbContext>()
            .UseSqlite(pathProvider.GetConnectionString())
            .Options;

        _factory = new TestDbContextFactory(options);

        using (var context = _factory.CreateDbContext())
        {
            context.Database.Migrate();
        }

        _service = new SettingsService(_factory, NullLogger<SettingsService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDirectory))
            {
                Directory.Delete(_testDbDirectory, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<DevDeskDbContext>
    {
        private readonly DbContextOptions<DevDeskDbContext> _options;

        public TestDbContextFactory(DbContextOptions<DevDeskDbContext> options)
        {
            _options = options;
        }

        public DevDeskDbContext CreateDbContext() => new(_options);
    }

    [Fact]
    public async Task Test01_FirstRunDefaults_ReturnsExpectedDefaultSettings()
    {
        var settings = await _service.GetSettingsAsync();

        Assert.NotNull(settings);
        Assert.Null(settings.VsCodeExecutableOverride);
        Assert.Equal(PreferredTerminal.Auto, settings.PreferredTerminal);
        Assert.Equal(MonitorRefreshInterval.Seconds1_5, settings.MonitorRefreshInterval);
    }

    [Fact]
    public async Task Test02_SaveAndLoadRoundtrip_PersistsAllFields()
    {
        string tempExe = Path.Combine(_testDbDirectory, "FakeCode02.exe");
        File.WriteAllText(tempExe, "dummy");

        var toSave = new DevDeskSettings
        {
            VsCodeExecutableOverride = tempExe,
            PreferredTerminal = PreferredTerminal.WindowsTerminal,
            MonitorRefreshInterval = MonitorRefreshInterval.Seconds5
        };

        await _service.SaveSettingsAsync(toSave);

        var loaded = await _service.GetSettingsAsync();

        Assert.Equal(toSave.VsCodeExecutableOverride, loaded.VsCodeExecutableOverride);
        Assert.Equal(PreferredTerminal.WindowsTerminal, loaded.PreferredTerminal);
        Assert.Equal(MonitorRefreshInterval.Seconds5, loaded.MonitorRefreshInterval);
    }

    [Fact]
    public async Task Test03_MissingKey_FallsBackToDefault()
    {
        await using (var context = _factory.CreateDbContext())
        {
            context.AppSettings.Add(new AppSetting
            {
                Key = "launcher.preferredTerminal",
                Value = "PowerShell7"
            });
            await context.SaveChangesAsync();
        }

        var loaded = await _service.GetSettingsAsync();

        Assert.Null(loaded.VsCodeExecutableOverride);
        Assert.Equal(PreferredTerminal.PowerShell7, loaded.PreferredTerminal);
        Assert.Equal(MonitorRefreshInterval.Seconds1_5, loaded.MonitorRefreshInterval);
    }

    [Fact]
    public async Task Test04_InvalidTerminalEnum_FallsBackToAuto()
    {
        await using (var context = _factory.CreateDbContext())
        {
            context.AppSettings.Add(new AppSetting
            {
                Key = "launcher.preferredTerminal",
                Value = "NotAValidTerminalName"
            });
            await context.SaveChangesAsync();
        }

        var loaded = await _service.GetSettingsAsync();

        Assert.Equal(PreferredTerminal.Auto, loaded.PreferredTerminal);
    }

    [Fact]
    public async Task Test05_InvalidRefreshValue_FallsBackToDefault1_5Seconds()
    {
        await using (var context = _factory.CreateDbContext())
        {
            context.AppSettings.Add(new AppSetting
            {
                Key = "monitor.refreshInterval",
                Value = "99999Seconds"
            });
            await context.SaveChangesAsync();
        }

        var loaded = await _service.GetSettingsAsync();

        Assert.Equal(MonitorRefreshInterval.Seconds1_5, loaded.MonitorRefreshInterval);
    }

    [Fact]
    public async Task Test06_OneCorruptKey_DoesNotInvalidateOtherSettings()
    {
        await using (var context = _factory.CreateDbContext())
        {
            context.AppSettings.Add(new AppSetting
            {
                Key = "launcher.vscodeExecutableOverride",
                Value = @"C:\Valid\Path\Code.exe"
            });
            context.AppSettings.Add(new AppSetting
            {
                Key = "launcher.preferredTerminal",
                Value = "CorruptGarbageValue"
            });
            context.AppSettings.Add(new AppSetting
            {
                Key = "monitor.refreshInterval",
                Value = "Seconds10"
            });
            await context.SaveChangesAsync();
        }

        var loaded = await _service.GetSettingsAsync();

        Assert.Equal(@"C:\Valid\Path\Code.exe", loaded.VsCodeExecutableOverride);
        Assert.Equal(PreferredTerminal.Auto, loaded.PreferredTerminal);
        Assert.Equal(MonitorRefreshInterval.Seconds10, loaded.MonitorRefreshInterval);
    }

    [Fact]
    public void Test07_ExecutablePathValidator_ValidAbsoluteExeAccepted()
    {
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string notepadPath = Path.Combine(winDir, "notepad.exe");

        if (File.Exists(notepadPath))
        {
            bool valid = ExecutablePathValidator.TryValidate(notepadPath, out var normalized, out var error);
            Assert.True(valid);
            Assert.Null(error);
            Assert.Equal(Path.GetFullPath(notepadPath), normalized);
        }
    }

    [Fact]
    public void Test08_ExecutablePathValidator_NonExistentPathRejected()
    {
        string fakePath = @"C:\NonExistentDirectory_XYZ123\Code.exe";
        bool valid = ExecutablePathValidator.TryValidate(fakePath, out var normalized, out var error);

        Assert.False(valid);
        Assert.Null(normalized);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Tools\script.cmd")]
    [InlineData(@"C:\Tools\script.bat")]
    [InlineData(@"C:\Tools\script.ps1")]
    [InlineData(@"C:\Tools\script.vbs")]
    [InlineData(@"C:\Tools\script.sh")]
    public void Test09_10_11_ExecutablePathValidator_NonExeRejected(string scriptPath)
    {
        bool valid = ExecutablePathValidator.TryValidate(scriptPath, out var normalized, out var error);

        Assert.False(valid);
        Assert.Null(normalized);
        Assert.Contains(".exe", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test14_SettingsSurviveServiceRecreation()
    {
        // Service 1 saves
        await _service.SaveSettingsAsync(new DevDeskSettings
        {
            PreferredTerminal = PreferredTerminal.CommandPrompt,
            MonitorRefreshInterval = MonitorRefreshInterval.Seconds2
        });

        // Service 2 reads from same SQLite database
        var service2 = new SettingsService(_factory, NullLogger<SettingsService>.Instance);
        var loaded = await service2.GetSettingsAsync();

        Assert.Equal(PreferredTerminal.CommandPrompt, loaded.PreferredTerminal);
        Assert.Equal(MonitorRefreshInterval.Seconds2, loaded.MonitorRefreshInterval);
    }

    [Fact]
    public async Task Test15_16_17_ResetToDefaults_RemovesOnlyPhase13Keys_ProjectsAndCommandsUntouched()
    {
        var projectId = Guid.NewGuid();

        await using (var context = _factory.CreateDbContext())
        {
            context.DeveloperProjects.Add(new DeveloperProject
            {
                Id = projectId,
                Name = "Important Dev Project",
                Path = @"C:\Projects\Important",
                Framework = ".NET 10",
                Language = "C#"
            });

            context.SavedCommands.Add(new SavedCommand
            {
                Id = Guid.NewGuid(),
                Name = "Build Production",
                Executable = "dotnet",
                Arguments = new[] { "build", "-c", "Release" }
            });

            context.AppSettings.Add(new AppSetting { Key = "launcher.vscodeExecutableOverride", Value = @"C:\Code\Code.exe" });
            context.AppSettings.Add(new AppSetting { Key = "launcher.preferredTerminal", Value = "PowerShell7" });
            context.AppSettings.Add(new AppSetting { Key = "monitor.refreshInterval", Value = "Seconds5" });
            context.AppSettings.Add(new AppSetting { Key = "unrelated.customKey", Value = "shouldRemain" });

            await context.SaveChangesAsync();
        }

        // Reset
        await _service.ResetToDefaultsAsync();

        // Verify state
        await using (var context = _factory.CreateDbContext())
        {
            var project = await context.DeveloperProjects.FindAsync(projectId);
            Assert.NotNull(project);
            Assert.Equal("Important Dev Project", project.Name);

            var command = await context.SavedCommands.FirstOrDefaultAsync();
            Assert.NotNull(command);
            Assert.Equal("Build Production", command.Name);

            var unrelated = await context.AppSettings.FirstOrDefaultAsync(s => s.Key == "unrelated.customKey");
            Assert.NotNull(unrelated);
            Assert.Equal("shouldRemain", unrelated.Value);

            var phase13Keys = await context.AppSettings
                .Where(s => s.Key == "launcher.vscodeExecutableOverride" ||
                            s.Key == "launcher.preferredTerminal" ||
                            s.Key == "monitor.refreshInterval")
                .ToListAsync();

            Assert.Empty(phase13Keys);
        }

        var current = _service.GetCurrentSettings();
        Assert.Null(current.VsCodeExecutableOverride);
        Assert.Equal(PreferredTerminal.Auto, current.PreferredTerminal);
        Assert.Equal(MonitorRefreshInterval.Seconds1_5, current.MonitorRefreshInterval);
    }

    [Fact]
    public async Task Test18_SettingsStorage_ContainsNoSecretFields()
    {
        string tempExe = Path.Combine(_testDbDirectory, "FakeCode18.exe");
        File.WriteAllText(tempExe, "dummy");

        await _service.SaveSettingsAsync(new DevDeskSettings
        {
            VsCodeExecutableOverride = tempExe,
            PreferredTerminal = PreferredTerminal.WindowsPowerShell,
            MonitorRefreshInterval = MonitorRefreshInterval.Seconds2
        });

        await using var context = _factory.CreateDbContext();
        var allSettings = await context.AppSettings.ToListAsync();

        foreach (var row in allSettings)
        {
            Assert.DoesNotContain("password", row.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", row.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", row.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("apikey", row.Key, StringComparison.OrdinalIgnoreCase);
        }
    }
}
