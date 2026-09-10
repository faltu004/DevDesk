using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using DevDesk.Infrastructure.Persistence.Database;

namespace DevDesk.Infrastructure.Persistence;

/// <summary>
/// Design-time factory enabling Entity Framework CLI tooling to discover and configure DevDeskDbContext without running the WPF application host.
/// </summary>
public sealed class DevDeskDbContextFactory : IDesignTimeDbContextFactory<DevDeskDbContext>
{
    public DevDeskDbContext CreateDbContext(string[] args)
    {
        var pathProvider = new DatabasePathProvider();
        var optionsBuilder = new DbContextOptionsBuilder<DevDeskDbContext>();
        optionsBuilder.UseSqlite(pathProvider.GetConnectionString());

        return new DevDeskDbContext(optionsBuilder.Options);
    }
}
