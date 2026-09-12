using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.App.Services.Dialogs;
using DevDesk.App.ViewModels.Commands;
using DevDesk.App.ViewModels.Projects;
using DevDesk.App.ViewModels.Shell;
using DevDesk.Core.Commands;
using DevDesk.Core.Models;
using DevDesk.Core.Repositories;
using DevDesk.Infrastructure.Commands;
using DevDesk.Infrastructure.Persistence;
using DevDesk.Infrastructure.Persistence.Database;
using DevDesk.Infrastructure.Persistence.Repositories;
using DevDesk.Infrastructure.Runner;
using DevDesk.Infrastructure.Services;
using Xunit;

namespace DevDesk.Tests;

[Collection("StaSmoke")]
public sealed class SavedCommandsRuntimeQaTests : IDisposable
{
    private readonly string _testDbDirectory;
    private readonly string _testDbPath;
    private readonly IDbContextFactory<DevDeskDbContext> _contextFactory;
    private readonly DatabasePathProvider _pathProvider;
    private readonly IProjectRepository _projectRepository;
    private readonly SavedCommandRepository _commandRepository;
    private readonly SavedCommandService _commandService;
    private readonly SavedCommandToolResolver _toolResolver;
    private readonly SystemProcessLauncher _launcher;
    private readonly SavedCommandExecutor _executor;
    private readonly DeveloperProject _devDeskProject;

