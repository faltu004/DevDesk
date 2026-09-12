namespace DevDesk.Infrastructure.Commands;

public sealed record ResolvedCommandTool
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public bool IsCmdShim { get; init; }

    public static ResolvedCommandTool Succeeded(string executablePath, bool isCmdShim) =>
        new() { Success = true, ExecutablePath = executablePath, IsCmdShim = isCmdShim };

    public static ResolvedCommandTool Failed(string errorMessage) =>
        new() { Success = false, ErrorMessage = errorMessage };
}

public interface ISavedCommandToolResolver
{
    ResolvedCommandTool ResolveTool(string rawExecutable, IReadOnlyList<string> arguments);
}
