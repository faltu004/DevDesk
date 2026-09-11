using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevDesk.Core.Git;
using DevDesk.Infrastructure.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevDesk.Tests.Git;

public class GitServiceTests
{
    private sealed class FakeGitToolLocator : IGitToolLocator
    {
        public bool IsInstalled { get; set; } = true;
        public string GitPath { get; set; } = @"C:\Program Files\Git\cmd\git.exe";

        public bool TryLocateGit(out string gitPath)
        {
            gitPath = IsInstalled ? GitPath : string.Empty;
            return IsInstalled;
        }
    }

    private sealed class FakeGitProcessRunner : IGitProcessRunner
    {
        public Func<string, IReadOnlyList<string>, GitProcessResult>? Handler { get; set; }
        public List<(string WorkingDirectory, IReadOnlyList<string> Arguments)> ExecutedCommands { get; } = new();

        public Task<GitProcessResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            TimeSpan? timeout = null,
            int maxOutputBytes = 10 * 1024 * 1024,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecutedCommands.Add((workingDirectory, arguments));

            if (Handler != null)
            {
                return Task.FromResult(Handler(workingDirectory, arguments));
            }

            return Task.FromResult(new GitProcessResult(0, string.Empty, string.Empty));
        }
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_GitNotInstalled_ReturnsGitNotInstalledError()
    {
        var locator = new FakeGitToolLocator { IsInstalled = false };
        var runner = new FakeGitProcessRunner();
        var service = new GitService(locator, runner, NullLogger<GitService>.Instance);

        var tempDir = Directory.GetCurrentDirectory();
        var status = await service.GetRepositoryStatusAsync(tempDir);

        Assert.False(status.IsGitRepository);
        Assert.Equal(GitErrorCode.GitNotInstalled, status.ErrorCode);
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_DirectoryDoesNotExist_ReturnsPathNotFound()
    {
        var locator = new FakeGitToolLocator();
        var runner = new FakeGitProcessRunner();
        var service = new GitService(locator, runner, NullLogger<GitService>.Instance);

        var status = await service.GetRepositoryStatusAsync(@"D:\NonExistent_Directory_12345");

        Assert.False(status.IsGitRepository);
        Assert.Equal(GitErrorCode.PathNotFound, status.ErrorCode);
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_NonGitDirectory_ReturnsNotAGitRepository()
    {
        var locator = new FakeGitToolLocator();
        var runner = new FakeGitProcessRunner
        {
            Handler = (cwd, args) =>
            {
                if (args.Contains("rev-parse"))
                {
                    return new GitProcessResult(128, string.Empty, "fatal: not a git repository (or any of the parent directories): .git");
                }
                return new GitProcessResult(0, string.Empty, string.Empty);
            }
        };

        var service = new GitService(locator, runner, NullLogger<GitService>.Instance);
        var tempDir = Directory.GetCurrentDirectory();
        var status = await service.GetRepositoryStatusAsync(tempDir);

        Assert.False(status.IsGitRepository);
        Assert.Equal(GitErrorCode.NotAGitRepository, status.ErrorCode);
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_NestedDirectory_ResolvesAuthoritativeRoot()
    {
        var projectDir = Directory.GetCurrentDirectory();
        var repoRoot = @"D:\Projects\SuperApp";

        var locator = new FakeGitToolLocator();
        var runner = new FakeGitProcessRunner
        {
            Handler = (cwd, args) =>
            {
                if (args.Contains("rev-parse"))
                {
                    return new GitProcessResult(0, repoRoot + "\n", string.Empty);
                }
                if (args.Contains("status"))
                {
                    return new GitProcessResult(0, "# branch.oid 1234567\0# branch.head main\0", string.Empty);
                }
                if (args.Contains("log"))
                {
                    return new GitProcessResult(0, "hash\0short\0Author\0email\02026-09-11T12:00:00Z\0Initial commit\0", string.Empty);
                }
                return new GitProcessResult(0, string.Empty, string.Empty);
            }
        };

        var service = new GitService(locator, runner, NullLogger<GitService>.Instance);
        var status = await service.GetRepositoryStatusAsync(projectDir);

        Assert.True(status.IsGitRepository);
        Assert.Equal(repoRoot, status.RepositoryRoot);

        // Verify status and log commands were run against the resolved repository root, not nested project dir
        Assert.Contains(runner.ExecutedCommands, c => c.Arguments.Contains("status") && c.Arguments.Contains(repoRoot));
        Assert.Contains(runner.ExecutedCommands, c => c.Arguments.Contains("log") && c.Arguments.Contains(repoRoot));
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_GitWorktree_ResolvesWithoutDotGitDirRequirement()
    {
        var worktreeDir = Directory.GetCurrentDirectory();
        var locator = new FakeGitToolLocator();
        var runner = new FakeGitProcessRunner
        {
            Handler = (cwd, args) =>
            {
                if (args.Contains("rev-parse"))
                {
                    return new GitProcessResult(0, worktreeDir + "\n", string.Empty);
                }
                if (args.Contains("status"))
                {
                    return new GitProcessResult(0, "# branch.head worktree-branch\0", string.Empty);
                }
                return new GitProcessResult(0, string.Empty, string.Empty);
            }
        };

        var service = new GitService(locator, runner, NullLogger<GitService>.Instance);
        var status = await service.GetRepositoryStatusAsync(worktreeDir);

        Assert.True(status.IsGitRepository);
        Assert.Equal("worktree-branch", status.Branch?.BranchName);
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_SafeDirectoryRestriction_ReturnsSafeDirectoryViolation()
    {
        var locator = new FakeGitToolLocator();
        var runner = new FakeGitProcessRunner
        {
            Handler = (cwd, args) => new GitProcessResult(128, string.Empty, "fatal: detected dubious ownership in repository at 'D:/Repo'")
        };

        var service = new GitService(locator, runner, NullLogger<GitService>.Instance);
        var tempDir = Directory.GetCurrentDirectory();
        var status = await service.GetRepositoryStatusAsync(tempDir);

        Assert.False(status.IsGitRepository);
        Assert.Equal(GitErrorCode.SafeDirectoryViolation, status.ErrorCode);
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_AccessDenied_ReturnsAccessDeniedError()
    {
        var locator = new FakeGitToolLocator();
        var runner = new FakeGitProcessRunner
        {
            Handler = (cwd, args) => new GitProcessResult(128, string.Empty, "fatal: Permission denied")
        };

        var service = new GitService(locator, runner, NullLogger<GitService>.Instance);
        var tempDir = Directory.GetCurrentDirectory();
        var status = await service.GetRepositoryStatusAsync(tempDir);

        Assert.False(status.IsGitRepository);
        Assert.Equal(GitErrorCode.AccessDenied, status.ErrorCode);
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_TimedOut_ReturnsTimeoutError()
    {
        var locator = new FakeGitToolLocator();
        var runner = new FakeGitProcessRunner
        {
            Handler = (cwd, args) => new GitProcessResult(0, string.Empty, string.Empty, IsTimedOut: true)
        };

        var service = new GitService(locator, runner, NullLogger<GitService>.Instance);
        var tempDir = Directory.GetCurrentDirectory();
        var status = await service.GetRepositoryStatusAsync(tempDir);

        Assert.False(status.IsGitRepository);
        Assert.Equal(GitErrorCode.Timeout, status.ErrorCode);
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_CallerCancellation_ThrowsOperationCanceledException()
    {
        var locator = new FakeGitToolLocator();
        var runner = new FakeGitProcessRunner();
        var service = new GitService(locator, runner, NullLogger<GitService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var tempDir = Directory.GetCurrentDirectory();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetRepositoryStatusAsync(tempDir, cts.Token));
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_OutputTooLarge_ReturnsOutputTooLargeError()
    {
        var projectDir = Directory.GetCurrentDirectory();
        var locator = new FakeGitToolLocator();
        var runner = new FakeGitProcessRunner
        {
            Handler = (cwd, args) =>
            {
                if (args.Contains("rev-parse")) return new GitProcessResult(0, projectDir + "\n", string.Empty);
                if (args.Contains("status")) return new GitProcessResult(-1, string.Empty, "Exceeded limit", IsOutputTruncated: true);
                return new GitProcessResult(0, string.Empty, string.Empty);
            }
        };

        var service = new GitService(locator, runner, NullLogger<GitService>.Instance);
        var status = await service.GetRepositoryStatusAsync(projectDir);

        Assert.False(status.IsGitRepository);
        Assert.Equal(GitErrorCode.OutputTooLarge, status.ErrorCode);
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_StructuredArguments_PassedCorrectlyWithoutShell()
    {
        var projectDir = Directory.GetCurrentDirectory();
        var locator = new FakeGitToolLocator();
        var runner = new FakeGitProcessRunner
        {
            Handler = (cwd, args) =>
            {
                if (args.Contains("rev-parse")) return new GitProcessResult(0, projectDir + "\n", string.Empty);
                if (args.Contains("status")) return new GitProcessResult(0, "# branch.oid (initial)\0# branch.head main\0", string.Empty);
                return new GitProcessResult(0, string.Empty, string.Empty);
            }
        };

        var service = new GitService(locator, runner, NullLogger<GitService>.Instance);
        await service.GetRepositoryStatusAsync(projectDir);

        foreach (var cmd in runner.ExecutedCommands)
        {
            // Verify structured passing: "--no-pager", "-C", path are separate arguments
            Assert.Contains("--no-pager", cmd.Arguments);
            Assert.Contains("-C", cmd.Arguments);
            Assert.DoesNotContain("cmd.exe", cmd.Arguments);
            Assert.DoesNotContain("powershell", cmd.Arguments);
        }
    }

    [Fact]
    public async Task GetRepositoryStatusAsync_UnbornRepository_SkipsLogExecution()
    {
        var projectDir = Directory.GetCurrentDirectory();
        var locator = new FakeGitToolLocator();
        var runner = new FakeGitProcessRunner
        {
            Handler = (cwd, args) =>
            {
                if (args.Contains("rev-parse")) return new GitProcessResult(0, projectDir + "\n", string.Empty);
                if (args.Contains("status")) return new GitProcessResult(0, "# branch.oid (initial)\0# branch.head main\0", string.Empty);
                return new GitProcessResult(0, string.Empty, string.Empty);
            }
        };

        var service = new GitService(locator, runner, NullLogger<GitService>.Instance);
        var status = await service.GetRepositoryStatusAsync(projectDir);

        Assert.True(status.IsGitRepository);
        Assert.True(status.Branch?.IsUnborn);
        Assert.Null(status.LastCommit);
        Assert.DoesNotContain(runner.ExecutedCommands, c => c.Arguments.Contains("log"));
    }
}
