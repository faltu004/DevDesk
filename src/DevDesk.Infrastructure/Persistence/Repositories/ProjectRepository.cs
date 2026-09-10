using Microsoft.EntityFrameworkCore;
using DevDesk.Core.Common;
using DevDesk.Core.Models;
using DevDesk.Core.Repositories;

namespace DevDesk.Infrastructure.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of the project repository utilizing IDbContextFactory for safe short-lived context management.
/// </summary>
public sealed class ProjectRepository : IProjectRepository
{
    private readonly IDbContextFactory<DevDeskDbContext> _contextFactory;

    public ProjectRepository(IDbContextFactory<DevDeskDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<DeveloperProject>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var projects = await context.DeveloperProjects
            .AsNoTracking()
            .OrderByDescending(p => p.LastOpenedAt ?? p.CreatedAt)
            .ToListAsync(cancellationToken);

        return projects;
    }

    public async Task<DeveloperProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.DeveloperProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<DeveloperProject?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = PathHelper.NormalizePath(path);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.DeveloperProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Path == normalizedPath, cancellationToken);
    }

    public async Task AddAsync(DeveloperProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Path = PathHelper.NormalizePath(project.Path);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.DeveloperProjects.AddAsync(project, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(DeveloperProject project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Path = PathHelper.NormalizePath(project.Path);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.DeveloperProjects.Update(project);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.DeveloperProjects.FindAsync([id], cancellationToken);
        if (entity is not null)
        {
            context.DeveloperProjects.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
