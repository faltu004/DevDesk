using Microsoft.EntityFrameworkCore;
using DevDesk.Core.Models;
using DevDesk.Core.Repositories;

namespace DevDesk.Infrastructure.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of the saved command repository utilizing IDbContextFactory for thread-safe operations.
/// </summary>
public sealed class SavedCommandRepository : ISavedCommandRepository
{
    private readonly IDbContextFactory<DevDeskDbContext> _contextFactory;

    public SavedCommandRepository(IDbContextFactory<DevDeskDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<SavedCommand>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SavedCommands
            .AsNoTracking()
            .Include(c => c.Project)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SavedCommand>> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SavedCommands
            .AsNoTracking()
            .Include(c => c.Project)
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SavedCommand>> GetGlobalCommandsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SavedCommands
            .AsNoTracking()
            .Where(c => c.ProjectId == null)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<SavedCommand?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SavedCommands
            .AsNoTracking()
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<SavedCommand> AddAsync(SavedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        command.CreatedAtUtc = DateTimeOffset.UtcNow;
        command.UpdatedAtUtc = command.CreatedAtUtc;
        
        // Ensure Project navigation reference is not accidentally tracked as a new insert
        var projectNav = command.Project;
        command.Project = null;

        await context.SavedCommands.AddAsync(command, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        command.Project = projectNav;
        return command;
    }

    public async Task<SavedCommand> UpdateAsync(SavedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SavedCommands.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
        if (existing is null)
        {
            throw new KeyNotFoundException($"SavedCommand with ID '{command.Id}' was not found.");
        }

        existing.Name = command.Name.Trim();
        existing.Description = command.Description?.Trim();
        existing.Executable = command.Executable.Trim();
        existing.Arguments = command.Arguments;
        existing.WorkingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory) ? null : command.WorkingDirectory.Trim();
        existing.Category = string.IsNullOrWhiteSpace(command.Category) ? null : command.Category.Trim();
        existing.IsEnabled = command.IsEnabled;
        existing.ProjectId = command.ProjectId;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SavedCommands.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        context.SavedCommands.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
