namespace DevDesk.Infrastructure.Runner;

/// <summary>
/// Abstraction representing an active or completed process instance managed by DevDesk.
/// Decouples runner coordination from concrete System.Diagnostics.Process for deterministic testing.
/// </summary>
internal interface IManagedProcess : IDisposable
{
    int ProcessId { get; }
    bool HasExited { get; }
    int? ExitCode { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken = default);
    void KillEntireProcessTree();
    IReadOnlyList<int> GetActiveProcessIds();
    bool ContainsProcessHandle(IntPtr processHandle);

    event EventHandler<string>? StandardOutputReceived;
    event EventHandler<string>? StandardErrorReceived;
    event EventHandler<int>? Exited;
}

/// <summary>
/// Configuration for spawning a managed process.
/// </summary>
internal sealed record ProcessLaunchConfiguration
{
    public required string WorkingDirectory { get; init; }
    public required string ExecutablePath { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public bool IsCmdShim { get; init; }
}

/// <summary>
/// Factory abstraction for creating managed process instances.
/// </summary>
internal interface IProcessLauncher
{
    IManagedProcess Launch(ProcessLaunchConfiguration config);
}
