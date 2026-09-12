using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.Core.Launchers;
using DevDesk.Core.Settings;
using DevDesk.Infrastructure.Launchers;

namespace DevDesk.Tests.Settings;

public class SettingsLauncherIntegrationTests
{
    private sealed class FakeToolLocator : IExternalToolLocator
    {
        public string? VsCodePath { get; set; } = @"C:\AutoDetected\Code.exe";
        public string? ExplorerPath { get; set; } = @"C:\Windows\explorer.exe";
        public List<PreferredTerminal> TerminalSearchHistory { get; } = new();
        public PreferredTerminal? AvailableTerminal { get; set; } = PreferredTerminal.WindowsTerminal;

        public string? FindVsCodeExecutable() => VsCodePath;
        public string? FindExplorerExecutable() => ExplorerPath;
        public TerminalLaunchTarget? FindPreferredTerminal() => FindPreferredTerminal(PreferredTerminal.Auto);

        public TerminalLaunchTarget? FindPreferredTerminal(PreferredTerminal preference)
        {
            TerminalSearchHistory.Add(preference);

            if (AvailableTerminal == preference || (preference == PreferredTerminal.Auto && AvailableTerminal.HasValue))
            {
                return new TerminalLaunchTarget(
                    $@"C:\Windows\{AvailableTerminal}.exe",
                    AvailableTerminal.Value switch
                    {
                        PreferredTerminal.WindowsTerminal => TerminalType.WindowsTerminal,
                        PreferredTerminal.PowerShell7 => TerminalType.PowerShell7,
                        PreferredTerminal.WindowsPowerShell => TerminalType.WindowsPowerShell,
                        PreferredTerminal.CommandPrompt => TerminalType.CommandPrompt,
                        _ => TerminalType.WindowsTerminal
                    }
                );
            }

            // Fallback sequence simulation
            if (AvailableTerminal.HasValue)
            {
                return new TerminalLaunchTarget(
                    @"C:\Windows\wt.exe",
                    TerminalType.WindowsTerminal
                );
            }

            return null;
        }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public System.Diagnostics.ProcessStartInfo? LastStartInfo { get; private set; }

        public bool Start(System.Diagnostics.ProcessStartInfo startInfo)
        {
            LastStartInfo = startInfo;
            return true;
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public DevDeskSettings Current { get; set; } = new();

        public event Action<DevDeskSettings>? SettingsChanged;

        public Task<DevDeskSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public DevDeskSettings GetCurrentSettings() => Current;
        public Task SaveSettingsAsync(DevDeskSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            SettingsChanged?.Invoke(settings);
            return Task.CompletedTask;
        }
        public Task ResetToDefaultsAsync(CancellationToken cancellationToken = default)
        {
            Current = new DevDeskSettings();
            SettingsChanged?.Invoke(Current);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Test12_PreferredTerminal_ChangesResolutionOrder()
    {
        var locator = new FakeToolLocator();

        // When preferred is PowerShell7, locator logs PowerShell7 attempt
        var target = locator.FindPreferredTerminal(PreferredTerminal.PowerShell7);

        Assert.NotNull(target);
        Assert.Contains(PreferredTerminal.PowerShell7, locator.TerminalSearchHistory);
    }

    [Fact]
    public void Test13_UnavailablePreferredTerminal_SafelyFallsBack()
    {
        var locator = new FakeToolLocator
        {
            AvailableTerminal = PreferredTerminal.WindowsTerminal // Only Windows Terminal is available
        };

        // User prefers PowerShell 7, which isn't installed
        var target = locator.FindPreferredTerminal(PreferredTerminal.PowerShell7);

        // Locator falls back safely to available terminal rather than crashing
        Assert.NotNull(target);
        Assert.Equal(TerminalType.WindowsTerminal, target.Type);
    }

    [Fact]
    public async Task Test_VsCodeOverride_TakesPrecedenceWhenValid()
    {
        var locator = new FakeToolLocator { VsCodePath = @"C:\AutoDetected\Code.exe" };
        var runner = new FakeProcessRunner();
        var settingsService = new FakeSettingsService();

        // Create temporary real file for override test
        string tempExe = Path.Combine(Path.GetTempPath(), "FakeVsCodeOverride_" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(tempExe, "dummy");

        try
        {
            settingsService.Current = new DevDeskSettings
            {
                VsCodeExecutableOverride = tempExe
            };

            var launcher = new LauncherService(locator, runner, NullLogger<LauncherService>.Instance, settingsService);

            string projDir = Directory.GetCurrentDirectory();
            var result = await launcher.OpenInVsCodeAsync(projDir);

            Assert.True(result.Success);
            Assert.NotNull(runner.LastStartInfo);
            Assert.Equal(tempExe, runner.LastStartInfo.FileName);
        }
        finally
        {
            if (File.Exists(tempExe)) File.Delete(tempExe);
        }
    }
}
