using DevDesk.Core.Models;

namespace DevDesk.Core.Services;

/// <summary>
/// Domain service contract for managing saved developer commands, validation, and scoping.
/// </summary>
public interface ISavedCommandService
{
    Task<IReadOnlyList<SavedCommand>> GetAllCommandsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCommand>> GetProjectCommandsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCommand>> GetGlobalCommandsAsync(CancellationToken cancellationToken = default);

    Task<SavedCommand?> GetCommandByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SavedCommand> SaveCommandAsync(SavedCommand command, CancellationToken cancellationToken = default);

    Task<bool> DeleteCommandAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a command model for persistence rules (non-empty name, non-empty executable, valid ProjectId).
    /// Does not require executable or working directory to exist on disk at save time.
    /// </summary>
    (bool IsValid, string? ErrorMessage) ValidateCommand(SavedCommand command);

    /// <summary>
    /// Resolves the effective working directory for execution according to priority rules:
    /// 1. Explicit command WorkingDirectory
    /// 2. Associated project Path
    /// 3. UserProfile directory
    /// </summary>
    Task<string> ResolveWorkingDirectoryAsync(SavedCommand command, CancellationToken cancellationToken = default);
}
