using Microsoft.Extensions.Logging;
using DevDesk.Core.Common;
using DevDesk.Core.Detection;
using DevDesk.Core.Models;
using DevDesk.Core.Repositories;
using DevDesk.Core.Services;

namespace DevDesk.Infrastructure.Services;

/// <summary>
/// Service implementation managing project lifecycle, validation rules, auto-detection, and persistence operations.
/// </summary>
public sealed class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepository;
    private readonly IProjectDetectionService _detectionService;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(
        IProjectRepository projectRepository,
        IProjectDetectionService detectionService,
        ILogger<ProjectService> logger)
    {
        _projectRepository = projectRepository;
        _detectionService = detectionService;
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

        // Non-destructive initial detection if metadata was not manually filled
        try
        {
            var detected = await _detectionService.DetectAsync(normalizedPath, cancellationToken);
            project.IsGitRepository = detected.IsGitRepository;

            if (detected.IsRecognized)
            {
                if (string.IsNullOrWhiteSpace(project.Framework) && !string.IsNullOrWhiteSpace(detected.Framework))
                {
                    project.Framework = detected.Framework;
                }
                if (string.IsNullOrWhiteSpace(project.Language) && !string.IsNullOrWhiteSpace(detected.Language))
                {
                    project.Language = detected.Language;
                }
                if (string.IsNullOrWhiteSpace(project.PackageManager) && !string.IsNullOrWhiteSpace(detected.PackageManager))
                {
                    project.PackageManager = detected.PackageManager;
                }
                if (string.IsNullOrWhiteSpace(project.RunCommand) && !string.IsNullOrWhiteSpace(detected.SuggestedRunCommand))
                {
                    project.RunCommand = detected.SuggestedRunCommand;
                }
                if (string.IsNullOrWhiteSpace(project.BuildCommand) && !string.IsNullOrWhiteSpace(detected.SuggestedBuildCommand))
                {
                    project.BuildCommand = detected.SuggestedBuildCommand;
                }
                if (string.IsNullOrWhiteSpace(project.TestCommand) && !string.IsNullOrWhiteSpace(detected.SuggestedTestCommand))
                {
                    project.TestCommand = detected.SuggestedTestCommand;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial detection failed non-critically for new project at '{Path}'", normalizedPath);
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

    public async Task<ProjectDetectionResult> DetectAndApplyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            _logger.LogWarning("Cannot detect project: ID {Id} not found", id);
            throw new KeyNotFoundException($"Project with ID '{id}' was not found.");
        }

        var detected = await _detectionService.DetectAsync(project.Path, cancellationToken);

        // Update IsGitRepository to reflect latest filesystem truth
        project.IsGitRepository = detected.IsGitRepository;

        if (detected.IsRecognized)
        {
            // Refresh framework, language, and package manager from deterministic evidence
            if (!string.IsNullOrWhiteSpace(detected.Framework))
            {
                project.Framework = detected.Framework;
            }
            if (!string.IsNullOrWhiteSpace(detected.Language))
            {
                project.Language = detected.Language;
            }
            if (!string.IsNullOrWhiteSpace(detected.PackageManager))
            {
                project.PackageManager = detected.PackageManager;
            }

            // Only populate blank commands; NEVER overwrite existing user configuration
            if (string.IsNullOrWhiteSpace(project.RunCommand) && !string.IsNullOrWhiteSpace(detected.SuggestedRunCommand))
            {
                project.RunCommand = detected.SuggestedRunCommand;
            }
            if (string.IsNullOrWhiteSpace(project.BuildCommand) && !string.IsNullOrWhiteSpace(detected.SuggestedBuildCommand))
            {
                project.BuildCommand = detected.SuggestedBuildCommand;
            }
            if (string.IsNullOrWhiteSpace(project.TestCommand) && !string.IsNullOrWhiteSpace(detected.SuggestedTestCommand))
            {
                project.TestCommand = detected.SuggestedTestCommand;
            }

            // Only populate blank port if an explicit safe static value was detected
            if (!project.DefaultPort.HasValue && detected.SuggestedDefaultPort.HasValue)
            {
                project.DefaultPort = detected.SuggestedDefaultPort;
            }
        }

        await _projectRepository.UpdateAsync(project, cancellationToken);
        _logger.LogInformation("Applied detection results to project '{Name}' ({Id}). Recognized: {Recognized}, IsGit: {IsGit}",
            project.Name, project.Id, detected.IsRecognized, project.IsGitRepository);

        return detected;
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