    public SavedCommandsRuntimeQaTests()
    {
        _testDbDirectory = Path.Combine(Path.GetTempPath(), "DevDesk_RuntimeQa", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDirectory);
        _testDbPath = Path.Combine(_testDbDirectory, "runtime_qa.db");

        _pathProvider = new DatabasePathProvider(_testDbPath);
        var options = new DbContextOptionsBuilder<DevDeskDbContext>()
            .UseSqlite(_pathProvider.GetConnectionString())
            .Options;
        _contextFactory = new QaDbContextFactory(options);

        // Apply migrations
        using var context = _contextFactory.CreateDbContext();
        context.Database.Migrate();

        _projectRepository = new ProjectRepository(_contextFactory);
        _commandRepository = new SavedCommandRepository(_contextFactory);
        _commandService = new SavedCommandService(_commandRepository, _projectRepository);
        _toolResolver = new SavedCommandToolResolver();
        _launcher = new SystemProcessLauncher();
        _executor = new SavedCommandExecutor(_commandService, _toolResolver, _launcher, NullLogger<SavedCommandExecutor>.Instance);

        // Seed "Dev Desk" project
        _devDeskProject = new DeveloperProject
        {
            Name = "Dev Desk",
            Path = @"D:\Games\Dev Desk"
        };
        _projectRepository.AddAsync(_devDeskProject).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _executor.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_testDbDirectory))
            {
                Directory.Delete(_testDbDirectory, true);
            }
        }
        catch { }
    }

    private sealed class QaDbContextFactory : IDbContextFactory<DevDeskDbContext>
    {
        private readonly DbContextOptions<DevDeskDbContext> _options;
        public QaDbContextFactory(DbContextOptions<DevDeskDbContext> options) => _options = options;
        public DevDeskDbContext CreateDbContext() => new(_options);
    }

    // =============================================================================================
    // 1. GLOBAL COMMAND: dotnet --info
    // =============================================================================================
    [Fact]
    public async Task Qa01_GlobalCommand_DotnetInfo()
    {
        var cmd = new SavedCommand
        {
            Name = "Dotnet Info",
            ProjectId = null,
            Executable = "dotnet",
            Arguments = new[] { "--info" }
        };
        await _commandService.SaveCommandAsync(cmd);

        SavedCommandRunSession? observedStarting = null;
        SavedCommandRunSession? observedRunning = null;

        _executor.SessionChanged += (_, s) =>
        {
            if (s.CommandId == cmd.Id)
            {
                if (s.State == SavedCommandExecutionState.Starting) observedStarting = s;
                if (s.State == SavedCommandExecutionState.Running) observedRunning = s;
            }
        };

        var result = await _executor.RunCommandAsync(cmd.Id);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.SessionId);

        // Await completion (dotnet --info takes < 1 second)
        var completed = await WaitForSessionCompletionAsync(cmd.Id, TimeSpan.FromSeconds(10));
        Assert.NotNull(completed);
        Assert.Equal(SavedCommandExecutionState.Succeeded, completed.State);
        Assert.Equal(0, completed.ExitCode);

        // Verify output captured inside DevDesk
        var output = _executor.GetSessionOutput(cmd.Id, result.SessionId.Value);
        Assert.NotEmpty(output);
        var fullText = string.Join(Environment.NewLine, output.Select(o => o.Text));
        Assert.Contains(".NET", fullText, StringComparison.OrdinalIgnoreCase);
    }

    // =============================================================================================
    // 2. PROJECT COMMAND: Build DevDesk (no explicit working dir => resolved D:\Games\Dev Desk)
    // =============================================================================================
    [Fact]
    public async Task Qa02_ProjectCommand_BuildDevDesk()
    {
        var cmd = new SavedCommand
        {
            Name = "Build DevDesk",
            ProjectId = _devDeskProject.Id,
            Executable = "dotnet",
            Arguments = new[] { "build", "DevDesk.sln", "-c", "Debug", "--no-restore", "/nodeReuse:false" },
            WorkingDirectory = null // Test fallback to project directory
        };
        await _commandService.SaveCommandAsync(cmd);

        var result = await _executor.RunCommandAsync(cmd.Id);
        Assert.True(result.Success, result.ErrorMessage);

        var active = _executor.GetActiveSession(cmd.Id);
        Assert.NotNull(active);
        Assert.Equal(@"D:\Games\Dev Desk", active.WorkingDirectory);

        // Verify duplicate Run while active does not create second execution
        var duplicateResult = await _executor.RunCommandAsync(cmd.Id);
        Assert.False(duplicateResult.Success);
        Assert.Contains("already running", duplicateResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Wait for build to complete
        var completed = await WaitForSessionCompletionAsync(cmd.Id, TimeSpan.FromSeconds(60));
        Assert.NotNull(completed);
        Assert.Equal(SavedCommandExecutionState.Succeeded, completed.State);
        Assert.Equal(0, completed.ExitCode);

        var output = _executor.GetSessionOutput(cmd.Id, result.SessionId!.Value);
        Assert.NotEmpty(output);
        var text = string.Join(Environment.NewLine, output.Select(o => o.Text));
        Assert.Contains("Build succeeded", text, StringComparison.OrdinalIgnoreCase);
    }

    // =============================================================================================
    // 3. GIT COMMAND: git status
    // =============================================================================================
    [Fact]
    public async Task Qa03_GitCommand_GitStatus()
    {
        var cmd = new SavedCommand
        {
            Name = "Git Status",
            ProjectId = _devDeskProject.Id,
            Executable = "git",
            Arguments = new[] { "status" }
        };
        await _commandService.SaveCommandAsync(cmd);

        var result = await _executor.RunCommandAsync(cmd.Id);
        Assert.True(result.Success, result.ErrorMessage);

        var completed = await WaitForSessionCompletionAsync(cmd.Id, TimeSpan.FromSeconds(10));
        Assert.NotNull(completed);
        Assert.Equal(SavedCommandExecutionState.Succeeded, completed.State);
        Assert.Equal(0, completed.ExitCode);

        var output = _executor.GetSessionOutput(cmd.Id, result.SessionId!.Value);
        Assert.NotEmpty(output);
        var text = string.Join(Environment.NewLine, output.Select(o => o.Text));
        Assert.Contains("branch", text, StringComparison.OrdinalIgnoreCase);
    }

    // =============================================================================================
    // 4. TRUSTED WINDOWS SHIM: npm --version
    // =============================================================================================
    [Fact]
    public async Task Qa04_TrustedShim_NpmVersion()
    {
        var toolRes = _toolResolver.ResolveTool("npm", new[] { "--version" });
        if (!toolRes.Success)
        {
            // Node/npm is not installed on this machine, skip safely as allowed by spec
            return;
        }

        Assert.True(toolRes.IsCmdShim, "npm on Windows should resolve to .cmd shim");

        var cmd = new SavedCommand
        {
            Name = "NPM Version",
            ProjectId = null,
            Executable = "npm",
            Arguments = new[] { "--version" }
        };
        await _commandService.SaveCommandAsync(cmd);

        var result = await _executor.RunCommandAsync(cmd.Id);
        Assert.True(result.Success, result.ErrorMessage);

        var completed = await WaitForSessionCompletionAsync(cmd.Id, TimeSpan.FromSeconds(15));
        Assert.NotNull(completed);
        Assert.Equal(SavedCommandExecutionState.Succeeded, completed.State);
        Assert.Equal(0, completed.ExitCode);

        var output = _executor.GetSessionOutput(cmd.Id, result.SessionId!.Value);
        Assert.NotEmpty(output);
        var text = string.Join(Environment.NewLine, output.Select(o => o.Text)).Trim();
        Assert.Matches(@"^\d+\.\d+\.\d+", text);
    }

    // =============================================================================================
    // 5. NAVIGATION LIFECYCLE: Navigate away & return while command runs
    // =============================================================================================
    [Fact]
    public async Task Qa05_NavigationLifecycle_PreservesRunningCommand()
    {
        var cmd = new SavedCommand
        {
            Name = "Dotnet Version",
            ProjectId = null,
            Executable = "dotnet",
            Arguments = new[] { "--version" }
        };
        await _commandService.SaveCommandAsync(cmd);

        var dialogService = new TestDialogService();
        var vm = new CommandsViewModel(_commandService, _executor, _projectRepository, dialogService, NullLogger<CommandsViewModel>.Instance, a => a())
        {
            SelectedScope = CommandScopeFilter.GlobalCommands
        };

        await vm.InitializeAsync();
        Assert.Single(vm.FilteredCommands);

        // Run from ViewModel
        await vm.RunCommandCommand.ExecuteAsync(vm.FilteredCommands[0]);
        Assert.True(vm.FilteredCommands[0].IsActive);

        // Navigate away: DeactivateAsync
        await vm.DeactivateAsync();

        // Simulate time spent on other views
        await Task.Delay(50);

        // Navigate back: InitializeAsync
        await vm.InitializeAsync();

        // Session state was preserved or completed cleanly
        var reloadedItem = vm.FilteredCommands.First(c => c.Id == cmd.Id);
        Assert.True(reloadedItem.ExecutionState == SavedCommandExecutionState.Running || reloadedItem.ExecutionState == SavedCommandExecutionState.Succeeded);

        await WaitForSessionCompletionAsync(cmd.Id, TimeSpan.FromSeconds(10));
        await vm.InitializeAsync();
        Assert.Equal(SavedCommandExecutionState.Succeeded, vm.FilteredCommands[0].ExecutionState);
    }

    // =============================================================================================
    // 6. STOP / OWNERSHIP: Job Tree Termination
    // =============================================================================================
    [Fact]
    public async Task Qa06_Stop_CancelsCommandAndKillsJobTree()
    {
        // ping 127.0.0.1 -n 10 runs for ~9 seconds natively without shell
        var cmd = new SavedCommand
        {
            Name = "Long Pinger",
            ProjectId = null,
            Executable = "ping.exe",
            Arguments = new[] { "127.0.0.1", "-n", "15" }
        };
        await _commandService.SaveCommandAsync(cmd);

        var result = await _executor.RunCommandAsync(cmd.Id);
        Assert.True(result.Success, result.ErrorMessage);

        var active = _executor.GetActiveSession(cmd.Id);
        Assert.NotNull(active);
        int pid = active.ProcessId!.Value;

        // Ensure process started
        await Task.Delay(300);

        // Stop command
        bool stopped = await _executor.StopCommandAsync(cmd.Id);
        Assert.True(stopped);

        var completed = await WaitForSessionCompletionAsync(cmd.Id, TimeSpan.FromSeconds(5));
        Assert.NotNull(completed);
        Assert.Equal(SavedCommandExecutionState.Cancelled, completed.State);

        // Process must be dead
        await Task.Delay(200);
        bool isAlive = false;
        try
        {
            var proc = System.Diagnostics.Process.GetProcessById(pid);
            isAlive = !proc.HasExited;
        }
        catch (ArgumentException)
        {
            isAlive = false; // already gone
        }

        Assert.False(isAlive, "Terminated command process tree should no longer be running.");
    }

    // =============================================================================================
    // 7. PERSISTENCE: Reopen resets runtime state to Idle
    // =============================================================================================
    [Fact]
    public async Task Qa07_Persistence_DefinitionsPersist_RuntimeStateIsIdle()
    {
        var cmd = new SavedCommand
        {
            Name = "Persist Test",
            ProjectId = null,
            Executable = "dotnet",
            Arguments = new[] { "--version" },
            Category = "Diagnostics"
        };
        await _commandService.SaveCommandAsync(cmd);

        // Simulate app restart: create a new service and executor instance from DB
        var freshCommandRepo = new SavedCommandRepository(_contextFactory);
        var freshCommandService = new SavedCommandService(freshCommandRepo, _projectRepository);
        var freshExecutor = new SavedCommandExecutor(freshCommandService, _toolResolver, _launcher, NullLogger<SavedCommandExecutor>.Instance);

        var allCommands = await freshCommandService.GetAllCommandsAsync();
        var reloaded = allCommands.FirstOrDefault(c => c.Id == cmd.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Persist Test", reloaded.Name);
        Assert.Equal("Diagnostics", reloaded.Category);

        // Fresh executor has zero active sessions and reports no residual PID
        Assert.Null(freshExecutor.GetActiveSession(cmd.Id));
        Assert.Null(freshExecutor.GetLatestSession(cmd.Id));
        Assert.Empty(freshExecutor.GetActiveSessions());
    }

    // =============================================================================================
    // 8. UNIFIED SHUTDOWN: Truthful reporting of Projects + Commands
    // =============================================================================================
    [Fact]
    public void Qa08_UnifiedShutdown_TruthfulMessage()
    {
        var vmBoth = new ConfirmShutdownViewModel(activeProjectCount: 1, activeCommandCount: 2);
        Assert.Equal(3, vmBoth.TotalActiveCount);
        Assert.Contains("1 project", vmBoth.Message);
        Assert.Contains("2 saved commands", vmBoth.Message);
        Assert.Contains("They must be stopped before DevDesk exits.", vmBoth.Message);

        var vmCommandsOnly = new ConfirmShutdownViewModel(activeProjectCount: 0, activeCommandCount: 1);
        Assert.Equal(1, vmCommandsOnly.TotalActiveCount);
        Assert.Contains("1 saved command", vmCommandsOnly.Message);

        var vmProjectsOnly = new ConfirmShutdownViewModel(activeProjectCount: 2, activeCommandCount: 0);
        Assert.Equal(2, vmProjectsOnly.TotalActiveCount);
        Assert.Contains("2 projects", vmProjectsOnly.Message);
    }

    // =============================================================================================
    // 9. CRUD & Project Delete Warning
    // =============================================================================================
    [Fact]
    public async Task Qa09_CrudAndAssociatedCommandsWarning()
    {
        // Add
        var cmd = new SavedCommand
        {
            Name = "Initial Name",
            ProjectId = _devDeskProject.Id,
            Executable = "git",
            Arguments = new[] { "log", "-n", "1" },
            Category = "VCS"
        };
        await _commandService.SaveCommandAsync(cmd);

        // Edit
        cmd.Name = "Updated Name";
        cmd.Category = "Git Tools";
        await _commandService.SaveCommandAsync(cmd);

        var updated = await _commandService.GetCommandByIdAsync(cmd.Id);
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("Git Tools", updated.Category);

        // Project Delete Warning check
        var projectCommands = await _commandService.GetProjectCommandsAsync(_devDeskProject.Id);
        int associatedCount = projectCommands.Count;
        Assert.True(associatedCount >= 1);

        var deleteVm = new ConfirmDeleteViewModel(_devDeskProject.Name, _devDeskProject.Path, associatedCount);
        Assert.True(deleteVm.HasAssociatedCommands);
        Assert.Contains($"{associatedCount} associated saved command", deleteVm.AssociatedCommandsWarning);

        // Delete Command
        bool deleted = await _commandService.DeleteCommandAsync(cmd.Id);
        Assert.True(deleted);

        var postDelete = await _commandService.GetCommandByIdAsync(cmd.Id);
        Assert.Null(postDelete);
    }

    private async Task<SavedCommandRunSession?> WaitForSessionCompletionAsync(Guid commandId, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            var active = _executor.GetActiveSession(commandId);
            if (active is null)
            {
                var latest = _executor.GetLatestSession(commandId);
                if (latest is not null && latest.State != SavedCommandExecutionState.Starting && latest.State != SavedCommandExecutionState.Running)
                {
                    return latest;
                }
            }
            await Task.Delay(100);
        }
        return null;
    }

    private sealed class TestDialogService : IDialogService
    {
        public bool ShowAddEditCommandDialog(AddEditCommandViewModel viewModel) => true;
        public bool ShowConfirmDeleteCommandDialog(ConfirmDeleteCommandViewModel viewModel) => true;
        public bool ShowAddEditProjectDialog(AddEditProjectViewModel viewModel) => true;
        public bool ShowConfirmDeleteDialog(ConfirmDeleteViewModel viewModel) => true;
        public bool ShowConfirmShutdownDialog(int activeProjectCount, int activeCommandCount = 0) => true;
        public string? ShowFolderPicker(string? initialDirectory = null, string? title = null) => null;
        public void ShowMessage(string title, string message) { }
        public string? ShowFilePicker(string? filter = null, string? title = null) => null;
        public bool ShowConfirmationDialog(string title, string message, string confirmButtonText = "Confirm") => true;
    }
}
