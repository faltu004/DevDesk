using Microsoft.EntityFrameworkCore;
using DevDesk.Core.Models;

namespace DevDesk.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core database context for DevDesk.
/// </summary>
public sealed class DevDeskDbContext : DbContext
{
    public DevDeskDbContext(DbContextOptions<DevDeskDbContext> options)
        : base(options)
    {
    }

    public DbSet<DeveloperProject> DeveloperProjects => Set<DeveloperProject>();

    public DbSet<SavedCommand> SavedCommands => Set<SavedCommand>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DevDeskDbContext).Assembly);
    }
}
