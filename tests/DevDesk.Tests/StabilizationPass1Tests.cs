using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.App.Services.Dialogs;
using DevDesk.App.Services.Navigation;
using DevDesk.App.ViewModels.Common;
using DevDesk.App.ViewModels.Dashboard;
using DevDesk.App.ViewModels.Ports;
using DevDesk.App.ViewModels.Processes;
using DevDesk.App.ViewModels.Projects;
using DevDesk.App.ViewModels.Settings;
using DevDesk.App.Views.Commands;
using DevDesk.App.Views.Dashboard;
using DevDesk.App.Views.Ports;
using DevDesk.App.Views.Processes;
using DevDesk.App.Views.Projects;
using DevDesk.App.Views.Settings;
using DevDesk.Core.Detection;
using DevDesk.Core.Git;
using DevDesk.Core.Launchers;
using DevDesk.Core.Models;
using DevDesk.Core.Ports;
using DevDesk.Core.Processes;
using DevDesk.Core.Runner;
using DevDesk.Core.Services;
using DevDesk.Core.Settings;
using DevDesk.Core.SystemMonitor;
using DevDesk.Infrastructure.Persistence.Database;

namespace DevDesk.Tests;

[Collection("StaSmoke")]
public sealed class StabilizationPass1Tests
{
    [Fact]
    public void Test01_Settings_GeneralCategory_SelectsAndRequestsScroll()
    {
        var vm = CreateSettingsViewModel();
        string? requestedScroll = null;
        vm.RequestScrollToCategory += (sender, cat) => requestedScroll = cat;

        var generalCat = vm.Categories.First(c => c.Id == "General");
        vm.SelectCategoryCommand.Execute(generalCat);

        Assert.Equal("General", vm.SelectedCategoryId);
        Assert.True(generalCat.IsSelected);
        Assert.Equal("General", requestedScroll);
    }

    [Fact]
    public void Test02_Settings_EditorsAndTerminalsCategory_SelectsAndRequestsScroll()
    {
        var vm = CreateSettingsViewModel();
        string? requestedScroll = null;
        vm.RequestScrollToCategory += (sender, cat) => requestedScroll = cat;

        var editorsCat = vm.Categories.First(c => c.Id == "Editors");
        vm.SelectCategoryCommand.Execute(editorsCat);

        Assert.Equal("Editors", vm.SelectedCategoryId);
        Assert.True(editorsCat.IsSelected);
        Assert.Equal("Editors", requestedScroll);
    }

    [Fact]
    public void Test03_Settings_MonitoringCategory_SelectsAndRequestsScroll()
    {
        var vm = CreateSettingsViewModel();
        string? requestedScroll = null;
        vm.RequestScrollToCategory += (sender, cat) => requestedScroll = cat;

        var monCat = vm.Categories.First(c => c.Id == "Monitoring");
        vm.SelectCategoryCommand.Execute(monCat);

        Assert.Equal("Monitoring", vm.SelectedCategoryId);
        Assert.True(monCat.IsSelected);
        Assert.Equal("Monitoring", requestedScroll);
    }

    [Fact]
    public void Test04_Settings_DataAndAboutCategory_SelectsAndRequestsScroll()
    {
        var vm = CreateSettingsViewModel();
        string? requestedScroll = null;
        vm.RequestScrollToCategory += (sender, cat) => requestedScroll = cat;

        var dataCat = vm.Categories.First(c => c.Id == "Data");
        vm.SelectCategoryCommand.Execute(dataCat);

        Assert.Equal("Data", vm.SelectedCategoryId);
        Assert.True(dataCat.IsSelected);
        Assert.Equal("Data", requestedScroll);
    }

    [Fact]
    public void Test05_Settings_UpdateSelectedCategoryFromScroll_UpdatesWithoutLoopbackEvent()
    {
        var vm = CreateSettingsViewModel();
        string? requestedScroll = null;
        vm.RequestScrollToCategory += (sender, cat) => requestedScroll = cat;

        vm.UpdateSelectedCategoryFromScroll("Monitoring");

        Assert.Equal("Monitoring", vm.SelectedCategoryId);
        Assert.True(vm.Categories.First(c => c.Id == "Monitoring").IsSelected);
        Assert.False(vm.Categories.First(c => c.Id == "General").IsSelected);
        // Must NOT fire RequestScrollToCategory when updating from scroll event
        Assert.Null(requestedScroll);
    }

