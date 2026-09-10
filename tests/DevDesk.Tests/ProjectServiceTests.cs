using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.Core.Detection;
using DevDesk.Core.Models;
using DevDesk.Infrastructure.Detection;
using DevDesk.Infrastructure.Detection.Detectors;
using DevDesk.Infrastructure.Persistence;
using DevDesk.Infrastructure.Persistence.Database;
using DevDesk.Infrastructure.Persistence.Repositories;
using DevDesk.Infrastructure.Services;

namespace DevDesk.Tests;

public sealed class ProjectServiceTests : IDisposable
{
    private readonly string _testBaseDirectory;
    private readonly string _testDbPath;
    private readonly IDbContextFactory<DevDeskDbContext> _contextFactory;
    private readonly ProjectRepository _repository;
    private readonly ProjectService _service;

    public ProjectServiceTests()
    {
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "DevDesk_ServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBaseDirectory);
        _testDbPath = Path.Combine(_testBaseDirectory, "test_devdesk.db");

        var pathProvider = new DatabasePathProvider(_testDbPath);
        var options = new DbContextOptionsBuilder<DevDeskDbContext>()
            .UseSqlite(pathProvider.GetConnectionString())
            .Options;

        _contextFactory = new TestDbContextFactory(options);

        // Apply migrations to test DB
        using var context = _contextFactory.CreateDbContext();
        context.Database.Migrate();

        _repository = new ProjectRepository(_contextFactory);
        var detectionService = new ProjectDetectionService(
            new IProjectDetector[]
            {
                new NodeProjectDetector(NullLogger<NodeProjectDetector>.Instance),
                new DotNetProjectDetector(NullLogger<DotNetProjectDetector>.Instance),
                new PythonProjectDetector(NullLogger<PythonProjectDetector>.Instance)
            },
            NullLogger<ProjectDetectionService>.Instance);
        _service = new ProjectService(_repository, detectionService, NullLogger<ProjectService>.Instance);
    }

