using DevDesk.Core.Models;

namespace DevDesk.Core.Repositories;

/// <summary>
/// Repository contract for managing persisted developer projects.
/// </summary>
public interface IProjectRepository
{
    /// <summary>
    /// Retrieves all registered developer projects.
    /// </summary>
    Task<IReadOnlyList<DeveloperProject>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a project by its unique identifier.
    /// </summary>
    Task<DeveloperProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a project matching the specified filesystem path.
    /// </summary>
    Task<DeveloperProject?> GetByPathAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new project to persistence.
    /// </summary>
    Task AddAsync(DeveloperProject project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing project's persistent state.
    /// </summary>
    Task UpdateAsync(DeveloperProject project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a project by its unique identifier.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
