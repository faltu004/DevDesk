using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.Core.Launchers;
using DevDesk.Infrastructure.Launchers;

namespace DevDesk.Tests;

public sealed class LauncherServiceTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly FakeProcessRunner _processRunner;
    private readonly FakeExternalToolLocator _toolLocator;
    private readonly LauncherService _launcherService;

    public LauncherServiceTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "DevDesk_LauncherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBaseDir);

        _processRunner = new FakeProcessRunner();
        _toolLocator = new FakeExternalToolLocator
        {
            VsCodePath = @"C:\Mock\Microsoft VS Code\Code.exe",
            ExplorerPath = @"C:\Windows\explorer.exe",
            PreferredTerminal = new TerminalLaunchTarget(@"C:\Mock\wt.exe", TerminalType.WindowsTerminal)
        };

        _launcherService = new LauncherService(
            _toolLocator,
            _processRunner,
            NullLogger<LauncherService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testBaseDir))
            {
                Directory.Delete(_testBaseDir, true);
            }
        }
        catch
        {
            // Ignore cleanup failure in temp directory
        }
    }

    [Fact]
    public async Task OpenInVsCode_MissingProjectDirectory_ReturnsDirectoryNotFound()
    {
        var nonExistentPath = Path.Combine(_testBaseDir, "does-not-exist");

        var result = await _launcherService.OpenInVsCodeAsync(nonExistentPath);

        Assert.False(result.Success);
        Assert.Equal(LaunchFailureReason.DirectoryNotFound, result.FailureReason);
        Assert.Equal("The project folder could not be found.", result.ErrorMessage);
        Assert.Equal(0, _processRunner.CallCount);
    }

    [Fact]
    public async Task OpenInExplorer_MissingProjectDirectory_ReturnsDirectoryNotFound()
    {
        var nonExistentPath = Path.Combine(_testBaseDir, "does-not-exist");

        var result = await _launcherService.OpenInExplorerAsync(nonExistentPath);

        Assert.False(result.Success);
        Assert.Equal(LaunchFailureReason.DirectoryNotFound, result.FailureReason);
        Assert.Equal("The project folder could not be found.", result.ErrorMessage);
        Assert.Equal(0, _processRunner.CallCount);
    }

    [Fact]
    public async Task OpenTerminal_MissingProjectDirectory_ReturnsDirectoryNotFound()
    {
        var nonExistentPath = Path.Combine(_testBaseDir, "does-not-exist");

        var result = await _launcherService.OpenTerminalAsync(nonExistentPath);

        Assert.False(result.Success);
        Assert.Equal(LaunchFailureReason.DirectoryNotFound, result.FailureReason);
        Assert.Equal("The project folder could not be found.", result.ErrorMessage);
        Assert.Equal(0, _processRunner.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OpenInVsCode_EmptyOrWhitespacePath_ReturnsInvalidPath(string invalidPath)
    {
        var result = await _launcherService.OpenInVsCodeAsync(invalidPath);

        Assert.False(result.Success);
        Assert.Equal(LaunchFailureReason.InvalidPath, result.FailureReason);
        Assert.Equal("A project path was not specified.", result.ErrorMessage);
        Assert.Equal(0, _processRunner.CallCount);
    }

    [Fact]
    public async Task OpenInVsCode_NormalizesProjectPathBeforeLaunching()
    {
        var projDir = Path.Combine(_testBaseDir, "normalized-test");
        Directory.CreateDirectory(projDir);

        // Path with trailing directory separator
        var pathWithTrailingSlash = projDir + Path.DirectorySeparatorChar;

        var result = await _launcherService.OpenInVsCodeAsync(pathWithTrailingSlash);

        Assert.True(result.Success);
        Assert.NotNull(_processRunner.LastStartInfo);
        Assert.Equal(projDir, _processRunner.LastStartInfo.WorkingDirectory);
        Assert.Single(_processRunner.LastStartInfo.ArgumentList);
        Assert.Equal(projDir, _processRunner.LastStartInfo.ArgumentList[0]);
    }

    [Fact]
    public async Task OpenInVsCode_NotInstalled_ReturnsApplicationNotFound()
    {
        var projDir = Path.Combine(_testBaseDir, "vscode-missing-app");
        Directory.CreateDirectory(projDir);

        _toolLocator.VsCodePath = null; // Simulate VS Code missing

        var result = await _launcherService.OpenInVsCodeAsync(projDir);

        Assert.False(result.Success);
        Assert.Equal(LaunchFailureReason.ApplicationNotFound, result.FailureReason);
        Assert.Equal("Visual Studio Code was not found on this computer.", result.ErrorMessage);
        Assert.Equal(0, _processRunner.CallCount);
    }

    [Fact]
    public async Task OpenInVsCode_Installed_ReceivesCorrectExecutableAndArguments()
    {
        var projDir = Path.Combine(_testBaseDir, "vscode-app");
        Directory.CreateDirectory(projDir);

        var result = await _launcherService.OpenInVsCodeAsync(projDir);

        Assert.True(result.Success);
        Assert.NotNull(_processRunner.LastStartInfo);
        Assert.Equal(@"C:\Mock\Microsoft VS Code\Code.exe", _processRunner.LastStartInfo.FileName);
        Assert.Equal(projDir, _processRunner.LastStartInfo.WorkingDirectory);
        Assert.Single(_processRunner.LastStartInfo.ArgumentList);
        Assert.Equal(projDir, _processRunner.LastStartInfo.ArgumentList[0]);
        Assert.False(_processRunner.LastStartInfo.UseShellExecute);
        Assert.True(_processRunner.LastStartInfo.CreateNoWindow);
    }

    [Fact]
    public async Task OpenInExplorer_Installed_ReceivesCorrectExecutableAndArguments()
    {
        var projDir = Path.Combine(_testBaseDir, "explorer-app");
        Directory.CreateDirectory(projDir);

        var result = await _launcherService.OpenInExplorerAsync(projDir);

        Assert.True(result.Success);
        Assert.NotNull(_processRunner.LastStartInfo);
        Assert.Equal(@"C:\Windows\explorer.exe", _processRunner.LastStartInfo.FileName);
        Assert.Equal(projDir, _processRunner.LastStartInfo.WorkingDirectory);
        Assert.Single(_processRunner.LastStartInfo.ArgumentList);
        Assert.Equal(projDir, _processRunner.LastStartInfo.ArgumentList[0]);
        Assert.False(_processRunner.LastStartInfo.UseShellExecute);
    }

    [Fact]
    public async Task OpenTerminal_WindowsTerminal_ReceivesDashDAndWorkingDirectory()
    {
        var projDir = Path.Combine(_testBaseDir, "wt-app");
        Directory.CreateDirectory(projDir);

        _toolLocator.PreferredTerminal = new TerminalLaunchTarget(@"C:\Mock\wt.exe", TerminalType.WindowsTerminal);

        var result = await _launcherService.OpenTerminalAsync(projDir);

        Assert.True(result.Success);
        Assert.NotNull(_processRunner.LastStartInfo);
        Assert.Equal(@"C:\Mock\wt.exe", _processRunner.LastStartInfo.FileName);
        Assert.Equal(projDir, _processRunner.LastStartInfo.WorkingDirectory);
        Assert.Equal(2, _processRunner.LastStartInfo.ArgumentList.Count);
        Assert.Equal("-d", _processRunner.LastStartInfo.ArgumentList[0]);
        Assert.Equal(projDir, _processRunner.LastStartInfo.ArgumentList[1]);
        Assert.False(_processRunner.LastStartInfo.UseShellExecute);
        Assert.False(_processRunner.LastStartInfo.CreateNoWindow); // Visible window
    }

    [Fact]
    public async Task OpenTerminal_PowerShell7_ReceivesNoExecutionFlags()
    {
        var projDir = Path.Combine(_testBaseDir, "pwsh-app");
        Directory.CreateDirectory(projDir);

        _toolLocator.PreferredTerminal = new TerminalLaunchTarget(@"C:\Program Files\PowerShell\7\pwsh.exe", TerminalType.PowerShell7);

        var result = await _launcherService.OpenTerminalAsync(projDir);

        Assert.True(result.Success);
        Assert.NotNull(_processRunner.LastStartInfo);
        Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe", _processRunner.LastStartInfo.FileName);
        Assert.Equal(projDir, _processRunner.LastStartInfo.WorkingDirectory);
        Assert.Empty(_processRunner.LastStartInfo.ArgumentList); // No command passed!
        Assert.False(_processRunner.LastStartInfo.CreateNoWindow);
    }

    [Fact]
    public async Task OpenTerminal_WindowsPowerShell_ReceivesNoLogoAndWorkingDirectory()
    {
        var projDir = Path.Combine(_testBaseDir, "powershell-app");
        Directory.CreateDirectory(projDir);

        _toolLocator.PreferredTerminal = new TerminalLaunchTarget(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", TerminalType.WindowsPowerShell);

        var result = await _launcherService.OpenTerminalAsync(projDir);

        Assert.True(result.Success);
        Assert.NotNull(_processRunner.LastStartInfo);
        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", _processRunner.LastStartInfo.FileName);
        Assert.Equal(projDir, _processRunner.LastStartInfo.WorkingDirectory);
        Assert.Single(_processRunner.LastStartInfo.ArgumentList);
        Assert.Equal("-NoLogo", _processRunner.LastStartInfo.ArgumentList[0]);
        Assert.False(_processRunner.LastStartInfo.CreateNoWindow);
    }

    [Fact]
    public async Task OpenTerminal_NoTerminalInstalled_ReturnsApplicationNotFound()
    {
        var projDir = Path.Combine(_testBaseDir, "no-terminal-app");
        Directory.CreateDirectory(projDir);

        _toolLocator.PreferredTerminal = null; // No terminal found

        var result = await _launcherService.OpenTerminalAsync(projDir);

        Assert.False(result.Success);
        Assert.Equal(LaunchFailureReason.ApplicationNotFound, result.FailureReason);
        Assert.Equal("No supported terminal (Windows Terminal, PowerShell) could be found on this computer.", result.ErrorMessage);
        Assert.Equal(0, _processRunner.CallCount);
    }

    [Fact]
    public async Task Launchers_PathContainingSpaces_PassedVerbatimInArgumentList()
    {
        var spaceDir = Path.Combine(_testBaseDir, "My Special Projects Directory");
        Directory.CreateDirectory(spaceDir);

        var result = await _launcherService.OpenInVsCodeAsync(spaceDir);

        Assert.True(result.Success);
        Assert.NotNull(_processRunner.LastStartInfo);
        Assert.Equal(spaceDir, _processRunner.LastStartInfo.ArgumentList[0]);
        Assert.Equal(spaceDir, _processRunner.LastStartInfo.WorkingDirectory);
    }

    [Fact]
    public async Task Launchers_PathWithShellSensitiveCharacters_PassedSafelyAsLiteralArgument()
    {
        // Valid Windows directory name containing shell metacharacters: spaces, &, ;, (), %, !, ^
        var sensitiveDir = Path.Combine(_testBaseDir, "proj (v1.0) & foo;bar %USER% !alert^test");
        Directory.CreateDirectory(sensitiveDir);

        // 1. VS Code
        var vsCodeResult = await _launcherService.OpenInVsCodeAsync(sensitiveDir);
        Assert.True(vsCodeResult.Success);
        Assert.Equal(sensitiveDir, _processRunner.LastStartInfo!.ArgumentList[0]);

        // 2. Explorer
        var explorerResult = await _launcherService.OpenInExplorerAsync(sensitiveDir);
        Assert.True(explorerResult.Success);
        Assert.Equal(sensitiveDir, _processRunner.LastStartInfo!.ArgumentList[0]);

        // 3. Terminal
        var terminalResult = await _launcherService.OpenTerminalAsync(sensitiveDir);
        Assert.True(terminalResult.Success);
        Assert.Equal("-d", _processRunner.LastStartInfo!.ArgumentList[0]);
        Assert.Equal(sensitiveDir, _processRunner.LastStartInfo!.ArgumentList[1]);
        Assert.Equal(sensitiveDir, _processRunner.LastStartInfo!.WorkingDirectory);
    }

    [Fact]
    public async Task OpenInVsCode_ProcessRunnerReturnsFalse_ReturnsProcessStartFailed()
    {
        var projDir = Path.Combine(_testBaseDir, "runner-failed-app");
        Directory.CreateDirectory(projDir);

        _processRunner.ResultToReturn = false;

        var result = await _launcherService.OpenInVsCodeAsync(projDir);

        Assert.False(result.Success);
        Assert.Equal(LaunchFailureReason.ProcessStartFailed, result.FailureReason);
        Assert.Contains("Please check system permissions", result.ErrorMessage);
    }

    [Fact]
    public async Task OpenInExplorer_ProcessStartThrowsWin32Exception_ReturnsProcessStartFailedWithoutCrashing()
    {
        var projDir = Path.Combine(_testBaseDir, "win32-error-app");
        Directory.CreateDirectory(projDir);

        _processRunner.ExceptionToThrow = new Win32Exception(5, "Access is denied");

        var result = await _launcherService.OpenInExplorerAsync(projDir);

        Assert.False(result.Success);
        Assert.Equal(LaunchFailureReason.ProcessStartFailed, result.FailureReason);
        Assert.Contains("Please check system permissions", result.ErrorMessage);
    }

    [Fact]
    public async Task Launchers_CancellationRequested_ThrowsOperationCanceledException()
    {
        var projDir = Path.Combine(_testBaseDir, "canceled-app");
        Directory.CreateDirectory(projDir);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-canceled token

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _launcherService.OpenInVsCodeAsync(projDir, cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _launcherService.OpenInExplorerAsync(projDir, cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _launcherService.OpenTerminalAsync(projDir, cts.Token);
        });

        Assert.Equal(0, _processRunner.CallCount);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public ProcessStartInfo? LastStartInfo { get; private set; }
        public int CallCount { get; private set; }
        public bool ResultToReturn { get; set; } = true;
        public Exception? ExceptionToThrow { get; set; }

        public bool Start(ProcessStartInfo startInfo)
        {
            CallCount++;
            LastStartInfo = startInfo;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return ResultToReturn;
        }
    }

    private sealed class FakeExternalToolLocator : IExternalToolLocator
    {
        public string? VsCodePath { get; set; }
        public string? ExplorerPath { get; set; }
        public TerminalLaunchTarget? PreferredTerminal { get; set; }

        public string? FindVsCodeExecutable() => VsCodePath;
        public string? FindExplorerExecutable() => ExplorerPath;
        public TerminalLaunchTarget? FindPreferredTerminal() => PreferredTerminal;
        public TerminalLaunchTarget? FindPreferredTerminal(DevDesk.Core.Settings.PreferredTerminal preference) => PreferredTerminal;
    }
}
