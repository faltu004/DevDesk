using System;
using System.Threading;
using System.Threading.Tasks;
using DevDesk.App.ViewModels.Projects;
using DevDesk.Core.Git;
using DevDesk.Core.Launchers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevDesk.Tests.Git;

public class ProjectGitViewModelTests
{
    private sealed class FakeLauncherService : ILauncherService
    {
        public string? LastExplorerPath { get; private set; }
        public string? LastTerminalPath { get; private set; }

        public Task<LaunchResult> OpenInVsCodeAsync(string projectPath, CancellationToken cancellationToken = default)
            => Task.FromResult(LaunchResult.Ok());

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

    private sealed class FakeGitService : IGitService
    {
        public Func<string, CancellationToken, Task<GitRepositoryStatus>>? StatusFunc { get; set; }

        public Task<GitAvailability> CheckGitAvailabilityAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new GitAvailability(true, "git.exe", "git version 2.45"));

        public Task<GitRepositoryStatus> GetRepositoryStatusAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            if (StatusFunc != null)
            {
                return StatusFunc(projectPath, cancellationToken);
            }

            return Task.FromResult(new GitRepositoryStatus
            {
                IsGitRepository = true,
                RepositoryRoot = projectPath,
                Branch = new GitBranchInfo("main", "origin/main", 0, 0),
                WorkingTree = new GitWorkingTreeSummary(0, 0, 0, 0),
                LastCommit = new GitCommitInfo("hash", "short", "Author", "email", DateTimeOffset.UtcNow, "subject")
            });
        }
    }

    [Fact]
    public async Task SetProject_AndActivate_RefreshesGitStatus()
    {
        var gitService = new FakeGitService();
        var launcher = new FakeLauncherService();
        var vm = new ProjectGitViewModel(gitService, launcher, NullLogger<ProjectGitViewModel>.Instance);

        var projectId = Guid.NewGuid();
        var projectPath = @"D:\Projects\AppOne";

        vm.SetProject(projectId, projectPath);
        vm.Activate();

        // Allow background async task to finish
        for (int i = 0; i < 20 && vm.IsLoading; i++)
        {
            await Task.Delay(20);
        }

        Assert.True(vm.IsGitRepository);
        Assert.Equal("main", vm.BranchName);
        Assert.Equal("origin/main", vm.UpstreamBranch);
        Assert.Equal("Up to date", vm.SyncStatusDisplay);
        Assert.Equal(0, vm.StagedCount);
        Assert.True(vm.IsWorkingTreeClean);
        Assert.True(vm.HasLastCommit);
    }

    [Fact]
    public async Task StaleRefresh_DoesNotOverwriteNewlySelectedProject()
    {
        var project1Id = Guid.NewGuid();
        var project2Id = Guid.NewGuid();

        var tcs1 = new TaskCompletionSource<GitRepositoryStatus>();

        var gitService = new FakeGitService
        {
            StatusFunc = (path, ct) =>
            {
                if (path.Contains("Project1"))
                {
                    return tcs1.Task;
                }

                return Task.FromResult(new GitRepositoryStatus
                {
                    IsGitRepository = true,
                    RepositoryRoot = @"D:\Project2",
                    Branch = new GitBranchInfo("feature-project2"),
                    WorkingTree = new GitWorkingTreeSummary(2, 1, 0, 0)
                });
            }
        };

        var launcher = new FakeLauncherService();
        var vm = new ProjectGitViewModel(gitService, launcher, NullLogger<ProjectGitViewModel>.Instance);
        vm.Activate();

        // 1. Select Project 1 (stuck waiting on tcs1)
        vm.SetProject(project1Id, @"D:\Project1");
        await Task.Delay(20);

        // 2. Quickly select Project 2
        vm.SetProject(project2Id, @"D:\Project2");

        for (int i = 0; i < 20 && vm.IsLoading; i++)
        {
            await Task.Delay(20);
        }

        // Project 2 should be active
        Assert.Equal("feature-project2", vm.BranchName);
        Assert.Equal(2, vm.StagedCount);

        // 3. Now let Project 1 finish late
        tcs1.SetResult(new GitRepositoryStatus
        {
            IsGitRepository = true,
            RepositoryRoot = @"D:\Project1",
            Branch = new GitBranchInfo("stale-project1-branch"),
            WorkingTree = new GitWorkingTreeSummary(99, 99, 0, 0)
        });

        await Task.Delay(50);

        // Verify Project 1 result was safely ignored!
        Assert.Equal("feature-project2", vm.BranchName);
        Assert.Equal(2, vm.StagedCount);
    }

    [Fact]
    public async Task OpenFolderCommand_CallsLauncherServiceWithRepositoryRoot()
    {
        var gitService = new FakeGitService();
        var launcher = new FakeLauncherService();
        var vm = new ProjectGitViewModel(gitService, launcher, NullLogger<ProjectGitViewModel>.Instance);

        vm.SetProject(Guid.NewGuid(), @"D:\Projects\MyApp\SubFolder");
        vm.ApplyStatus(new GitRepositoryStatus
        {
            IsGitRepository = true,
            RepositoryRoot = @"D:\Projects\MyApp"
        });

        await vm.OpenFolderAsync();

        Assert.Equal(@"D:\Projects\MyApp", launcher.LastExplorerPath);
    }

    [Fact]
    public async Task OpenTerminalCommand_CallsLauncherServiceWithRepositoryRoot()
    {
        var gitService = new FakeGitService();
        var launcher = new FakeLauncherService();
        var vm = new ProjectGitViewModel(gitService, launcher, NullLogger<ProjectGitViewModel>.Instance);

        vm.SetProject(Guid.NewGuid(), @"D:\Projects\MyApp\SubFolder");
        vm.ApplyStatus(new GitRepositoryStatus
        {
            IsGitRepository = true,
            RepositoryRoot = @"D:\Projects\MyApp"
        });

        await vm.OpenTerminalAsync();

        Assert.Equal(@"D:\Projects\MyApp", launcher.LastTerminalPath);
    }

    [Fact]
    public void SyncStatusDisplay_ReflectsDivergenceAccurately()
    {
        var gitService = new FakeGitService();
        var launcher = new FakeLauncherService();
        var vm = new ProjectGitViewModel(gitService, launcher, NullLogger<ProjectGitViewModel>.Instance);

        // No upstream
        vm.ApplyStatus(new GitRepositoryStatus
        {
            IsGitRepository = true,
            Branch = new GitBranchInfo("main", null, null, null)
        });
        Assert.Equal("No upstream", vm.SyncStatusDisplay);

        // Up to date
        vm.ApplyStatus(new GitRepositoryStatus
        {
            IsGitRepository = true,
            Branch = new GitBranchInfo("main", "origin/main", 0, 0)
        });
        Assert.Equal("Up to date", vm.SyncStatusDisplay);

        // Ahead
        vm.ApplyStatus(new GitRepositoryStatus
        {
            IsGitRepository = true,
            Branch = new GitBranchInfo("main", "origin/main", 3, 0)
        });
        Assert.Equal("+3 ahead", vm.SyncStatusDisplay);

        // Behind
        vm.ApplyStatus(new GitRepositoryStatus
        {
            IsGitRepository = true,
            Branch = new GitBranchInfo("main", "origin/main", 0, 2)
        });
        Assert.Equal("-2 behind", vm.SyncStatusDisplay);

        // Diverged
        vm.ApplyStatus(new GitRepositoryStatus
        {
            IsGitRepository = true,
            Branch = new GitBranchInfo("main", "origin/main", 4, 1)
        });
        Assert.Equal("+4 / -1", vm.SyncStatusDisplay);
    }
}
