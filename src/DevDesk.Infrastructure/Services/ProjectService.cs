using Microsoft.Extensions.Logging;
using DevDesk.Core.Common;
using DevDesk.Core.Models;
using DevDesk.Core.Repositories;
using DevDesk.Core.Services;

namespace DevDesk.Infrastructure.Services;

/// <summary>
/// Service implementation managing project lifecycle, validation rules, and persistence operations.
/// </summary>
public sealed class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepository;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(
        IProjectRepository projectRepository,
        ILogger<ProjectService> logger)
    {
        _projectRepository = projectRepository;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DeveloperProject>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        return await _projectRepository.GetAllAsync(cancellationToken);
    }

    public async Task<DeveloperProject?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _projectRepository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<DeveloperProject> AddProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        ValidateProject(project);

        var normalizedPath = PathHelper.NormalizePath(project.Path);
        project.Path = normalizedPath;

        if (!Directory.Exists(normalizedPath))
        {
            _logger.LogWarning("Project directory does not exist: {Path}", normalizedPath);
            throw new DirectoryNotFoundException("The selected project folder could not be found.");
        }

        var existingProject = await _projectRepository.GetByPathAsync(normalizedPath, cancellationToken);
        if (existingProject is not null)
        {
            _logger.LogWarning("Attempted to register duplicate project path: {Path}", normalizedPath);
            throw new InvalidOperationException("This project is already registered in DevDesk.");
        }

        SanitizeCommands(project);

        if (project.CreatedAt == default)
        {
            project.CreatedAt = DateTimeOffset.UtcNow;
        }

        await _projectRepository.AddAsync(project, cancellationToken);
        _logger.LogInformation("Registered new project '{Name}' at '{Path}' with ID {Id}", project.Name, project.Path, project.Id);

        return project;
    }

    public async Task<DeveloperProject> UpdateProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        ValidateProject(project);

        var existing = await _projectRepository.GetByIdAsync(project.Id, cancellationToken);
        if (existing is null)
        {
            _logger.LogWarning("Project with ID {Id} not found for update", project.Id);
            throw new KeyNotFoundException($"Project with ID '{project.Id}' was not found.");
        }

        var normalizedPath = PathHelper.NormalizePath(project.Path);
        project.Path = normalizedPath;

        if (!Directory.Exists(normalizedPath))
        {
            _logger.LogWarning("Project directory does not exist during update: {Path}", normalizedPath);
            throw new DirectoryNotFoundException("The selected project folder could not be found.");
        }

        var duplicate = await _projectRepository.GetByPathAsync(normalizedPath, cancellationToken);
        if (duplicate is not null && duplicate.Id != project.Id)
        {
            _logger.LogWarning("Project path conflict during update: {Path}", normalizedPath);
            throw new InvalidOperationException("This project is already registered in DevDesk.");
        }

        SanitizeCommands(project);

        await _projectRepository.UpdateAsync(project, cancellationToken);
        _logger.LogInformation("Updated project '{Name}' ({Id})", project.Name, project.Id);

        return project;
    }

    public async Task RemoveProjectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing project record with ID {Id} from DevDesk (physical files untouched)", id);
        await _projectRepository.DeleteAsync(id, cancellationToken);
    }

    private static void ValidateProject(DeveloperProject project)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
        {
            throw new ArgumentException("Project name is required.", nameof(project));
        }

        project.Name = project.Name.Trim();

        if (string.IsNullOrWhiteSpace(project.Path))
        {
            throw new ArgumentException("Project path is required.", nameof(project));
        }

        if (project.DefaultPort.HasValue && project.DefaultPort.Value is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(project.DefaultPort), project.DefaultPort, "Default port must be between 1 and 65535.");
        }
    }

    private static void SanitizeCommands(DeveloperProject project)
    {
        project.RunCommand = string.IsNullOrWhiteSpace(project.RunCommand) ? null : project.RunCommand.Trim();
        project.BuildCommand = string.IsNullOrWhiteSpace(project.BuildCommand) ? null : project.BuildCommand.Trim();
        project.TestCommand = string.IsNullOrWhiteSpace(project.TestCommand) ? null : project.TestCommand.Trim();
        project.Framework = string.IsNullOrWhiteSpace(project.Framework) ? null : project.Framework.Trim();
        project.Language = string.IsNullOrWhiteSpace(project.Language) ? null : project.Language.Trim();
        project.PackageManager = string.IsNullOrWhiteSpace(project.PackageManager) ? null : project.PackageManager.Trim();
    }
}
