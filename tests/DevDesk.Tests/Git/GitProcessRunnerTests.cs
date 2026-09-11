using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevDesk.Infrastructure.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevDesk.Tests.Git;

public class GitProcessRunnerTests
{
    private sealed class StubToolLocator : IGitToolLocator
    {
        public bool IsAvailable { get; set; } = true;
        public string Path { get; set; } = "git.exe";

        public bool TryLocateGit(out string gitPath)
        {
            gitPath = IsAvailable ? Path : string.Empty;
            return IsAvailable;
        }
    }

    [Fact]
    public async Task RunAsync_WhenGitNotAvailable_ReturnsNonZeroResultWithoutThrowing()
    {
        var locator = new StubToolLocator { IsAvailable = false };
        var runner = new GitProcessRunner(locator, NullLogger<GitProcessRunner>.Instance);

        var result = await runner.RunAsync(Directory.GetCurrentDirectory(), ["--version"]);

        Assert.False(result.IsSuccess);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("not found", result.StandardError);
    }

    [Fact]
    public async Task RunAsync_WhenCallerTokenCancelled_ThrowsOperationCanceledException()
    {
        var locator = new StubToolLocator();
        var runner = new GitProcessRunner(locator, NullLogger<GitProcessRunner>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(Directory.GetCurrentDirectory(), ["--version"], cancellationToken: cts.Token));
    }
}
