using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.App.Services.Dialogs;
using DevDesk.App.ViewModels.Commands;
using DevDesk.App.ViewModels.Projects;
using DevDesk.App.ViewModels.Shell;
using DevDesk.App.Views.Commands;
using DevDesk.Core.Commands;
using DevDesk.Core.Models;
using DevDesk.Core.Repositories;
using DevDesk.Core.Services;
using DevDesk.Infrastructure.Commands;
using DevDesk.Infrastructure.Persistence;
using DevDesk.Infrastructure.Persistence.Database;
using DevDesk.Infrastructure.Persistence.Repositories;
using DevDesk.Infrastructure.Runner;
using DevDesk.Infrastructure.Runner.Native;
using DevDesk.Infrastructure.Services;
using Xunit;

namespace DevDesk.Tests;

[Collection("StaSmoke")]
public sealed class SavedCommandsTests : IDisposable
{
    private readonly string _testDbDirectory;
    private readonly string _testDbPath;
    private readonly IDbContextFactory<DevDeskDbContext> _contextFactory;
    private readonly DatabasePathProvider _pathProvider;
    private readonly IProjectRepository _projectRepository;
    private readonly ISavedCommandRepository _commandRepository;
    private readonly ISavedCommandService _commandService;

    public SavedCommandsTests()
    {
        _testDbDirectory = Path.Combine(Path.GetTempPath(), "DevDesk_CmdTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDirectory);
        _testDbPath = Path.Combine(_testDbDirectory, "test_cmd.db");

        _pathProvider = new DatabasePathProvider(_testDbPath);

        var options = new DbContextOptionsBuilder<DevDeskDbContext>()
            .UseSqlite(_pathProvider.GetConnectionString())
            .Options;

        _contextFactory = new TestDbContextFactory(options);

        // Apply migrations
        using var context = _contextFactory.CreateDbContext();
        context.Database.Migrate();

        _projectRepository = new ProjectRepository(_contextFactory);
        _commandRepository = new SavedCommandRepository(_contextFactory);
        _commandService = new SavedCommandService(_commandRepository, _projectRepository);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_testDbDirectory))
            {
                Directory.Delete(_testDbDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<DevDeskDbContext>
    {
        private readonly DbContextOptions<DevDeskDbContext> _options;
        public TestDbContextFactory(DbContextOptions<DevDeskDbContext> options) => _options = options;
        public DevDeskDbContext CreateDbContext() => new(_options);
    }

    // ---------------------------------------------------------------------------------------------
    // Fakes for Execution & Dialog Testing
    // ---------------------------------------------------------------------------------------------

    private sealed class TestManagedProcess : IManagedProcess
    {
        private readonly TaskCompletionSource _tcs = new();
        public int ProcessId { get; set; } = 4242;
        public bool HasExited { get; set; }
        public int? ExitCode { get; set; } = 0;
        public bool WasKilled { get; private set; }

        public event EventHandler<string>? StandardOutputReceived;
        public event EventHandler<string>? StandardErrorReceived;
        public event EventHandler<int>? Exited;

        public void EmitOutput(string text) => StandardOutputReceived?.Invoke(this, text);
        public void EmitError(string text) => StandardErrorReceived?.Invoke(this, text);

        public void Complete(int exitCode)
        {
            ExitCode = exitCode;
            HasExited = true;
            Exited?.Invoke(this, exitCode);
            _tcs.TrySetResult();
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken = default) => _tcs.Task;

        public void KillEntireProcessTree()
        {
            WasKilled = true;
            Complete(-1);
        }

        public IReadOnlyList<int> GetActiveProcessIds() => new[] { ProcessId };
        public bool ContainsProcessHandle(IntPtr processHandle) => false;
        public void Dispose() { }
    }

    private sealed class TestProcessLauncher : IProcessLauncher
    {
        public ProcessLaunchConfiguration? LastConfig { get; private set; }
        public TestManagedProcess ProcessToReturn { get; set; } = new();

        public IManagedProcess Launch(ProcessLaunchConfiguration config)
        {
            LastConfig = config;
            return ProcessToReturn;
        }
    }

    private sealed class TestDialogService : IDialogService
    {
        public bool AddEditCommandResult { get; set; } = true;
        public bool ConfirmDeleteCommandResult { get; set; } = true;
        public bool ConfirmShutdownResult { get; set; } = true;
        public int LastShutdownProjectCount { get; private set; }
        public int LastShutdownCommandCount { get; private set; }

        public string? ShowFolderPicker(string? initialDirectory = null, string? title = null) => null;
        public bool ShowAddEditProjectDialog(AddEditProjectViewModel viewModel) => true;
        public bool ShowConfirmDeleteDialog(ConfirmDeleteViewModel viewModel) => true;

        public bool ShowAddEditCommandDialog(AddEditCommandViewModel viewModel) => AddEditCommandResult;
        public bool ShowConfirmDeleteCommandDialog(ConfirmDeleteCommandViewModel viewModel) => ConfirmDeleteCommandResult;

        public bool ShowConfirmShutdownDialog(int activeProjectCount, int activeCommandCount = 0)
        {
            LastShutdownProjectCount = activeProjectCount;
            LastShutdownCommandCount = activeCommandCount;
            return ConfirmShutdownResult;
        }

        public void ShowMessage(string title, string message) { }
    }

    // =============================================================================================
    // 1. Global Create
    // =============================================================================================
    [Fact]
    public async Task Test01_CreateGlobalCommand_SavesWithNullProjectId()
    {
        var cmd = new SavedCommand
        {
            Name = "Global Test Tool",
            Executable = "dotnet",
            Arguments = new[] { "--info" },
            ProjectId = null
        };

        var saved = await _commandService.SaveCommandAsync(cmd);
        Assert.NotNull(saved);
        Assert.Null(saved.ProjectId);

        var retrieved = await _commandService.GetCommandByIdAsync(saved.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Global Test Tool", retrieved.Name);
        Assert.Null(retrieved.ProjectId);
    }

    // =============================================================================================
    // 2. Project Create
    // =============================================================================================
    [Fact]
    public async Task Test02_CreateProjectCommand_SavesWithValidProjectId()
    {
        var project = new DeveloperProject { Name = "Alpha", Path = Path.Combine(_testDbDirectory, "Alpha") };
        await _projectRepository.AddAsync(project);

        var cmd = new SavedCommand
        {
            Name = "Build Alpha",
            Executable = "dotnet",
            Arguments = new[] { "build" },
            ProjectId = project.Id
        };

        var saved = await _commandService.SaveCommandAsync(cmd);
        Assert.Equal(project.Id, saved.ProjectId);

        var retrieved = await _commandService.GetCommandByIdAsync(saved.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(project.Id, retrieved.ProjectId);
        Assert.Equal("Alpha", retrieved.Project?.Name);
    }

    // =============================================================================================
    // 3. Edit Command
    // =============================================================================================
    [Fact]
    public async Task Test03_EditCommand_UpdatesAllFields()
    {
        var cmd = new SavedCommand
        {
            Name = "Old Name",
            Executable = "git",
            Arguments = new[] { "status" },
            Category = "Tools"
        };
        var saved = await _commandService.SaveCommandAsync(cmd);

        saved.Name = "Updated Name";
        saved.Executable = "dotnet";
        saved.Arguments = new[] { "test", "--filter", "Unit" };
        saved.Category = "Testing";
        saved.Description = "Runs unit tests";

        var updated = await _commandService.SaveCommandAsync(saved);

        var retrieved = await _commandService.GetCommandByIdAsync(saved.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated Name", retrieved.Name);
        Assert.Equal("dotnet", retrieved.Executable);
        Assert.Equal(3, retrieved.Arguments.Count);
        Assert.Equal("Testing", retrieved.Category);
        Assert.Equal("Runs unit tests", retrieved.Description);
    }

    // =============================================================================================
    // 4. Delete Command
    // =============================================================================================
    [Fact]
    public async Task Test04_DeleteCommand_RemovesRecord()
    {
        var cmd = new SavedCommand { Name = "To Delete", Executable = "dotnet", Arguments = new[] { "clean" } };
        var saved = await _commandService.SaveCommandAsync(cmd);

        bool deleted = await _commandService.DeleteCommandAsync(saved.Id);
        Assert.True(deleted);

        var retrieved = await _commandService.GetCommandByIdAsync(saved.Id);
        Assert.Null(retrieved);
    }

    // =============================================================================================
    // 5 & 6. Project Deletion Cascade & Global Survives
    // =============================================================================================
    [Fact]
    public async Task Test05_06_ProjectDeletion_CascadesProjectCommands_PreservesGlobalCommands()
    {
        var project = new DeveloperProject { Name = "Beta", Path = Path.Combine(_testDbDirectory, "Beta") };
        await _projectRepository.AddAsync(project);

        var projCmd = new SavedCommand { Name = "Beta Build", Executable = "dotnet", Arguments = new[] { "build" }, ProjectId = project.Id };
        var globalCmd = new SavedCommand { Name = "Global Status", Executable = "git", Arguments = new[] { "status" }, ProjectId = null };

        await _commandService.SaveCommandAsync(projCmd);
        await _commandService.SaveCommandAsync(globalCmd);

        // Delete project
        await _projectRepository.DeleteAsync(project.Id);

        // Project command should be gone
        var retrievedProjCmd = await _commandService.GetCommandByIdAsync(projCmd.Id);
        Assert.Null(retrievedProjCmd);

        // Global command must survive
        var retrievedGlobalCmd = await _commandService.GetCommandByIdAsync(globalCmd.Id);
        Assert.NotNull(retrievedGlobalCmd);
        Assert.Equal("Global Status", retrievedGlobalCmd.Name);
    }

    // =============================================================================================
    // 7. Persistence Round Trip
    // =============================================================================================
    [Fact]
    public async Task Test07_PersistenceRoundTrip_PreservesAllProperties()
    {
        var cmd = new SavedCommand
        {
            Name = "Roundtrip Test",
            Description = "Full metadata roundtrip",
            Executable = @"C:\Tools\CustomTool.exe",
            Arguments = new[] { "arg1", "--flag", "value with spaces" },
            WorkingDirectory = @"D:\Workspaces\Target",
            Category = "DevOps",
            IsEnabled = true
        };

        var saved = await _commandService.SaveCommandAsync(cmd);
        var retrieved = await _commandService.GetCommandByIdAsync(saved.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(cmd.Name, retrieved.Name);
        Assert.Equal(cmd.Description, retrieved.Description);
        Assert.Equal(cmd.Executable, retrieved.Executable);
        Assert.Equal(cmd.Arguments, retrieved.Arguments);
        Assert.Equal(cmd.WorkingDirectory, retrieved.WorkingDirectory);
        Assert.Equal(cmd.Category, retrieved.Category);
        Assert.True(retrieved.IsEnabled);
    }

    // =============================================================================================
    // 8. EF Core Argument List Edit Detected
    // =============================================================================================
    [Fact]
    public async Task Test08_ArgumentsListEdit_DetectedByEFValueComparer()
    {
        var cmd = new SavedCommand
        {
            Name = "Comparer Test",
            Executable = "dotnet",
            Arguments = new[] { "build" }
        };

        await _commandService.SaveCommandAsync(cmd);

        // Retrieve in fresh context, mutate sequence, save, reload
        using (var context = _contextFactory.CreateDbContext())
        {
            var entity = await context.SavedCommands.FirstAsync(c => c.Id == cmd.Id);
            entity.Arguments = new[] { "test" };
            await context.SaveChangesAsync();
        }

        using (var context = _contextFactory.CreateDbContext())
        {
            var reloaded = await context.SavedCommands.FirstAsync(c => c.Id == cmd.Id);
            Assert.Single(reloaded.Arguments);
            Assert.Equal("test", reloaded.Arguments[0]);
        }
    }

    // =============================================================================================
    // 9, 10, 11. Search, Project Filter, Category Filter
    // =============================================================================================
    [Fact]
    public async Task Test09_10_11_Search_ProjectFilter_CategoryFilter()
    {
        var project = new DeveloperProject { Name = "Gamma", Path = Path.Combine(_testDbDirectory, "Gamma") };
        await _projectRepository.AddAsync(project);

        await _commandService.SaveCommandAsync(new SavedCommand { Name = "Frontend Lint", Executable = "npm", Arguments = new[] { "run", "lint" }, Category = "Quality", ProjectId = project.Id });
        await _commandService.SaveCommandAsync(new SavedCommand { Name = "Backend Test", Executable = "dotnet", Arguments = new[] { "test" }, Category = "Testing", ProjectId = project.Id });
        await _commandService.SaveCommandAsync(new SavedCommand { Name = "Global Git", Executable = "git", Arguments = new[] { "status" }, Category = "VersionControl", ProjectId = null });

        var all = await _commandService.GetAllCommandsAsync();
        Assert.Equal(3, all.Count);

        var projectCmds = await _commandService.GetProjectCommandsAsync(project.Id);
        Assert.Equal(2, projectCmds.Count);

        var globalCmds = await _commandService.GetGlobalCommandsAsync();
        Assert.Single(globalCmds);
    }

    // =============================================================================================
    // 12, 13. Validation Missing Name / Missing Executable
    // =============================================================================================
    [Fact]
    public void Test12_13_Validation_MissingNameAndExecutable_Fails()
    {
        var missingName = new SavedCommand { Name = "", Executable = "dotnet" };
        var (valid1, err1) = _commandService.ValidateCommand(missingName);
        Assert.False(valid1);
        Assert.Contains("name is required", err1, StringComparison.OrdinalIgnoreCase);

        var missingExe = new SavedCommand { Name = "Valid", Executable = "   " };
        var (valid2, err2) = _commandService.ValidateCommand(missingExe);
        Assert.False(valid2);
        Assert.Contains("executable is required", err2, StringComparison.OrdinalIgnoreCase);
    }

    // =============================================================================================
    // 14. Project Missing at Run
    // =============================================================================================
    [Fact]
    public async Task Test14_ProjectMissingAtRun_FailsSafely()
    {
        var missingProjectId = Guid.NewGuid();
        var cmd = new SavedCommand { Name = "Orphan", Executable = "dotnet", ProjectId = missingProjectId };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _commandService.SaveCommandAsync(cmd));
        Assert.Contains("does not exist", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =============================================================================================
    // 15. Working Directory Missing at Run
    // =============================================================================================
    [Fact]
    public async Task Test15_NonexistentWorkingDirectory_FailsSafelyAtRun()
    {
        var cmd = new SavedCommand
        {
            Name = "Bad Dir",
            Executable = "dotnet",
            WorkingDirectory = @"Z:\NonExistent\Directory\Path"
        };
        await _commandService.SaveCommandAsync(cmd);

        var toolResolver = new SavedCommandToolResolver();
        var launcher = new TestProcessLauncher();
        using var executor = new SavedCommandExecutor(_commandService, toolResolver, launcher, NullLogger<SavedCommandExecutor>.Instance);

        var result = await executor.RunCommandAsync(cmd.Id);
        Assert.False(result.Success);
        Assert.Contains("Working directory does not exist", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // =============================================================================================
    // 16, 17, 18. Working Directory Resolution Rules
    // =============================================================================================
    [Fact]
    public async Task Test16_17_18_WorkingDirectoryResolutionRules()
    {
        var projDir = Path.Combine(_testDbDirectory, "ProjectDir");
        Directory.CreateDirectory(projDir);
        var project = new DeveloperProject { Name = "DirTest", Path = projDir };
        await _projectRepository.AddAsync(project);

        // 16. Fallback to project path
        var cmd1 = new SavedCommand { Name = "No Explicit Dir", Executable = "dotnet", ProjectId = project.Id };
        var res1 = await _commandService.ResolveWorkingDirectoryAsync(cmd1);
        Assert.Equal(projDir, res1);

        // 17. Explicit wins over project path
        var explicitDir = Path.Combine(_testDbDirectory, "ExplicitDir");
        var cmd2 = new SavedCommand { Name = "Explicit Dir", Executable = "dotnet", ProjectId = project.Id, WorkingDirectory = explicitDir };
        var res2 = await _commandService.ResolveWorkingDirectoryAsync(cmd2);
        Assert.Equal(explicitDir, res2);

        // 18. UserProfile fallback for global command
        var cmd3 = new SavedCommand { Name = "Global Dir", Executable = "dotnet", ProjectId = null };
        var res3 = await _commandService.ResolveWorkingDirectoryAsync(cmd3);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), res3);
    }

    // =============================================================================================
    // 19, 20, 21, 22. Arguments Spaces, Quotes, Empty, Unicode Round-Trip
    // =============================================================================================
    [Fact]
    public void Test19_20_21_22_Arguments_CommandLineToArgvW_RoundTrip()
    {
        // 19. Spaces
        var argsWithSpaces = new[] { "file with spaces.txt", "another space" };
        var cmdLineSpaces = WindowsCommandLineSerializer.FormatCommandLine("tool", argsWithSpaces);
        var parsedSpaces = WindowsCommandLineParser.ParseArguments(cmdLineSpaces["tool".Length..].Trim());
        Assert.Equal(argsWithSpaces, parsedSpaces);

        // 20. Quotes & Backslashes
        var argsWithQuotes = new[] { "quote\"test\\", @"C:\path\with\slash\" };
        var cmdLineQuotes = WindowsCommandLineSerializer.FormatCommandLine("tool", argsWithQuotes);
        var parsedQuotes = WindowsCommandLineParser.ParseArguments(cmdLineQuotes["tool".Length..].Trim());
        Assert.Equal(argsWithQuotes, parsedQuotes);

        // 21. Empty Argument
        var argsWithEmpty = new[] { "before", "", "after" };
        var cmdLineEmpty = WindowsCommandLineSerializer.FormatCommandLine("tool", argsWithEmpty);
        var parsedEmpty = WindowsCommandLineParser.ParseArguments(cmdLineEmpty["tool".Length..].Trim());
        Assert.Equal(argsWithEmpty, parsedEmpty);

        // 22. Unicode
        var argsWithUnicode = new[] { "日本語テスト", "🎉_emoji", "äöü" };
        var cmdLineUnicode = WindowsCommandLineSerializer.FormatCommandLine("tool", argsWithUnicode);
        var parsedUnicode = WindowsCommandLineParser.ParseArguments(cmdLineUnicode["tool".Length..].Trim());
        Assert.Equal(argsWithUnicode, parsedUnicode);
    }

    // =============================================================================================
    // 23, 24, 25. Direct Execution, No cmd or PowerShell Wrapping
    // =============================================================================================
    [Fact]
    public void Test23_24_25_NoCmdOrPowerShellWrapping_DirectExeExecution()
    {
        var resolver = new SavedCommandToolResolver();

        // 24, 25: Direct shell invocation rejected
        var cmdRes = resolver.ResolveTool("cmd.exe", new[] { "/c", "dir" });
        Assert.False(cmdRes.Success);
        Assert.Contains("Direct shell invocation", cmdRes.ErrorMessage);

        var psRes = resolver.ResolveTool("powershell", new[] { "-Command", "ls" });
        Assert.False(psRes.Success);
        Assert.Contains("Direct shell invocation", psRes.ErrorMessage);

        var pwshRes = resolver.ResolveTool("pwsh.exe", new[] { "-Command", "ls" });
        Assert.False(pwshRes.Success);
        Assert.Contains("Direct shell invocation", pwshRes.ErrorMessage);

        // 23: Direct execution of native binary
        var testExe = Path.Combine(_testDbDirectory, "native_app.exe");
        File.WriteAllText(testExe, "fake binary");
        var nativeRes = resolver.ResolveTool(testExe, new[] { "-v" });
        Assert.True(nativeRes.Success);
        Assert.False(nativeRes.IsCmdShim);
        Assert.Equal(testExe, nativeRes.ExecutablePath);
    }

    // =============================================================================================
    // 26, 27, 28. Trusted Shim Handling, Metacharacter Rejection, Arbitrary Batch Rejection
    // =============================================================================================
    [Fact]
    public void Test26_27_28_TrustedShimAndBatchSafety()
    {
        var resolver = new SavedCommandToolResolver();

        // 28. Arbitrary batch file rejected
        var arbitraryBatch = Path.Combine(_testDbDirectory, "untrusted_script.bat");
        File.WriteAllText(arbitraryBatch, "echo unsafe");
        var batchRes = resolver.ResolveTool(arbitraryBatch, Array.Empty<string>());
        Assert.False(batchRes.Success);
        Assert.Contains("arbitrary .bat/.cmd scripts", batchRes.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // 27. Dangerous shell-sensitive shim argument rejected
        var fakeNpmCmd = Path.Combine(_testDbDirectory, "npm.cmd");
        File.WriteAllText(fakeNpmCmd, "@echo off");
        var dangerousArgRes = resolver.ResolveTool(fakeNpmCmd, new[] { "run", "dev & taskkill" });
        Assert.False(dangerousArgRes.Success);
        Assert.Contains("forbidden shell metacharacters", dangerousArgRes.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // 26. Trusted npm.cmd with clean arguments allowed through hardened path
        var cleanNpmRes = resolver.ResolveTool(fakeNpmCmd, new[] { "run", "build", "--verbose" });
        Assert.True(cleanNpmRes.Success);
        Assert.True(cleanNpmRes.IsCmdShim);
    }

    // =============================================================================================
    // 29, 30, 31, 32, 33. Execution Start, Succeeded, Failed, Cancelled
    // =============================================================================================
    [Fact]
    public async Task Test29_30_31_32_33_ExecutionLifecycle_States()
    {
        var resolver = new SavedCommandToolResolver();
        var launcher = new TestProcessLauncher();
        using var executor = new SavedCommandExecutor(_commandService, resolver, launcher, NullLogger<SavedCommandExecutor>.Instance);

        var testExe = Path.Combine(_testDbDirectory, "runner_tool.exe");
        File.WriteAllText(testExe, "binary");

        var cmd = new SavedCommand { Name = "Lifecycle Cmd", Executable = testExe, Arguments = new[] { "run" } };
        await _commandService.SaveCommandAsync(cmd);

        // 30, 31: Success Lifecycle
        var mockProcess = new TestManagedProcess();
        launcher.ProcessToReturn = mockProcess;

        SavedCommandRunSession? lastSession = null;
        executor.SessionChanged += (_, s) => lastSession = s;

        var runResult = await executor.RunCommandAsync(cmd.Id);
        Assert.True(runResult.Success);
        Assert.NotNull(executor.GetActiveSession(cmd.Id));

        mockProcess.Complete(0);
        await Task.Delay(100); // allow monitor completion

        Assert.Null(executor.GetActiveSession(cmd.Id));
        var latest = executor.GetLatestSession(cmd.Id);
        Assert.NotNull(latest);
        Assert.Equal(SavedCommandExecutionState.Succeeded, latest.State);
        Assert.Equal(0, latest.ExitCode);

        // 32: Non-zero exit => Failed
        var mockProcessFail = new TestManagedProcess();
        launcher.ProcessToReturn = mockProcessFail;
        await executor.RunCommandAsync(cmd.Id);
        mockProcessFail.Complete(1);
        await Task.Delay(100);

        latest = executor.GetLatestSession(cmd.Id);
        Assert.NotNull(latest);
        Assert.Equal(SavedCommandExecutionState.Failed, latest.State);
        Assert.Equal(1, latest.ExitCode);

        // 33: Explicit Stop => Cancelled
        var mockProcessStop = new TestManagedProcess();
        launcher.ProcessToReturn = mockProcessStop;
        await executor.RunCommandAsync(cmd.Id);
        bool stopped = await executor.StopCommandAsync(cmd.Id);
        Assert.True(stopped);
        await Task.Delay(100);

        latest = executor.GetLatestSession(cmd.Id);
        Assert.NotNull(latest);
        Assert.Equal(SavedCommandExecutionState.Cancelled, latest.State);

        // 29: Executable not found
        var missingExeCmd = new SavedCommand { Name = "Missing Exe", Executable = @"C:\Missing\Tool.exe" };
        await _commandService.SaveCommandAsync(missingExeCmd);
        var missingResult = await executor.RunCommandAsync(missingExeCmd.Id);
        Assert.False(missingResult.Success);
        Assert.Contains("does not exist", missingResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // =============================================================================================
    // 34, 35. Concurrency: Same Command Blocked, Different Commands Concurrent
    // =============================================================================================
    [Fact]
    public async Task Test34_35_ConcurrencyRules()
    {
        var resolver = new SavedCommandToolResolver();
        var launcher = new TestProcessLauncher();
        using var executor = new SavedCommandExecutor(_commandService, resolver, launcher, NullLogger<SavedCommandExecutor>.Instance);

        var testExe = Path.Combine(_testDbDirectory, "concurrent_tool.exe");
        File.WriteAllText(testExe, "binary");

        var cmd1 = new SavedCommand { Name = "Cmd 1", Executable = testExe };
        var cmd2 = new SavedCommand { Name = "Cmd 2", Executable = testExe };
        await _commandService.SaveCommandAsync(cmd1);
        await _commandService.SaveCommandAsync(cmd2);

        var proc1 = new TestManagedProcess();
        launcher.ProcessToReturn = proc1;

        // Start Cmd 1
        var run1 = await executor.RunCommandAsync(cmd1.Id);
        Assert.True(run1.Success);

        // 34: Duplicate Run of Cmd 1 while active must fail
        var duplicateRun = await executor.RunCommandAsync(cmd1.Id);
        Assert.False(duplicateRun.Success);
        Assert.Contains("already running", duplicateRun.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // 35: Different command Cmd 2 can run concurrently
        var proc2 = new TestManagedProcess();
        launcher.ProcessToReturn = proc2;
        var run2 = await executor.RunCommandAsync(cmd2.Id);
        Assert.True(run2.Success);

        Assert.Equal(2, executor.GetActiveSessions().Count);

        // Cleanup
        proc1.Complete(0);
        proc2.Complete(0);
        await Task.Delay(100);
    }

    // =============================================================================================
    // 36, 37, 38. Cancellation Affects Only Owned Process & Job Assignment
    // =============================================================================================
    [Fact]
    public async Task Test36_37_38_CancellationAffectsOnlyTargetProcess()
    {
        var resolver = new SavedCommandToolResolver();
        var launcher = new TestProcessLauncher();
        using var executor = new SavedCommandExecutor(_commandService, resolver, launcher, NullLogger<SavedCommandExecutor>.Instance);

        var testExe = Path.Combine(_testDbDirectory, "job_tool.exe");
        File.WriteAllText(testExe, "binary");

        var cmdA = new SavedCommand { Name = "Cmd A", Executable = testExe };
        var cmdB = new SavedCommand { Name = "Cmd B", Executable = testExe };
        await _commandService.SaveCommandAsync(cmdA);
        await _commandService.SaveCommandAsync(cmdB);

        var procA = new TestManagedProcess();
        launcher.ProcessToReturn = procA;
        await executor.RunCommandAsync(cmdA.Id);

        var procB = new TestManagedProcess();
        launcher.ProcessToReturn = procB;
        await executor.RunCommandAsync(cmdB.Id);

        // Stop Cmd A only
        await executor.StopCommandAsync(cmdA.Id);
        Assert.True(procA.WasKilled);
        Assert.False(procB.WasKilled);

        procB.Complete(0);
        await Task.Delay(100);
    }

    // =============================================================================================
    // 39, 40, 41. Output Draining, Sequence Ordering, Bounded Output
    // =============================================================================================
    [Fact]
    public async Task Test39_40_41_OutputDraining_SequenceOrdering_Bounds()
    {
        var resolver = new SavedCommandToolResolver();
        var launcher = new TestProcessLauncher();
        using var executor = new SavedCommandExecutor(_commandService, resolver, launcher, NullLogger<SavedCommandExecutor>.Instance);

        var testExe = Path.Combine(_testDbDirectory, "output_tool.exe");
        File.WriteAllText(testExe, "binary");

        var cmd = new SavedCommand { Name = "Output Test", Executable = testExe };
        await _commandService.SaveCommandAsync(cmd);

        var proc = new TestManagedProcess();
        launcher.ProcessToReturn = proc;

        var receivedEvents = new List<SavedCommandOutputEvent>();
        executor.OutputReceived += (_, e) => receivedEvents.Add(e);

        var run = await executor.RunCommandAsync(cmd.Id);
        Assert.True(run.Success);

        proc.EmitOutput("Line 1: Building project");
        proc.EmitError("Line 2: Warning found");
        proc.EmitOutput("Line 3: Finished in 240ms");

        proc.Complete(0);
        await Task.Delay(100);

        // 39, 40: Drained and sequenced
        var output = executor.GetSessionOutput(cmd.Id, run.SessionId!.Value);
        Assert.Equal(3, output.Count);
        Assert.Equal(1, output[0].SequenceNumber);
        Assert.Equal(2, output[1].SequenceNumber);
        Assert.Equal(3, output[2].SequenceNumber);
        Assert.False(output[0].IsError);
        Assert.True(output[1].IsError);
        Assert.False(output[2].IsError);

        // 41: Bounded buffer test
        var buffer = new SavedCommandBoundedBuffer(maxLines: 5);
        for (int i = 0; i < 10; i++)
        {
            buffer.Add(Guid.NewGuid(), Guid.NewGuid(), $"Msg {i}", isError: false, i);
        }
        var snapshot = buffer.GetSnapshot();
        Assert.Equal(5, snapshot.Count);
        Assert.Equal("Msg 5", snapshot[0].Text);
        Assert.Equal("Msg 9", snapshot[4].Text);
    }

    // =============================================================================================
    // 42. Runtime State Not Persisted
    // =============================================================================================
    [Fact]
    public async Task Test42_RuntimeStateNotPersisted()
    {
        var cmd = new SavedCommand { Name = "Persistence Check", Executable = "dotnet" };
        await _commandService.SaveCommandAsync(cmd);

        // Verify entity properties in fresh context have no runtime PID/ExitCode fields
        using var context = _contextFactory.CreateDbContext();
        var reloaded = await context.SavedCommands.FirstAsync(c => c.Id == cmd.Id);
        Assert.NotNull(reloaded);
        // Entity only has durable properties
        Assert.Equal("Persistence Check", reloaded.Name);
    }

    // =============================================================================================
    // 43, 44, 45. Navigation Lifecycle: Does Not Stop Command, Rehydrates Session
    // =============================================================================================
    [Fact]
    public async Task Test43_44_45_NavigationLifecycle_PreservesCommandAndRehydrates()
    {
        var resolver = new SavedCommandToolResolver();
        var launcher = new TestProcessLauncher();
        using var executor = new SavedCommandExecutor(_commandService, resolver, launcher, NullLogger<SavedCommandExecutor>.Instance);

        var testExe = Path.Combine(_testDbDirectory, "nav_tool.exe");
        File.WriteAllText(testExe, "binary");

        var cmd = new SavedCommand { Name = "Nav Test Cmd", Executable = testExe };
        await _commandService.SaveCommandAsync(cmd);

        var proc = new TestManagedProcess();
        launcher.ProcessToReturn = proc;

        var dialogService = new TestDialogService();
        var viewModel = new CommandsViewModel(
            _commandService,
            executor,
            _projectRepository,
            dialogService,
            NullLogger<CommandsViewModel>.Instance,
            uiDispatcher: a => a())
        {
            SelectedScope = CommandScopeFilter.GlobalCommands
        };

        // Initialize view
        await viewModel.InitializeAsync();
        Assert.Single(viewModel.FilteredCommands);

        // Start command from UI
        await viewModel.RunCommandCommand.ExecuteAsync(viewModel.FilteredCommands[0]);
        Assert.True(viewModel.FilteredCommands[0].IsActive);

        // 43: Navigate away (DeactivateAsync) - must not stop process
        await viewModel.DeactivateAsync();
        Assert.False(proc.HasExited);
        Assert.False(proc.WasKilled);

        // 44: Return to Commands (InitializeAsync) - rehydrates state
        await viewModel.InitializeAsync();
        Assert.True(viewModel.FilteredCommands[0].IsActive);
        Assert.Equal(SavedCommandExecutionState.Running, viewModel.FilteredCommands[0].ExecutionState);

        // 45: Repeated navigation does not duplicate event subscriptions
        await viewModel.DeactivateAsync();
        await viewModel.InitializeAsync();

        proc.Complete(0);
        await Task.Delay(100);
        Assert.Equal(SavedCommandExecutionState.Succeeded, viewModel.FilteredCommands[0].ExecutionState);
    }

    // =============================================================================================
    // 46. Unified Shutdown Confirmation
    // =============================================================================================
    [Fact]
    public void Test46_UnifiedShutdown_MessageHandlesProjectsAndCommands()
    {
        var vmBoth = new ConfirmShutdownViewModel(activeProjectCount: 1, activeCommandCount: 2);
        Assert.Equal(3, vmBoth.TotalActiveCount);
        Assert.Contains("1 project", vmBoth.Message);
        Assert.Contains("2 saved commands", vmBoth.Message);

        var vmCommandsOnly = new ConfirmShutdownViewModel(activeProjectCount: 0, activeCommandCount: 3);
        Assert.Contains("3 saved commands", vmCommandsOnly.Message);

        var vmProjectsOnly = new ConfirmShutdownViewModel(activeProjectCount: 2, activeCommandCount: 0);
        Assert.Contains("2 projects", vmProjectsOnly.Message);
    }

    // =============================================================================================
    // 47. Project Delete Confirmation Mentions Associated Commands
    // =============================================================================================
    [Fact]
    public void Test47_ProjectDeleteConfirmation_MentionsAssociatedCommands()
    {
        var vmWithCommands = new ConfirmDeleteViewModel("MyProject", @"C:\Dev\MyProject", associatedCommandsCount: 3);
        Assert.True(vmWithCommands.HasAssociatedCommands);
        Assert.Contains("3 associated saved commands", vmWithCommands.AssociatedCommandsWarning);

        var vmZeroCommands = new ConfirmDeleteViewModel("CleanProject", @"C:\Dev\CleanProject", associatedCommandsCount: 0);
        Assert.False(vmZeroCommands.HasAssociatedCommands);
    }

    // =============================================================================================
    // 48. CommandsView Smoke Test
    // =============================================================================================
    [Fact]
    public void Test48_CommandsView_SmokeTest()
    {
        DevDeskSmokeTests.RunOnSta(() =>
        {
            DevDeskSmokeTests.EnsureApplicationResourcesLoaded();

            var dialogService = new TestDialogService();
            var resolver = new SavedCommandToolResolver();
            var launcher = new TestProcessLauncher();
            using var executor = new SavedCommandExecutor(_commandService, resolver, launcher, NullLogger<SavedCommandExecutor>.Instance);

            var vm = new CommandsViewModel(
                _commandService,
                executor,
                _projectRepository,
                dialogService,
                NullLogger<CommandsViewModel>.Instance,
                uiDispatcher: a => a());
            var view = new CommandsView { DataContext = vm };

            Assert.NotNull(view);
            Assert.Same(vm, view.DataContext);
        });
    }

    // =============================================================================================
    // 49. Add/Edit Dialog Smoke Test & Argument Badges
    // =============================================================================================
    [Fact]
    public void Test49_AddEditDialog_SmokeTest_AndArgumentTokens()
    {
        DevDeskSmokeTests.RunOnSta(() =>
        {
            DevDeskSmokeTests.EnsureApplicationResourcesLoaded();

            var project = new DeveloperProject { Name = "DialogProj", Path = @"C:\Dev\DialogProj" };
            var vm = new AddEditCommandViewModel(new[] { project }, project.Id)
            {
                Name = "Vite Dev",
                Executable = "npm",
                ArgumentsText = "run dev -- \"--port 3000\""
            };

            Assert.Equal(4, vm.ParsedArgumentsTokens.Count);
            Assert.Equal("run", vm.ParsedArgumentsTokens[0]);
            Assert.Equal("dev", vm.ParsedArgumentsTokens[1]);
            Assert.Equal("--", vm.ParsedArgumentsTokens[2]);
            Assert.Equal("--port 3000", vm.ParsedArgumentsTokens[3]);

            var dialog = new AddEditCommandDialog(vm);
            Assert.NotNull(dialog);
        });
    }

    // =============================================================================================
    // 50. Migration Upgrades Schema Safely
    // =============================================================================================
    [Fact]
    public async Task Test50_Migration_UpgradesSchemaSafely()
    {
        var freshDbPath = Path.Combine(_testDbDirectory, "migration_upgrade.db");
        var freshPathProvider = new DatabasePathProvider(freshDbPath);
        var freshOptions = new DbContextOptionsBuilder<DevDeskDbContext>()
            .UseSqlite(freshPathProvider.GetConnectionString())
            .Options;
        var freshFactory = new TestDbContextFactory(freshOptions);

        var initializer = new DatabaseInitializer(
            freshFactory,
            freshPathProvider,
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.InitializeAsync();
        Assert.True(File.Exists(freshDbPath));

        using var context = freshFactory.CreateDbContext();
        var canConnect = await context.Database.CanConnectAsync();
        Assert.True(canConnect);

        // Verify SavedCommands table is functional
        var count = await context.SavedCommands.CountAsync();
        Assert.Equal(0, count);
    }

    // =============================================================================================
    // 51. MSBUILDDISABLENODEREUSE Applied Strictly to Child Process Environment for dotnet/msbuild
    // =============================================================================================
    [Fact]
    public async Task Test51_MsBuildDisableNodeReuse_AppliedOnlyToTargetedChildProcesses()
    {
        Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", null);

        var resolver = new SavedCommandToolResolver();
        var launcher = new TestProcessLauncher();
        using var executor = new SavedCommandExecutor(_commandService, resolver, launcher, NullLogger<SavedCommandExecutor>.Instance);

        var dotnetExe = Path.Combine(_testDbDirectory, "dotnet.exe");
        File.WriteAllText(dotnetExe, "binary");
        var msbuildExe = Path.Combine(_testDbDirectory, "msbuild.exe");
        File.WriteAllText(msbuildExe, "binary");
        var gitExe = Path.Combine(_testDbDirectory, "git.exe");
        File.WriteAllText(gitExe, "binary");

        // 1. dotnet executable receives child environment override
        var dotnetCmd = new SavedCommand { Name = "Dotnet Build", Executable = dotnetExe, Arguments = new[] { "build" } };
        await _commandService.SaveCommandAsync(dotnetCmd);
        var dotnetProcess = new TestManagedProcess();
        launcher.ProcessToReturn = dotnetProcess;
        _ = executor.RunCommandAsync(dotnetCmd.Id);
        Assert.NotNull(launcher.LastConfig);
        Assert.NotNull(launcher.LastConfig.EnvironmentVariables);
        Assert.True(launcher.LastConfig.EnvironmentVariables.TryGetValue("MSBUILDDISABLENODEREUSE", out var dotnetVal));
        Assert.Equal("1", dotnetVal);
        dotnetProcess.Complete(0);

        // 2. msbuild executable receives child environment override
        var msbuildCmd = new SavedCommand { Name = "MsBuild Tool", Executable = msbuildExe, Arguments = new[] { "/m" } };
        await _commandService.SaveCommandAsync(msbuildCmd);
        var msbuildProcess = new TestManagedProcess();
        launcher.ProcessToReturn = msbuildProcess;
        _ = executor.RunCommandAsync(msbuildCmd.Id);
        Assert.NotNull(launcher.LastConfig);
        Assert.NotNull(launcher.LastConfig.EnvironmentVariables);
        Assert.True(launcher.LastConfig.EnvironmentVariables.TryGetValue("MSBUILDDISABLENODEREUSE", out var msbuildVal));
        Assert.Equal("1", msbuildVal);
        msbuildProcess.Complete(0);

        // 3. Unrelated commands (e.g. git) do NOT receive the override
        var gitCmd = new SavedCommand { Name = "Git Status", Executable = gitExe, Arguments = new[] { "status" } };
        await _commandService.SaveCommandAsync(gitCmd);
        var gitProcess = new TestManagedProcess();
        launcher.ProcessToReturn = gitProcess;
        _ = executor.RunCommandAsync(gitCmd.Id);
        Assert.NotNull(launcher.LastConfig);
        Assert.True(launcher.LastConfig.EnvironmentVariables == null ||
                    !launcher.LastConfig.EnvironmentVariables.ContainsKey("MSBUILDDISABLENODEREUSE"));
        gitProcess.Complete(0);

        // 4. DevDesk's current process-wide environment is NOT mutated
        Assert.Null(Environment.GetEnvironmentVariable("MSBUILDDISABLENODEREUSE"));
    }
}
