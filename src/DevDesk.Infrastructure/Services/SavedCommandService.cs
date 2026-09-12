using DevDesk.Core.Models;
using DevDesk.Core.Repositories;
using DevDesk.Core.Services;

namespace DevDesk.Infrastructure.Services;

public sealed class SavedCommandService : ISavedCommandService
{
    private static readonly HashSet<string> ProhibitedShellTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "cmd.exe", "powershell", "powershell.exe", "pwsh", "pwsh.exe"
    };

    private static readonly HashSet<string> TrustedShimToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "npm", "npx", "pnpm", "yarn", "corepack"
    };

    private readonly ISavedCommandRepository _repository;
    private readonly IProjectRepository _projectRepository;

    public SavedCommandService(
        ISavedCommandRepository repository,
        IProjectRepository projectRepository)
    {
        _repository = repository;
        _projectRepository = projectRepository;
    }

    public async Task<IReadOnlyList<SavedCommand>> GetAllCommandsAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.GetAllAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SavedCommand>> GetProjectCommandsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await _repository.GetByProjectIdAsync(projectId, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedCommand>> GetGlobalCommandsAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.GetGlobalCommandsAsync(cancellationToken);
    }

    public async Task<SavedCommand?> GetCommandByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<SavedCommand> SaveCommandAsync(SavedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (isValid, errorMessage) = ValidateCommand(command);
        if (!isValid)
        {
            throw new InvalidOperationException(errorMessage ?? "Command validation failed.");
        }

        if (command.ProjectId.HasValue)
        {
            var project = await _projectRepository.GetByIdAsync(command.ProjectId.Value, cancellationToken);
            if (project is null)
            {
                throw new InvalidOperationException($"Associated project with ID '{command.ProjectId.Value}' does not exist.");
            }
        }

        var existing = await _repository.GetByIdAsync(command.Id, cancellationToken);
        if (existing is null)
        {
            return await _repository.AddAsync(command, cancellationToken);
        }

        return await _repository.UpdateAsync(command, cancellationToken);
    }

    public async Task<bool> DeleteCommandAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _repository.DeleteAsync(id, cancellationToken);
    }

    public (bool IsValid, string? ErrorMessage) ValidateCommand(SavedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return (false, "Command name is required.");
        }

        if (command.Name.Length > 200)
        {
            return (false, "Command name cannot exceed 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(command.Executable))
        {
            return (false, "Executable is required.");
        }

        var trimmedExe = command.Executable.Trim();
        var fileName = Path.GetFileName(trimmedExe);
        var baseNameWithoutExt = Path.GetFileNameWithoutExtension(trimmedExe);

        // Disallow raw shell escape hatches
        if (ProhibitedShellTools.Contains(fileName) || ProhibitedShellTools.Contains(baseNameWithoutExt))
        {
            return (false, "Direct shell invocation ('cmd', 'powershell', 'pwsh') is prohibited. Specify the target developer tool directly.");
        }

        // Check for arbitrary batch scripts
        if (fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            if (!TrustedShimToolNames.Contains(baseNameWithoutExt))
            {
                return (false, "Execution of arbitrary .bat/.cmd scripts is not permitted in Phase 11. Use direct executables or trusted tools.");
            }
        }

        return (true, null);
    }

    public async Task<string> ResolveWorkingDirectoryAsync(SavedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Explicit command WorkingDirectory
        if (!string.IsNullOrWhiteSpace(command.WorkingDirectory))
        {
            return command.WorkingDirectory.Trim();
        }

        // 2. Associated project Path
        if (command.ProjectId.HasValue)
        {
            var project = await _projectRepository.GetByIdAsync(command.ProjectId.Value, cancellationToken);
            if (project is not null && !string.IsNullOrWhiteSpace(project.Path))
            {
                return project.Path;
            }
        }

        // 3. UserProfile fallback for global command
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