    [Fact]
    public async Task AddProjectAsync_WithValidProject_SucceedsAndPersists()
    {
        // Arrange
        var projectDir = Path.Combine(_testBaseDirectory, "PortfolioApp");
        Directory.CreateDirectory(projectDir);

        var project = new DeveloperProject
        {
            Name = "Portfolio App",
            Path = projectDir,
            Framework = "Vite",
            Language = "TypeScript",
            PackageManager = "pnpm",
            RunCommand = "pnpm dev",
            BuildCommand = "pnpm build",
            TestCommand = "pnpm test",
            DefaultPort = 5173
        };

        // Act
        var result = await _service.AddProjectAsync(project);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Portfolio App", result.Name);

        var retrieved = await _service.GetProjectByIdAsync(result.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Portfolio App", retrieved.Name);
        Assert.Equal(5173, retrieved.DefaultPort);
        Assert.Equal("pnpm dev", retrieved.RunCommand);
    }

    [Fact]
    public async Task AddProjectAsync_WithNonExistentDirectory_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testBaseDirectory, "DoesNotExistFolder_" + Guid.NewGuid().ToString("N"));
        var project = new DeveloperProject
        {
            Name = "Ghost Project",
            Path = nonExistentPath
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _service.AddProjectAsync(project));
        Assert.Equal("The selected project folder could not be found.", ex.Message);
    }

    [Fact]
    public async Task AddProjectAsync_WithDuplicateNormalizedPath_ThrowsInvalidOperationException()
    {
        // Arrange
        var projectDir = Path.Combine(_testBaseDirectory, "DuplicateWorkspace");
        Directory.CreateDirectory(projectDir);

        var project1 = new DeveloperProject
        {
            Name = "Workspace 1",
            Path = projectDir
        };
        await _service.AddProjectAsync(project1);

        // Path with alternate separators/casing
        var project2 = new DeveloperProject
        {
            Name = "Workspace 2",
            Path = projectDir.ToLowerInvariant() + @"\"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddProjectAsync(project2));
        Assert.Equal("This project is already registered in DevDesk.", ex.Message);
    }

    [Fact]
    public async Task AddProjectAsync_WithInvalidPort_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var projectDir = Path.Combine(_testBaseDirectory, "InvalidPortApp");
        Directory.CreateDirectory(projectDir);

        var project = new DeveloperProject
        {
            Name = "Invalid Port App",
            Path = projectDir,
            DefaultPort = 70000 // Invalid (> 65535)
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.AddProjectAsync(project));
        Assert.Contains("Default port must be between 1 and 65535.", ex.Message);
    }

    [Fact]
    public async Task AddProjectAsync_WithEmptyName_ThrowsArgumentException()
    {
        // Arrange
        var projectDir = Path.Combine(_testBaseDirectory, "EmptyNameApp");
        Directory.CreateDirectory(projectDir);

        var project = new DeveloperProject
        {
            Name = "   ",
            Path = projectDir
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.AddProjectAsync(project));
    }

    [Fact]
    public async Task UpdateProjectAsync_WithValidChanges_UpdatesDatabase()
    {
        // Arrange
        var projectDir = Path.Combine(_testBaseDirectory, "UpdateTestApp");
        Directory.CreateDirectory(projectDir);

        var project = new DeveloperProject
        {
            Name = "Initial App",
            Path = projectDir,
            DefaultPort = 3000
        };
        await _service.AddProjectAsync(project);

        // Act
        project.Name = "Updated App Name";
        project.RunCommand = "npm start";
        project.BuildCommand = "npm run build";
        project.DefaultPort = 8080;
        await _service.UpdateProjectAsync(project);

        // Assert
        var updated = await _service.GetProjectByIdAsync(project.Id);
        Assert.NotNull(updated);
        Assert.Equal("Updated App Name", updated.Name);
        Assert.Equal("npm start", updated.RunCommand);
        Assert.Equal(8080, updated.DefaultPort);
    }

    [Fact]
    public async Task UpdateProjectAsync_WithDuplicatePath_ThrowsInvalidOperationException()
    {
        // Arrange
        var dir1 = Path.Combine(_testBaseDirectory, "ProjectOne");
        var dir2 = Path.Combine(_testBaseDirectory, "ProjectTwo");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        var project1 = new DeveloperProject { Name = "P1", Path = dir1 };
        var project2 = new DeveloperProject { Name = "P2", Path = dir2 };
        await _service.AddProjectAsync(project1);
        await _service.AddProjectAsync(project2);

        // Act - Try updating project2's path to point to project1's folder
        project2.Path = dir1;

        // Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateProjectAsync(project2));
        Assert.Equal("This project is already registered in DevDesk.", ex.Message);
    }

    [Fact]
    public async Task RemoveProjectAsync_RemovesDatabaseRecord_LeavesPhysicalDirectoryIntact()
    {
        // Arrange
        var projectDir = Path.Combine(_testBaseDirectory, "DeleteSafetyProject");
        Directory.CreateDirectory(projectDir);
        var sourceFile = Path.Combine(projectDir, "package.json");
        await File.WriteAllTextAsync(sourceFile, "{\"name\": \"delete-safety-project\"}");

        var project = new DeveloperProject
        {
            Name = "Safety Test Project",
            Path = projectDir
        };
        await _service.AddProjectAsync(project);

        // Verify it was added
        var added = await _service.GetProjectByIdAsync(project.Id);
        Assert.NotNull(added);

        // Act - Remove from DevDesk
        await _service.RemoveProjectAsync(project.Id);

        // Assert - DB record removed
        var afterRemoval = await _service.GetProjectByIdAsync(project.Id);
        Assert.Null(afterRemoval);

        // Assert - Physical directory and file remain intact on disk!
        Assert.True(Directory.Exists(projectDir), "Physical directory must not be deleted.");
        Assert.True(File.Exists(sourceFile), "Physical source files must not be deleted.");
    }

    [Fact]
    public async Task GetProjectsAsync_ReturnsAllProjects()
    {
        // Arrange
        var dirA = Path.Combine(_testBaseDirectory, "ProjA");
        var dirB = Path.Combine(_testBaseDirectory, "ProjB");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        await _service.AddProjectAsync(new DeveloperProject { Name = "Proj A", Path = dirA });
        await _service.AddProjectAsync(new DeveloperProject { Name = "Proj B", Path = dirB });

        // Act
        var list = await _service.GetProjectsAsync();

        // Assert
        Assert.Equal(2, list.Count);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_testBaseDirectory))
            {
                Directory.Delete(_testBaseDirectory, recursive: true);
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
