using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevDesk.Core.Common;
using DevDesk.Core.Git;
using Microsoft.Extensions.Logging;

namespace DevDesk.Infrastructure.Git;

/// <summary>
/// Authoritative Git service implementing inspection-only queries for repository status,
/// topology, working tree modifications, and last commit metadata.
/// </summary>
public sealed class GitService : IGitService
{
    private readonly IGitToolLocator _toolLocator;
    private readonly IGitProcessRunner _runner;
    private readonly ILogger<GitService> _logger;

    public GitService(
        IGitToolLocator toolLocator,
        IGitProcessRunner runner,
        ILogger<GitService> logger)
    {
        _toolLocator = toolLocator ?? throw new ArgumentNullException(nameof(toolLocator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GitAvailability> CheckGitAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        if (!_toolLocator.TryLocateGit(out var gitPath))
        {
            return new GitAvailability(false, null, null, "Git executable not found on system PATH");
        }

        try
        {
            var result = await _runner.RunAsync(
                workingDirectory: string.Empty,
                arguments: ["--version"],
                timeout: TimeSpan.FromSeconds(3),
                cancellationToken: cancellationToken);

            if (result.IsSuccess)
            {
                var version = result.StandardOutput.Trim();
                return new GitAvailability(true, gitPath, version);
            }

            return new GitAvailability(false, gitPath, null, result.StandardError.Trim());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Git availability");
            return new GitAvailability(false, gitPath, null, ex.Message);
        }
    }

    public async Task<GitRepositoryStatus> GetRepositoryStatusAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return GitRepositoryStatus.Error(GitErrorCode.PathNotFound, "Project path cannot be empty");
        }

        if (!Directory.Exists(projectPath))
        {
            return GitRepositoryStatus.Error(GitErrorCode.PathNotFound, $"Directory does not exist: {projectPath}");
        }

        if (!_toolLocator.TryLocateGit(out _))
        {
            return GitRepositoryStatus.Error(GitErrorCode.GitNotInstalled, "Git executable not found on system PATH");
        }

        // 1. Authoritative repository root resolution via git rev-parse --show-toplevel
        var rootResult = await _runner.RunAsync(
            workingDirectory: projectPath,
            arguments: ["--no-pager", "-C", projectPath, "rev-parse", "--show-toplevel"],
            timeout: TimeSpan.FromSeconds(5),
            cancellationToken: cancellationToken);

        if (rootResult.IsTimedOut)
        {
            return GitRepositoryStatus.Error(GitErrorCode.Timeout, "Git rev-parse timed out");
        }

        if (rootResult.ExitCode != 0)
        {
            var err = rootResult.StandardError ?? string.Empty;
            if (err.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
            {
                return GitRepositoryStatus.NotGit();
            }

            if (err.Contains("detected dubious ownership", StringComparison.OrdinalIgnoreCase))
            {
                return GitRepositoryStatus.Error(
                    GitErrorCode.SafeDirectoryViolation,
                    "Git detected dubious ownership in repository (safe.directory).");
            }

            if (err.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
                err.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            {
                return GitRepositoryStatus.Error(
                    GitErrorCode.AccessDenied,
                    "Access denied to Git repository.");
            }

            return GitRepositoryStatus.NotGit(err.Trim());
        }

        var repositoryRootRaw = rootResult.StandardOutput.Trim();
        if (string.IsNullOrEmpty(repositoryRootRaw))
        {
            return GitRepositoryStatus.NotGit();
        }

        var repositoryRoot = PathHelper.NormalizePath(repositoryRootRaw);

        // 2. Machine-safe status via git status --porcelain=v2 -z --branch --untracked-files=all
        var statusResult = await _runner.RunAsync(
            workingDirectory: repositoryRoot,
            arguments: ["--no-pager", "-C", repositoryRoot, "status", "--porcelain=v2", "-z", "--branch", "--untracked-files=all"],
            timeout: TimeSpan.FromSeconds(5),
            maxOutputBytes: 10 * 1024 * 1024,
            cancellationToken: cancellationToken);

        if (statusResult.IsTimedOut)
        {
            return GitRepositoryStatus.Error(GitErrorCode.Timeout, "Git status timed out", repositoryRoot);
        }

        if (statusResult.IsOutputTruncated)
        {
            return GitRepositoryStatus.Error(
                GitErrorCode.OutputTooLarge,
                "Working tree status exceeded memory limit of 10 MB.",
                repositoryRoot);
        }

        if (statusResult.ExitCode != 0)
        {
            var err = statusResult.StandardError ?? string.Empty;
            if (err.Contains("index.lock", StringComparison.OrdinalIgnoreCase))
            {
                return GitRepositoryStatus.Error(
                    GitErrorCode.LockedOrBusy,
                    "Git index is locked by another process (.git/index.lock).",
                    repositoryRoot);
            }

            return GitRepositoryStatus.Error(
                GitErrorCode.ExecutionFailed,
                $"Git status failed: {err.Trim()}",
                repositoryRoot);
        }

        var parsed = GitPorcelainParser.Parse(statusResult.StandardOutput);

        // 3. Last commit details via git log -1 (only if commits exist)
        GitCommitInfo? lastCommit = null;
        if (!parsed.Branch.IsUnborn)
        {
            var logResult = await _runner.RunAsync(
                workingDirectory: repositoryRoot,
                arguments: ["--no-pager", "-C", repositoryRoot, "log", "-1", "--format=%H%x00%h%x00%an%x00%ae%x00%cI%x00%s"],
                timeout: TimeSpan.FromSeconds(5),
                cancellationToken: cancellationToken);

            if (logResult.IsSuccess && !string.IsNullOrWhiteSpace(logResult.StandardOutput))
            {
                lastCommit = GitLogParser.Parse(logResult.StandardOutput);
            }
        }

        return new GitRepositoryStatus
        {
            IsGitRepository = true,
            RepositoryRoot = repositoryRoot,
            Branch = parsed.Branch,
            WorkingTree = parsed.WorkingTree,
            LastCommit = lastCommit,
            ErrorCode = GitErrorCode.None,
            ErrorMessage = null,
            CapturedAtUtc = DateTimeOffset.UtcNow
        };
    }
}
