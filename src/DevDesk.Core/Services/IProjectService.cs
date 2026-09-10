using DevDesk.Core.Detection;
using DevDesk.Core.Models;

namespace DevDesk.Core.Services;

/// <summary>
/// Service contract coordinating project lifecycle, validation rules, auto-detection, and persistence operations.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Retrieves all registered projects ordered by recency.
    /// </summary>
    Task<IReadOnlyList<DeveloperProject>> GetProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a project by its unique identifier.
    /// </summary>
    Task<DeveloperProject?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and adds a new project to DevDesk.
    /// </summary>
    Task<DeveloperProject> AddProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and updates an existing project in DevDesk.
    /// </summary>
    Task<DeveloperProject> UpdateProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a project registration from DevDesk. Physical files on disk are not deleted.
    /// </summary>
    Task RemoveProjectAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs static auto-detection against the project folder, merges recognized non-destructive metadata,
    /// persists updates, and returns the detection result.
    /// </summary>
    Task<ProjectDetectionResult> DetectAndApplyAsync(Guid id, CancellationToken cancellationToken = default);
}