    [Fact]
    public void Test06_ProjectDetails_UnsupportedTabs_RemovedFromUI_OnlySupportedRemain()
    {
        var (vm, _, _) = CreateProjectsViewModel();

        // Valid supported tabs in ViewModel
        Assert.Equal("Overview", vm.SelectedDetailsTab);
        Assert.True(vm.IsOverviewTabSelected);

        vm.SelectDetailsTab("Logs");
        Assert.True(vm.IsLogsTabSelected);

        vm.SelectDetailsTab("Git");
        Assert.True(vm.IsGitTabSelected);

        // Verify XAML markup does not contain dead buttons for unsupported tabs
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DevDesk.App", "Views", "Projects", "ProjectsView.xaml");
        if (File.Exists(xamlPath))
        {
            var content = File.ReadAllText(xamlPath);
            Assert.DoesNotContain("CommandParameter=\"Scripts\"", content);
            Assert.DoesNotContain("CommandParameter=\"Environment\"", content);
            Assert.DoesNotContain("CommandParameter=\"Notes\"", content);
        }
    }

    [Fact]
    public void Test07_ProjectDetails_SupportedTabs_ActivateAndDeactivateCorrectly()
    {
        var (vm, logsVm, gitVm) = CreateProjectsViewModel();

        Assert.True(vm.IsOverviewTabSelected);
        Assert.False(vm.IsLogsTabSelected);
        Assert.False(vm.IsGitTabSelected);

        // Switch to Logs
        vm.SelectDetailsTab("Logs");
        Assert.True(vm.IsLogsTabSelected);
        Assert.False(vm.IsOverviewTabSelected);
        Assert.False(vm.IsGitTabSelected);

        // Switch to Git
        vm.SelectDetailsTab("Git");
        Assert.True(vm.IsGitTabSelected);
        Assert.False(vm.IsLogsTabSelected);
        Assert.False(vm.IsOverviewTabSelected);

        // Switch back to Overview
        vm.SelectDetailsTab("Overview");
        Assert.True(vm.IsOverviewTabSelected);
        Assert.False(vm.IsLogsTabSelected);
        Assert.False(vm.IsGitTabSelected);
    }

