using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.Core.Models;
using DevDesk.Infrastructure.Persistence;
using DevDesk.Infrastructure.Persistence.Database;
using DevDesk.Infrastructure.Persistence.Repositories;

namespace DevDesk.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _testDbDirectory;
    private readonly string _testDbPath;
    private readonly IDbContextFactory<DevDeskDbContext> _contextFactory;
    private readonly DatabasePathProvider _pathProvider;

    public PersistenceTests()
    {
        _testDbDirectory = Path.Combine(Path.GetTempPath(), "DevDesk_Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDirectory);
        _testDbPath = Path.Combine(_testDbDirectory, "test_devdesk.db");

        _pathProvider = new DatabasePathProvider(_testDbPath);

        var options = new DbContextOptionsBuilder<DevDeskDbContext>()
            .UseSqlite(_pathProvider.GetConnectionString())
            .Options;

        _contextFactory = new TestDbContextFactory(options);

        // Apply migrations to the test database
        using var context = _contextFactory.CreateDbContext();
        context.Database.Migrate();
    }

    [Fact]
    public async Task DeveloperProject_CanBeAddedAndRetrieved_Successfully()
    {
        // Arrange
        var repository = new ProjectRepository(_contextFactory);
        var project = new DeveloperProject
        {
            Name = "Portfolio",
            Path = @"C:\Projects\Portfolio",
            Framework = "Vite",
            Language = "TypeScript",
            PackageManager = "pnpm",
            RunCommand = "pnpm dev",
            BuildCommand = "pnpm build",
            TestCommand = "pnpm test",
            DefaultPort = 5173,
            IsGitRepository = true
        };

        // Act
        await repository.AddAsync(project);
        var retrieved = await repository.GetByIdAsync(project.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Portfolio", retrieved.Name);
        Assert.Equal(@"C:\Projects\Portfolio", retrieved.Path);
        Assert.Equal("Vite", retrieved.Framework);
        Assert.Equal(5173, retrieved.DefaultPort);
        Assert.True(retrieved.IsGitRepository);
    }

    [Fact]
    public async Task DeveloperProject_Enforces_UniquePathConstraint()
    {
        // Arrange
        var repository = new ProjectRepository(_contextFactory);
        var project1 = new DeveloperProject
        {
            Name = "Project Alpha",
            Path = @"C:\Dev\Workspaces\Alpha"
        };
        var project2 = new DeveloperProject
        {
            Name = "Project Alpha Duplicate",
            Path = @"c:\dev\workspaces\alpha\" // Same path, different casing and trailing slash
        };

        await repository.AddAsync(project1);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => repository.AddAsync(project2));
    }

    [Fact]
    public async Task DeveloperProject_Enforces_DefaultPortCheckConstraint()
    {
        // Arrange
        var repository = new ProjectRepository(_contextFactory);
        var invalidProject = new DeveloperProject
        {
            Name = "Invalid Port Project",
            Path = @"C:\Dev\InvalidPort",
            DefaultPort = 70000 // Out of valid port range (1-65535)
        };

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => repository.AddAsync(invalidProject));
    }

    [Fact]
    public async Task ProjectRepository_GetByPathAsync_NormalizesAndFindsProject()
    {
        // Arrange
        var repository = new ProjectRepository(_contextFactory);
        var project = new DeveloperProject
        {
            Name = "API Server",
            Path = @"C:\Dev\Services\ApiServer"
        };
        await repository.AddAsync(project);

        // Act - Query with alternate separator and casing
        var found = await repository.GetByPathAsync(@"c:/dev/services/apiserver/");

        // Assert
        Assert.NotNull(found);
        Assert.Equal(project.Id, found.Id);
        Assert.Equal("API Server", found.Name);
    }

    [Fact]
    public async Task ProjectRepository_UpdateAsync_PersistsPropertyChanges()
    {
        // Arrange
        var repository = new ProjectRepository(_contextFactory);
        var project = new DeveloperProject
        {
            Name = "Initial Name",
            Path = @"C:\Dev\SampleProject",
            Framework = "Next.js"
        };
        await repository.AddAsync(project);

        // Act
        project.Name = "Updated Name";
        project.Framework = "Remix";
        project.DefaultPort = 3000;
        await repository.UpdateAsync(project);

        var updated = await repository.GetByIdAsync(project.Id);

        // Assert
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("Remix", updated.Framework);
        Assert.Equal(3000, updated.DefaultPort);
    }

    [Fact]
    public async Task ProjectRepository_DeleteAsync_CascadesSavedCommands()
    {
        // Arrange
        var repository = new ProjectRepository(_contextFactory);
        var project = new DeveloperProject
        {
            Name = "Command Host Project",
            Path = @"C:\Dev\CommandHost"
        };
        await repository.AddAsync(project);

        using (var context = _contextFactory.CreateDbContext())
        {
            var projectCommand = new SavedCommand
            {
                ProjectId = project.Id,
                Name = "Project Task",
                Executable = "dotnet",
                Arguments = new[] { "run" },
                WorkingDirectory = project.Path
            };

            var globalCommand = new SavedCommand
            {
                ProjectId = null,
                Name = "Global Task",
                Executable = "git",
                Arguments = new[] { "status" },
                WorkingDirectory = @"C:\"
            };

            await context.SavedCommands.AddRangeAsync(projectCommand, globalCommand);
            await context.SaveChangesAsync();
        }

        // Act
        await repository.DeleteAsync(project.Id);

        // Assert
        var deletedProject = await repository.GetByIdAsync(project.Id);
        Assert.Null(deletedProject);

        using (var context = _contextFactory.CreateDbContext())
        {
            var remainingCommands = await context.SavedCommands.ToListAsync();
            Assert.Single(remainingCommands);
            Assert.Equal("Global Task", remainingCommands[0].Name);
            Assert.Null(remainingCommands[0].ProjectId);
        }
    }

    [Fact]
    public async Task AppSetting_Enforces_UniqueKeyConstraint()
    {
        // Arrange
        using var context = _contextFactory.CreateDbContext();
        var setting1 = new AppSetting { Key = "Theme", Value = "Dark" };
        var setting2 = new AppSetting { Key = "theme", Value = "Light" }; // Case-insensitive duplicate

        await context.AppSettings.AddAsync(setting1);
        await context.SaveChangesAsync();

        await context.AppSettings.AddAsync(setting2);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DatabaseInitializer_AppliesMigrations_ToFreshDatabase()
    {
        // Arrange
        var freshDbPath = Path.Combine(_testDbDirectory, "fresh_init.db");
        var freshPathProvider = new DatabasePathProvider(freshDbPath);
        var freshOptions = new DbContextOptionsBuilder<DevDeskDbContext>()
            .UseSqlite(freshPathProvider.GetConnectionString())
            .Options;
        var freshFactory = new TestDbContextFactory(freshOptions);

        var initializer = new DatabaseInitializer(
            freshFactory,
            freshPathProvider,
            NullLogger<DatabaseInitializer>.Instance);

        // Act
        await initializer.InitializeAsync();

        // Assert
        Assert.True(File.Exists(freshDbPath));
        using var context = freshFactory.CreateDbContext();
        var canConnect = await context.Database.CanConnectAsync();
        Assert.True(canConnect);

        // Verify tables exist by performing a query
        var projectCount = await context.DeveloperProjects.CountAsync();
        Assert.Equal(0, projectCount);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_testDbDirectory))
            {
                Directory.Delete(_testDbDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup in test temp folder
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<DevDeskDbContext>
    {
        private readonly DbContextOptions<DevDeskDbContext> _options;

        public TestDbContextFactory(DbContextOptions<DevDeskDbContext> options)
        {
            _options = options;
        }

        public DevDeskDbContext CreateDbContext() => new(_options);
    }
}
