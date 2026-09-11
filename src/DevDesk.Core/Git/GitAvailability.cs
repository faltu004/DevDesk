namespace DevDesk.Core.Git;

/// <summary>
/// Status of Git executable availability on the local machine.
/// </summary>
public sealed record GitAvailability(
    bool IsAvailable,
    string? ExecutablePath = null,
    string? Version = null,
    string? ErrorMessage = null);
