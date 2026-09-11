using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevDesk.Infrastructure.Git;

/// <summary>
/// Execution boundary for running Git commands securely and non-interactively.
/// </summary>
public interface IGitProcessRunner
{
    /// <summary>
    /// Executes a Git command with structured arguments in the specified working directory.
    /// </summary>
    Task<GitProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        int maxOutputBytes = 10 * 1024 * 1024,
        CancellationToken cancellationToken = default);
}
