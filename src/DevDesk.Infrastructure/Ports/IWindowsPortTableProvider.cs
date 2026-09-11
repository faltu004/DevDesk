namespace DevDesk.Infrastructure.Ports;

internal sealed record RawTcpListener(
    int LocalPort,
    string LocalAddress,
    int OwningProcessId,
    bool IsIPv6);

internal sealed class PortTableResult
{
    public IReadOnlyList<RawTcpListener> Listeners { get; init; } = Array.Empty<RawTcpListener>();

    public bool Success { get; init; }

    public bool HasPartialFailure { get; init; }

    public string? WarningMessage { get; init; }

    public string? ErrorMessage { get; init; }

    public static PortTableResult Ok(IReadOnlyList<RawTcpListener> listeners, bool partialFailure = false, string? warning = null) =>
        new() { Listeners = listeners, Success = true, HasPartialFailure = partialFailure, WarningMessage = warning };

    public static PortTableResult Failed(string error) =>
        new() { Success = false, ErrorMessage = error };
}

internal interface IWindowsPortTableProvider
{
    PortTableResult GetListeners(CancellationToken cancellationToken = default);
}
