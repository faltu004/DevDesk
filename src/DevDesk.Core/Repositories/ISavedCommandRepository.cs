using DevDesk.Core.Models;

namespace DevDesk.Core.Repositories;

/// <summary>
/// Repository interface for durable persistence of SavedCommand entities.
/// </summary>
public interface ISavedCommandRepository
{
    Task<IReadOnlyList<SavedCommand>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCommand>> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCommand>> GetGlobalCommandsAsync(CancellationToken cancellationToken = default);

    Task<SavedCommand?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SavedCommand> AddAsync(SavedCommand command, CancellationToken cancellationToken = default);

    Task<SavedCommand> UpdateAsync(SavedCommand command, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
