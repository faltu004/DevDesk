using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using DevDesk.App.Services.Dialogs;
using DevDesk.App.Services.Navigation;
using DevDesk.App.ViewModels.Dashboard;
using DevDesk.App.ViewModels.Projects;
using DevDesk.App.Views.Dashboard;
using DevDesk.Core.Detection;
using DevDesk.Core.Git;
using DevDesk.Core.Launchers;
using DevDesk.Core.Models;
using DevDesk.Core.Runner;
using DevDesk.Core.Services;
using DevDesk.Core.SystemMonitor;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevDesk.Tests;

[Collection("StaSmoke")]
public sealed class DashboardActionsTests
{
    private readonly FakeNavigationService _nav = new();
    private readonly FakeProjectService _projects = new();
    private readonly FakeLauncherService _launcher = new();
    private readonly FakeProjectRunnerService _runner = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeSystemMonitorService _monitor = new();
    private readonly FakeGitService _git = new();

    public DashboardActionsTests()
    {
        _projects.ExistingProjects.Add(new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "Portfolio",
            Path = @"C:\Dev\portfolio",
            Framework = "Vite",
            Language = "TypeScript",
            DefaultPort = 5173
        });
        _projects.ExistingProjects.Add(new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "DevAI Toolkit",
            Path = @"C:\Dev\devai-toolkit",
            Framework = "Next.js",
            Language = "TypeScript",
            DefaultPort = 3000
        });
        _projects.ExistingProjects.Add(new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "API Server",
            Path = @"C:\Dev\api-server",
            Framework = ".NET 8",
            Language = "C#",
            DefaultPort = 5000
        });
    }

    private DashboardViewModel CreateViewModel(ProjectsViewModel? projectsVm = null)
    {
        return new DashboardViewModel(
            _monitor,
            NullLogger<DashboardViewModel>.Instance,
            _nav,
            _projects,
            _launcher,
            _runner,
            _dialogs,
            projectsVm,
            uiDispatcher: a => a());
    }

    [Fact]
    public void Test01_ViewAllProjects_NavigatesToProjects()
    {
        var vm = CreateViewModel();

        vm.ViewAllProjectsCommand.Execute(null);

        Assert.Equal(NavigationItem.Projects, _nav.LastNavigatedItem);
    }

    [Fact]
    public void Test02_CheckPorts_NavigatesToPorts()
    {
        var vm = CreateViewModel();

        vm.CheckPortsCommand.Execute(null);

        Assert.Equal(NavigationItem.Ports, _nav.LastNavigatedItem);
    }

    [Fact]
    public async Task Test03_AddProject_InvokesPickerAndDialogAndSaves()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "devdesk_add_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        try
        {
            _dialogs.FolderPickerResult = tempFolder;
            _dialogs.AddProjectDialogResult = true;

            var vm = CreateViewModel();

            await vm.AddProjectCommand.ExecuteAsync(null);

            Assert.NotNull(_dialogs.LastAddEditViewModel);
            Assert.Equal(tempFolder, _dialogs.LastAddEditViewModel.ProjectPath);
            Assert.Single(_projects.AddedProjects);
            Assert.Equal(tempFolder, _projects.AddedProjects[0].Path);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [Fact]
    public async Task Test04_Run_ReceivesCorrectProject()
    {
        var vm = CreateViewModel();
        var targetRow = vm.ActiveProjects[0]; // Portfolio

        await vm.RunProjectCommand.ExecuteAsync(targetRow);

        Assert.Contains(targetRow.Id, _runner.StartedProjectIds);
        Assert.True(targetRow.IsRunning);
        Assert.Equal("Running", targetRow.StatusText);
    }

    [Fact]
    public async Task Test05_Stop_ReceivesCorrectProject()
    {
        var vm = CreateViewModel();
        var targetRow = vm.ActiveProjects[1]; // DevAI Toolkit
        targetRow.IsRunning = true;
        targetRow.StatusText = "Running";

        await vm.StopProjectCommand.ExecuteAsync(targetRow);

        Assert.Contains(targetRow.Id, _runner.StoppedProjectIds);
        Assert.False(targetRow.IsRunning);
        Assert.Equal("Stopped", targetRow.StatusText);
    }

    [Fact]
    public void Test06_ViewLogs_ReceivesCorrectProjectAndSelectsLogsTab()
    {
        var proj1 = new DeveloperProject
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Portfolio",
            Path = @"C:\Dev\portfolio"
        };
        var proj2 = new DeveloperProject
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "DevAI Toolkit",
            Path = @"C:\Dev\devai-toolkit"
        };

        _projects.ExistingProjects.Clear();
        _projects.ExistingProjects.Add(proj1);
        _projects.ExistingProjects.Add(proj2);

        var logsVm = new ProjectLogsViewModel(_runner, NullLogger<ProjectLogsViewModel>.Instance);
        var gitVm = new ProjectGitViewModel(_git, _launcher, NullLogger<ProjectGitViewModel>.Instance);

        var projectsVm = new ProjectsViewModel(
            _projects,
            _launcher,
            _runner,
            _dialogs,
            logsVm,
            gitVm,
            NullLogger<ProjectsViewModel>.Instance);

        projectsVm.Projects.Add(new ProjectPresentationModel(proj1));
        projectsVm.Projects.Add(new ProjectPresentationModel(proj2));

        var vm = CreateViewModel(projectsVm);

        var targetRow = vm.ActiveProjects[1]; // DevAI Toolkit
        vm.ViewLogsCommand.Execute(targetRow);

        Assert.NotNull(projectsVm.SelectedProject);
        Assert.Equal(proj2.Id, projectsVm.SelectedProject.Id);
        Assert.Equal("Logs", projectsVm.SelectedDetailsTab);
        Assert.Equal(NavigationItem.Projects, _nav.LastNavigatedItem);
    }

    [Fact]
    public async Task Test07_Open_ReceivesCorrectProject()
    {
        var vm = CreateViewModel();
        var targetRow = vm.ActiveProjects[0];

        await vm.OpenProjectCommand.ExecuteAsync(targetRow);

        Assert.Equal(targetRow.Path, _launcher.LastExplorerPath);
    }

    [Fact]
    public async Task Test08_OneRowCannotControlAnotherProject()
    {
        var vm = CreateViewModel();
        var row0 = vm.ActiveProjects[0];
        var row1 = vm.ActiveProjects[1];

        // Ensure row0 is stopped and row1 is running initially
        row0.IsRunning = false;
        row1.IsRunning = true;

        // Run row0
        await vm.RunProjectCommand.ExecuteAsync(row0);

        Assert.True(row0.IsRunning);
        // row1 should remain untouched
        Assert.True(row1.IsRunning);
        Assert.Contains(row0.Id, _runner.StartedProjectIds);
        Assert.DoesNotContain(row1.Id, _runner.StartedProjectIds);

        // Stop row1
        await vm.StopProjectCommand.ExecuteAsync(row1);

        Assert.False(row1.IsRunning);
        // row0 should remain running
        Assert.True(row0.IsRunning);
        Assert.Contains(row1.Id, _runner.StoppedProjectIds);
        Assert.DoesNotContain(row0.Id, _runner.StoppedProjectIds);
    }

    [Fact]
    public async Task Test09_Overflow_ReceivesCorrectRowContext()
    {
        var vm = CreateViewModel();
        var targetRow = vm.ActiveProjects[2]; // API Server

        // VS Code launcher
        await vm.OpenVsCodeCommand.ExecuteAsync(targetRow);
        Assert.Equal(targetRow.Path, _launcher.LastVsCodePath);

        // Terminal launcher
        await vm.OpenTerminalCommand.ExecuteAsync(targetRow);
        Assert.Equal(targetRow.Path, _launcher.LastTerminalPath);

        // Explorer launcher
        await vm.OpenProjectCommand.ExecuteAsync(targetRow);
        Assert.Equal(targetRow.Path, _launcher.LastExplorerPath);
    }

    [Fact]
    public async Task Test10_ClearHistory_ClearsRecentAndUpdatesService()
    {
        var proj1 = new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "Recent 1",
            Path = @"C:\Dev\recent1",
            LastOpenedAt = DateTimeOffset.UtcNow
        };
        _projects.ExistingProjects.Add(proj1);

        var vm = CreateViewModel();
        Assert.NotEmpty(vm.RecentProjects);
        Assert.True(vm.HasRecentProjects);

        await vm.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Empty(vm.RecentProjects);
        Assert.False(vm.HasRecentProjects);
        Assert.Null(proj1.LastOpenedAt);
        Assert.Contains(proj1.Id, _projects.UpdatedProjectIds);
    }

    [Fact]
    public async Task Test11_DashboardNavigation_DeactivatesSystemMonitor()
    {
        var vm = CreateViewModel();

        // Initialize starts monitor polling
        await vm.InitializeAsync();
        Assert.True(vm.IsPollingActive);

        // Deactivate stops monitor polling
        await vm.DeactivateAsync();
        Assert.False(vm.IsPollingActive);
    }

    [Fact]
    public void Test12_XamlSmokeTest_InstantiatesAndBindsView()
    {
        DevDeskSmokeTests.RunOnSta(() =>
        {
            DevDeskSmokeTests.EnsureApplicationResourcesLoaded();

            var vm = CreateViewModel();
            var view = new DashboardView
            {
                DataContext = vm
            };

            Assert.NotNull(view);
            Assert.Equal(vm, view.DataContext);
        });
    }

    [Fact]
    public async Task Test13_OpenVsCode_UnambiguousProjectContext_OpensVsCode()
    {
        // Setup a single project
        _projects.ExistingProjects.Clear();
        var singleProj = new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "Single Project",
            Path = @"C:\Dev\single-project",
            Framework = ".NET 10.0"
        };
        _projects.ExistingProjects.Add(singleProj);

        var vm = CreateViewModel();
        Assert.True(vm.CanOpenVsCode);

        await vm.OpenVsCodeCommand.ExecuteAsync(null);

        Assert.Equal(singleProj.Path, _launcher.LastVsCodePath);
    }

    [Fact]
    public void Test14_OpenVsCode_NoProjects_DisabledWithTooltip()
    {
        _projects.ExistingProjects.Clear();

        var vm = CreateViewModel();

        Assert.False(vm.CanOpenVsCode);
        Assert.Contains("No project context available", vm.VsCodeTooltip);
    }

    [Fact]
    public async Task Test15_OpenVsCode_AmbiguousProjects_NavigatesToProjects()
    {
        // 3 projects exist with none selected and no recent history
        var vm = CreateViewModel();
        Assert.True(vm.CanOpenVsCode);

        await vm.OpenVsCodeCommand.ExecuteAsync(null);

        // Ambiguous context navigates user to Projects view to select
        Assert.Equal(NavigationItem.Projects, _nav.LastNavigatedItem);
    }

    [Fact]
    public void Test16_ActiveProjects_AreDerivedFromPersistence_WithRealIds()
    {
        var vm = CreateViewModel();

        Assert.Equal(3, vm.ActiveProjects.Count);
        Assert.Equal(_projects.ExistingProjects[0].Id, vm.ActiveProjects[0].Id);
        Assert.Equal(_projects.ExistingProjects[1].Id, vm.ActiveProjects[1].Id);
        Assert.Equal(_projects.ExistingProjects[2].Id, vm.ActiveProjects[2].Id);
        Assert.Equal(_projects.ExistingProjects[0].Name, vm.ActiveProjects[0].Name);
        Assert.Equal(_projects.ExistingProjects[0].Path, vm.ActiveProjects[0].Path);
    }

    #region Fake Service Implementations

    private sealed class FakeNavigationService : INavigationService
    {
        public object? CurrentViewModel { get; set; }
        public NavigationItem CurrentItem { get; set; }
        public NavigationItem? LastNavigatedItem { get; private set; }
        public event Action? CurrentViewModelChanged;

        public void NavigateTo<TViewModel>() where TViewModel : class
        {
        }

        public void NavigateTo(NavigationItem item)
        {
            LastNavigatedItem = item;
            CurrentItem = item;
            CurrentViewModelChanged?.Invoke();
        }
    }

    private sealed class FakeProjectService : IProjectService
    {
        public List<DeveloperProject> AddedProjects { get; } = new();
        public List<DeveloperProject> ExistingProjects { get; } = new();
        public List<Guid> UpdatedProjectIds { get; } = new();

        public Task<IReadOnlyList<DeveloperProject>> GetProjectsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<DeveloperProject>>(ExistingProjects);
        }

        public Task<DeveloperProject?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingProjects.FirstOrDefault(p => p.Id == id));
        }

        public Task<DeveloperProject> AddProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default)
        {
            AddedProjects.Add(project);
            ExistingProjects.Add(project);
            return Task.FromResult(project);
        }

        public Task<DeveloperProject> UpdateProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default)
        {
            UpdatedProjectIds.Add(project.Id);
            return Task.FromResult(project);
        }

        public Task RemoveProjectAsync(Guid id, CancellationToken cancellationToken = default)
        {
            ExistingProjects.RemoveAll(p => p.Id == id);
            return Task.CompletedTask;
        }

        public Task<ProjectDetectionResult> DetectAndApplyAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProjectDetectionResult
            {
                Framework = "DetectedFramework"
            });
        }
    }

    private sealed class FakeLauncherService : ILauncherService
    {
        public string? LastVsCodePath { get; private set; }
        public string? LastExplorerPath { get; private set; }
        public string? LastTerminalPath { get; private set; }

        public Task<LaunchResult> OpenInVsCodeAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            LastVsCodePath = projectPath;
            return Task.FromResult(LaunchResult.Ok());
        }

        public Task<LaunchResult> OpenInExplorerAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            LastExplorerPath = projectPath;
            return Task.FromResult(LaunchResult.Ok());
        }

        public Task<LaunchResult> OpenTerminalAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            LastTerminalPath = projectPath;
            return Task.FromResult(LaunchResult.Ok());
        }
    }

    private sealed class FakeProjectRunnerService : IProjectRunnerService
    {
        public List<Guid> StartedProjectIds { get; } = new();
        public List<Guid> StoppedProjectIds { get; } = new();

        public event EventHandler<ProjectRunSession>? SessionChanged;
        public event EventHandler<ProcessOutputEvent>? OutputReceived { add { } remove { } }

        public Task<ProjectRunResult> StartProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            StartedProjectIds.Add(projectId);
            var session = new ProjectRunSession
            {
                SessionId = Guid.NewGuid(),
                ProjectId = projectId,
                ProjectName = "Test",
                CommandText = "dotnet run",
                ExecutablePath = "dotnet.exe",
                Arguments = Array.Empty<string>(),
                State = ProjectRunState.Running
            };
            SessionChanged?.Invoke(this, session);
            return Task.FromResult(ProjectRunResult.Succeeded(session));
        }

        public Task<ProjectStopResult> StopProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        {
            StoppedProjectIds.Add(projectId);
            var session = new ProjectRunSession
            {
                SessionId = Guid.NewGuid(),
                ProjectId = projectId,
                ProjectName = "Test",
                CommandText = "dotnet run",
                ExecutablePath = "dotnet.exe",
                Arguments = Array.Empty<string>(),
                State = ProjectRunState.Exited
            };
            SessionChanged?.Invoke(this, session);
            return Task.FromResult(ProjectStopResult.Succeeded(projectId));
        }

        public Task<ProjectRunResult> RestartProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
            => StartProjectAsync(projectId, cancellationToken);

        public Task<IReadOnlyList<ProjectStopResult>> StopAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectStopResult>>(Array.Empty<ProjectStopResult>());

        public ProjectRunSession? GetSession(Guid projectId) => null;
        public IReadOnlyList<ProjectRunSession> GetActiveSessions() => Array.Empty<ProjectRunSession>();
        public IReadOnlyList<ManagedProcessIdentity> GetManagedProcesses() => Array.Empty<ManagedProcessIdentity>();
        public IReadOnlyList<ProjectRunSession> GetRecentSessions(Guid projectId) => Array.Empty<ProjectRunSession>();
        public IReadOnlyList<ProcessOutputEvent> GetSessionLogs(Guid projectId, Guid sessionId) => Array.Empty<ProcessOutputEvent>();
    }

    private sealed class FakeDialogService : IDialogService
    {
        public string? FolderPickerResult { get; set; }
        public bool AddProjectDialogResult { get; set; }
        public AddEditProjectViewModel? LastAddEditViewModel { get; private set; }

        public string? ShowFolderPicker(string? initialDirectory = null, string? title = null) => FolderPickerResult;

        public bool ShowAddEditProjectDialog(AddEditProjectViewModel viewModel)
        {
            LastAddEditViewModel = viewModel;
            viewModel.ProjectName = "New Project";
            return AddProjectDialogResult;
        }

        public bool ShowConfirmDeleteDialog(ConfirmDeleteViewModel viewModel) => true;
        public bool ShowAddEditCommandDialog(DevDesk.App.ViewModels.Commands.AddEditCommandViewModel viewModel) => true;
        public bool ShowConfirmDeleteCommandDialog(DevDesk.App.ViewModels.Commands.ConfirmDeleteCommandViewModel viewModel) => true;
        public bool ShowConfirmShutdownDialog(int activeProjectCount, int activeCommandCount = 0) => true;
        public string? FilePickerResult { get; set; }
        public string? ShowFilePicker(string? filter = null, string? title = null) => FilePickerResult;
        public bool ConfirmationResult { get; set; } = true;
        public bool ShowConfirmationDialog(string title, string message, string confirmButtonText = "Confirm") => ConfirmationResult;
        public void ShowMessage(string title, string message) { }
    }

    private sealed class FakeSystemMonitorService : ISystemMonitorService
    {
        public bool IsPollingActive { get; private set; }
        public SystemSnapshot? CurrentSnapshot { get; private set; }
        public event EventHandler<SystemSnapshot>? SnapshotUpdated;

        public void ResetBaselines()
        {
            IsPollingActive = true;
        }

        public Task<SystemSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                IsPollingActive = false;
            }
            var snap = new SystemSnapshot();
            CurrentSnapshot = snap;
            SnapshotUpdated?.Invoke(this, snap);
            return Task.FromResult(snap);
        }
    }

    private sealed class FakeGitService : IGitService
    {
        public Task<GitAvailability> CheckGitAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GitAvailability(true, "2.40.0", @"C:\Program Files\Git\bin\git.exe"));
        }

        public Task<GitRepositoryStatus> GetRepositoryStatusAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GitRepositoryStatus.NotGit());
        }
    }

    #endregion
}