    [Fact]
    public void Test08_Projects_SearchText_FiltersCorrectly()
    {
        var (vm, _, _) = CreateProjectsViewModel();

        var p1 = new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "Web Frontend",
            Path = @"C:\Repos\web-frontend",
            Framework = "Next.js",
            Language = "TypeScript",
            PackageManager = "pnpm"
        };
        var p2 = new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "Core API",
            Path = @"C:\Repos\core-api",
            Framework = "ASP.NET Core",
            Language = "C#",
            PackageManager = "dotnet"
        };
        var p3 = new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "Mobile Client",
            Path = @"C:\Repos\mobile",
            Framework = "Flutter",
            Language = "Dart",
            PackageManager = "pub"
        };

        vm.Projects.Add(new ProjectPresentationModel(p1));
        vm.Projects.Add(new ProjectPresentationModel(p2));
        vm.Projects.Add(new ProjectPresentationModel(p3));

        // Initial search is empty -> all projects present
        vm.ApplyFilter();
        Assert.Equal(3, vm.FilteredProjects.Count);

        // Filter by Name
        vm.SearchText = "Core";
        Assert.Single(vm.FilteredProjects);
        Assert.Equal("Core API", vm.FilteredProjects[0].Name);

        // Filter by Framework
        vm.SearchText = "Flutter";
        Assert.Single(vm.FilteredProjects);
        Assert.Equal("Mobile Client", vm.FilteredProjects[0].Name);

        // Filter by Language
        vm.SearchText = "TypeScript";
        Assert.Single(vm.FilteredProjects);
        Assert.Equal("Web Frontend", vm.FilteredProjects[0].Name);

        // Filter by Package Manager
        vm.SearchText = "dotnet";
        Assert.Single(vm.FilteredProjects);
        Assert.Equal("Core API", vm.FilteredProjects[0].Name);

        // Filter with no matches
        vm.SearchText = "NonExistentTermXYZ";
        Assert.Empty(vm.FilteredProjects);
    }

    [Fact]
    public void Test09_Projects_ClearSearch_RestoresAllResults()
    {
        var (vm, _, _) = CreateProjectsViewModel();

        vm.Projects.Add(new ProjectPresentationModel(new DeveloperProject { Id = Guid.NewGuid(), Name = "Project A", Path = @"C:\A" }));
        vm.Projects.Add(new ProjectPresentationModel(new DeveloperProject { Id = Guid.NewGuid(), Name = "Project B", Path = @"C:\B" }));

        vm.SearchText = "Project A";
        Assert.Single(vm.FilteredProjects);

        // Execute ClearSearch
        vm.ClearSearchCommand.Execute(null);

        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Equal(2, vm.FilteredProjects.Count);
    }

    [Fact]
    public void Test10_Dashboard_SearchShortcut_And_SubmitNavigatesToProjects()
    {
        var nav = new FakeNavigationService();
        var (projectsVm, _, _) = CreateProjectsViewModel();
        var dashboardVm = CreateDashboardViewModel(nav, projectsVm);

        Assert.Equal("Ctrl + K", dashboardVm.SearchShortcut);
        Assert.Equal("Ctrl + K", projectsVm.SearchShortcut);

        // Setting SearchText in Dashboard
        dashboardVm.SearchText = "SearchQuery123";

        // Submit Search Command
        dashboardVm.SubmitSearchCommand.Execute(null);

        // Navigates to Projects and transfers search query
        Assert.Equal(NavigationItem.Projects, nav.LastNavigatedItem);
        Assert.Equal("SearchQuery123", projectsVm.SearchText);
    }

    [Fact]
    public void Test11_Dashboard_EnvironmentSummary_HasFourDynamicItems()
    {
        var nav = new FakeNavigationService();
        var (projectsVm, _, _) = CreateProjectsViewModel();
        var dashboardVm = CreateDashboardViewModel(nav, projectsVm);

        Assert.Equal(4, dashboardVm.EnvironmentSummary.Count);
        Assert.Equal("Active Projects", dashboardVm.EnvironmentSummary[0].Title);
        Assert.Equal("Active Dev Ports", dashboardVm.EnvironmentSummary[1].Title);
        Assert.Equal("Git Changes", dashboardVm.EnvironmentSummary[2].Title);
        Assert.Equal("Port Conflicts", dashboardVm.EnvironmentSummary[3].Title);

        foreach (var item in dashboardVm.EnvironmentSummary)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Value));
            Assert.False(string.IsNullOrWhiteSpace(item.Subtext));
        }
    }

    [Fact]
    public void Test12_Views_ContainNamedSearchBoxes_AndSections()
    {
        var xamlDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DevDesk.App", "Views");
        if (Directory.Exists(xamlDir))
        {
            var dashXaml = File.ReadAllText(Path.Combine(xamlDir, "Dashboard", "DashboardView.xaml"));
            Assert.Contains("x:Name=\"DashboardSearchTextBox\"", dashXaml);

            var projXaml = File.ReadAllText(Path.Combine(xamlDir, "Projects", "ProjectsView.xaml"));
            Assert.Contains("x:Name=\"ProjectSearchTextBox\"", projXaml);

            var portXaml = File.ReadAllText(Path.Combine(xamlDir, "Ports", "PortsView.xaml"));
            Assert.Contains("x:Name=\"PortsSearchTextBox\"", portXaml);

            var cmdXaml = File.ReadAllText(Path.Combine(xamlDir, "Commands", "CommandsView.xaml"));
            Assert.Contains("x:Name=\"CommandsSearchTextBox\"", cmdXaml);

            var procXaml = File.ReadAllText(Path.Combine(xamlDir, "Processes", "ProcessesView.xaml"));
            Assert.Contains("x:Name=\"ProcessesSearchTextBox\"", procXaml);

            var setXaml = File.ReadAllText(Path.Combine(xamlDir, "Settings", "SettingsView.xaml"));
            Assert.Contains("x:Name=\"SettingsScrollViewer\"", setXaml);
            Assert.Contains("x:Name=\"SectionGeneral\"", setXaml);
            Assert.Contains("x:Name=\"SectionEditors\"", setXaml);
            Assert.Contains("x:Name=\"SectionMonitoring\"", setXaml);
            Assert.Contains("x:Name=\"SectionData\"", setXaml);
        }
    }

    [Fact]
    public void Test13_BrandingAssets_AreConfiguredAndPresent()
    {
        var appDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DevDesk.App");
        if (Directory.Exists(appDir))
        {
            var logoPath = Path.Combine(appDir, "Resources", "Branding", "DevDesk-Logo-Transparent.png");
            var iconPath = Path.Combine(appDir, "Resources", "Branding", "DevDesk.ico");
            Assert.True(File.Exists(logoPath), "DevDesk-Logo-Transparent.png must exist");
            Assert.True(File.Exists(iconPath), "DevDesk.ico must exist");

            var mainXaml = File.ReadAllText(Path.Combine(appDir, "Views", "Shell", "MainWindow.xaml"));
            Assert.Contains("DevDesk-Logo-Transparent.png", mainXaml);
            Assert.Contains("DevDesk.ico", mainXaml);
            Assert.DoesNotContain("M11,2 L2,7 L2,15 L11,20 Z", mainXaml);

            var csproj = File.ReadAllText(Path.Combine(appDir, "DevDesk.App.csproj"));
            Assert.Contains("<ApplicationIcon>Resources\\Branding\\DevDesk.ico</ApplicationIcon>", csproj);
        }
    }

    #region Helper Factories and Fakes

    private static SettingsViewModel CreateSettingsViewModel()
    {
        return new SettingsViewModel(
            new FakeSettingsService(),
            new FakeDatabasePathProvider(),
            new FakeLauncherService(),
            new FakeDialogService(),
            NullLogger<SettingsViewModel>.Instance,
            null,
            action => action());
    }

    private static (ProjectsViewModel Vm, ProjectLogsViewModel Logs, ProjectGitViewModel Git) CreateProjectsViewModel()
    {
        var runner = new FakeProjectRunnerService();
        var git = new FakeGitService();
        var launcher = new FakeLauncherService();
        var logs = new ProjectLogsViewModel(runner, NullLogger<ProjectLogsViewModel>.Instance);
        var gitVm = new ProjectGitViewModel(git, launcher, NullLogger<ProjectGitViewModel>.Instance);
        var vm = new ProjectsViewModel(
            new FakeProjectService(),
            launcher,
            runner,
            new FakeDialogService(),
            logs,
            gitVm,
            NullLogger<ProjectsViewModel>.Instance);

        return (vm, logs, gitVm);
    }

    private static DashboardViewModel CreateDashboardViewModel(INavigationService nav, ProjectsViewModel projectsVm)
    {
        return new DashboardViewModel(
            new FakeSystemMonitorService(),
            NullLogger<DashboardViewModel>.Instance,
            nav,
            new FakeProjectService(),
            new FakeLauncherService(),
            new FakeProjectRunnerService(),
            new FakeDialogService(),
            projectsVm,
            uiDispatcher: action => action());
    }

    private static ProjectRunSession CreateSession(Guid projectId, ProjectRunState state) => new()
    {
        SessionId = Guid.NewGuid(),
        ProjectId = projectId,
        ProjectName = "Test",
        CommandText = "dotnet run",
        ExecutablePath = "dotnet.exe",
        Arguments = Array.Empty<string>(),
        State = state
    };

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

    private sealed class FakeDatabasePathProvider : IDatabasePathProvider
    {
        public string GetDatabasePath() => @"C:\Data\devdesk.db";
        public void EnsureDirectoryExists() { }
        public string GetConnectionString() => "Data Source=InMemoryDb";
    }

    private sealed class FakeLauncherService : ILauncherService
    {
        public Task<LaunchResult> OpenInVsCodeAsync(string projectPath, CancellationToken cancellationToken = default) => Task.FromResult(LaunchResult.Ok());
        public Task<LaunchResult> OpenInExplorerAsync(string directoryPath, CancellationToken cancellationToken = default) => Task.FromResult(LaunchResult.Ok());
        public Task<LaunchResult> OpenTerminalAsync(string projectPath, CancellationToken cancellationToken = default) => Task.FromResult(LaunchResult.Ok());
    }

    private sealed class FakeDialogService : IDialogService
    {
        public bool ShowConfirmationDialog(string title, string message, string confirmButtonText = "Confirm") => true;
        public string? ShowFilePicker(string? filter = null, string? title = null) => null;
        public bool ShowAddEditProjectDialog(AddEditProjectViewModel viewModel) => true;
        public bool ShowConfirmDeleteDialog(ConfirmDeleteViewModel viewModel) => true;
        public bool ShowAddEditCommandDialog(DevDesk.App.ViewModels.Commands.AddEditCommandViewModel viewModel) => true;
        public bool ShowConfirmDeleteCommandDialog(DevDesk.App.ViewModels.Commands.ConfirmDeleteCommandViewModel viewModel) => true;
        public bool ShowConfirmShutdownDialog(int activeProjectCount, int activeCommandCount = 0) => true;
        public string? ShowFolderPicker(string? initialDirectory = null, string? title = null) => null;
        public void ShowMessage(string title, string message) { }
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public object? CurrentViewModel { get; set; }
        public NavigationItem CurrentItem { get; set; }
        public NavigationItem? LastNavigatedItem { get; private set; }
        public event Action? CurrentViewModelChanged;

        public void NavigateTo<TViewModel>() where TViewModel : class { }
        public void NavigateTo(NavigationItem item)
        {
            LastNavigatedItem = item;
            CurrentItem = item;
            CurrentViewModelChanged?.Invoke();
        }
    }

    private sealed class FakeProjectService : IProjectService
    {
        public List<DeveloperProject> Projects { get; } = new();
        public Task<IReadOnlyList<DeveloperProject>> GetProjectsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeveloperProject>>(Projects);
        public Task<DeveloperProject?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Projects.FirstOrDefault(p => p.Id == id));
        public Task<DeveloperProject> AddProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default) { Projects.Add(project); return Task.FromResult(project); }
        public Task<DeveloperProject> UpdateProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default) => Task.FromResult(project);
        public Task RemoveProjectAsync(Guid id, CancellationToken cancellationToken = default) { Projects.RemoveAll(p => p.Id == id); return Task.CompletedTask; }
        public Task<ProjectDetectionResult> DetectAndApplyAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(ProjectDetectionResult.Empty);
    }

    private sealed class FakeProjectRunnerService : IProjectRunnerService
    {
        public event EventHandler<ProjectRunSession>? SessionChanged { add { } remove { } }
        public event EventHandler<ProcessOutputEvent>? OutputReceived { add { } remove { } }

        public Task<ProjectRunResult> StartProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(ProjectRunResult.Succeeded(CreateSession(projectId, ProjectRunState.Running)));
        public Task<ProjectStopResult> StopProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(ProjectStopResult.Succeeded(projectId));
        public Task<ProjectRunResult> RestartProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(ProjectRunResult.Succeeded(CreateSession(projectId, ProjectRunState.Running)));
        public Task<IReadOnlyList<ProjectStopResult>> StopAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectStopResult>>(Array.Empty<ProjectStopResult>());
        public ProjectRunSession? GetSession(Guid projectId) => null;
        public IReadOnlyList<ProjectRunSession> GetActiveSessions() => Array.Empty<ProjectRunSession>();
        public IReadOnlyList<ManagedProcessIdentity> GetManagedProcesses() => Array.Empty<ManagedProcessIdentity>();
        public IReadOnlyList<ProjectRunSession> GetRecentSessions(Guid projectId) => Array.Empty<ProjectRunSession>();
        public IReadOnlyList<ProcessOutputEvent> GetSessionLogs(Guid projectId, Guid sessionId) => Array.Empty<ProcessOutputEvent>();
        public void Dispose() { }
    }

    private sealed class FakeGitService : IGitService
    {
        public Task<GitAvailability> CheckGitAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitAvailability(true, "2.40.0", @"C:\Program Files\Git\bin\git.exe"));
        public Task<GitRepositoryStatus> GetRepositoryStatusAsync(string projectPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(GitRepositoryStatus.NotGit());
    }

    private sealed class FakeSystemMonitorService : ISystemMonitorService
    {
        public SystemSnapshot? CurrentSnapshot => new();
        public event EventHandler<SystemSnapshot>? SnapshotUpdated { add { } remove { } }
        public void ResetBaselines() { }
        public Task<SystemSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SystemSnapshot());
    }

    #endregion
}
