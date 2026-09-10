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

public sealed class ProjectDetectionTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly ProjectDetectionService _detectionService;
    private readonly string _testDbPath;
    private readonly TestDbContextFactory _contextFactory;
    private readonly ProjectRepository _repository;
    private readonly ProjectService _projectService;

    public ProjectDetectionTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "DevDesk_DetectionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBaseDir);

        _detectionService = new ProjectDetectionService(
            new IProjectDetector[]
            {
                new NodeProjectDetector(NullLogger<NodeProjectDetector>.Instance),
                new DotNetProjectDetector(NullLogger<DotNetProjectDetector>.Instance),
                new PythonProjectDetector(NullLogger<PythonProjectDetector>.Instance)
            },
            NullLogger<ProjectDetectionService>.Instance);

        _testDbPath = Path.Combine(_testBaseDir, "test_devdesk.db");
        var pathProvider = new DatabasePathProvider(_testDbPath);
        var options = new DbContextOptionsBuilder<DevDeskDbContext>()
            .UseSqlite(pathProvider.GetConnectionString())
            .Options;

        _contextFactory = new TestDbContextFactory(options);
        using var ctx = _contextFactory.CreateDbContext();
        ctx.Database.Migrate();

        _repository = new ProjectRepository(_contextFactory);
        _projectService = new ProjectService(_repository, _detectionService, NullLogger<ProjectService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testBaseDir))
            {
                Directory.Delete(_testBaseDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task DetectAsync_ViteAndTypeScriptProject_RecognizesCorrectly()
    {
        var projDir = Path.Combine(_testBaseDir, "vite-ts-app");
        Directory.CreateDirectory(projDir);

        await File.WriteAllTextAsync(Path.Combine(projDir, "package.json"), """
        {
          "name": "vite-ts-app",
          "scripts": {
            "dev": "vite",
            "build": "tsc -b && vite build",
            "test": "vitest"
          },
          "dependencies": {
            "react": "^18.3.1"
          },
          "devDependencies": {
            "typescript": "^5.5.3",
            "vite": "^5.4.1"
          }
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(projDir, "tsconfig.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(projDir, "pnpm-lock.yaml"), "lockfileVersion: '9.0'");

        var result = await _detectionService.DetectAsync(projDir);

        Assert.True(result.IsRecognized);
        Assert.Equal("Vite", result.Framework);
        Assert.Equal("TypeScript", result.Language);
        Assert.Equal("pnpm", result.PackageManager);
        Assert.Equal("pnpm dev", result.SuggestedRunCommand);
        Assert.Equal("pnpm build", result.SuggestedBuildCommand);
        Assert.Equal("pnpm test", result.SuggestedTestCommand);
        Assert.Null(result.SuggestedDefaultPort);
    }

    [Fact]
    public async Task DetectAsync_NextJsTakesPrecedenceOverReactAndNode()
    {
        var projDir = Path.Combine(_testBaseDir, "next-app");
        Directory.CreateDirectory(projDir);

        await File.WriteAllTextAsync(Path.Combine(projDir, "package.json"), """
        {
          "name": "next-app",
          "scripts": {
            "dev": "next dev",
            "build": "next build",
            "start": "next start"
          },
          "dependencies": {
            "next": "14.2.5",
            "react": "^18.3.1",
            "react-dom": "^18.3.1"
          }
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(projDir, "package-lock.json"), "{}");

        var result = await _detectionService.DetectAsync(projDir);

        Assert.True(result.IsRecognized);
        Assert.Equal("Next.js", result.Framework);
        Assert.Equal("JavaScript", result.Language);
        Assert.Equal("npm", result.PackageManager);
        Assert.Equal("npm run dev", result.SuggestedRunCommand);
        Assert.Equal("npm run build", result.SuggestedBuildCommand);
    }

    [Fact]
    public async Task DetectAsync_PlainNodeProject_RecognizesNodeAndJavaScript()
    {
        var projDir = Path.Combine(_testBaseDir, "node-api");
        Directory.CreateDirectory(projDir);

        await File.WriteAllTextAsync(Path.Combine(projDir, "package.json"), """
        {
          "name": "node-api",
          "scripts": {
            "start": "node index.js"
          },
          "dependencies": {
            "express": "^4.19.2"
          }
        }
        """);

        var result = await _detectionService.DetectAsync(projDir);

        Assert.True(result.IsRecognized);
        Assert.Equal("Node.js", result.Framework);
        Assert.Equal("JavaScript", result.Language);
        Assert.Equal("npm", result.PackageManager); // fallback to npm without lockfile
        Assert.Equal("npm run start", result.SuggestedRunCommand);
        Assert.Null(result.SuggestedBuildCommand);
    }

    [Fact]
    public async Task DetectAsync_YarnAndBunLockfiles_DetectedProperly()
    {
        var yarnDir = Path.Combine(_testBaseDir, "yarn-proj");
        Directory.CreateDirectory(yarnDir);
        await File.WriteAllTextAsync(Path.Combine(yarnDir, "package.json"), """{"name":"yarn-proj","scripts":{"dev":"node server.js"}}""");
        await File.WriteAllTextAsync(Path.Combine(yarnDir, "yarn.lock"), "");

        var yarnResult = await _detectionService.DetectAsync(yarnDir);
        Assert.Equal("Yarn", yarnResult.PackageManager);
        Assert.Equal("yarn dev", yarnResult.SuggestedRunCommand);

        var bunDir = Path.Combine(_testBaseDir, "bun-proj");
        Directory.CreateDirectory(bunDir);
        await File.WriteAllTextAsync(Path.Combine(bunDir, "package.json"), """
        {
          "name": "bun-proj",
          "scripts": {
            "dev": "vite",
            "start": "vite preview",
            "build": "vite build",
            "test": "custom-runner"
          }
        }
        """);
        await File.WriteAllTextAsync(Path.Combine(bunDir, "bun.lockb"), "");

        var bunResult = await _detectionService.DetectAsync(bunDir);
        Assert.Equal("Bun", bunResult.PackageManager);
        Assert.Equal("bun run dev", bunResult.SuggestedRunCommand);
        Assert.Equal("bun run build", bunResult.SuggestedBuildCommand);
        Assert.Equal("bun run test", bunResult.SuggestedTestCommand); // MUST NOT be "bun test"!
    }

    [Fact]
    public async Task DetectAsync_DotNetSolutionAndProject_RecognizesConservativeCommands()
    {
        var projDir = Path.Combine(_testBaseDir, "DotNetApp");
        Directory.CreateDirectory(projDir);

        await File.WriteAllTextAsync(Path.Combine(projDir, "DotNetApp.sln"), "");
        await File.WriteAllTextAsync(Path.Combine(projDir, "DotNetApp.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
          </ItemGroup>
        </Project>
        """);

        var result = await _detectionService.DetectAsync(projDir);

        Assert.True(result.IsRecognized);
        Assert.Equal(".NET 10.0", result.Framework);
        Assert.Equal("C#", result.Language);
        Assert.Equal("NuGet", result.PackageManager);
        Assert.Equal("dotnet run", result.SuggestedRunCommand);
        Assert.Equal("dotnet build", result.SuggestedBuildCommand);
        Assert.Equal("dotnet test", result.SuggestedTestCommand);
    }

    [Fact]
    public async Task DetectAsync_DotNetClassLibrary_DoesNotSuggestRun()
    {
        var projDir = Path.Combine(_testBaseDir, "DotNetLib");
        Directory.CreateDirectory(projDir);

        await File.WriteAllTextAsync(Path.Combine(projDir, "DotNetLib.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net9.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """);

        var result = await _detectionService.DetectAsync(projDir);

        Assert.True(result.IsRecognized);
        Assert.Equal(".NET 9.0", result.Framework);
        Assert.Equal("C#", result.Language);
        Assert.Equal("dotnet build", result.SuggestedBuildCommand);
        Assert.Null(result.SuggestedRunCommand); // Conservative: library is not runnable
        Assert.Null(result.SuggestedTestCommand); // No test sdk
    }

    [Fact]
    public async Task DetectAsync_PythonPoetryAndUv_RecognizedProperly()
    {
        var poetryDir = Path.Combine(_testBaseDir, "py-poetry");
        Directory.CreateDirectory(poetryDir);
        await File.WriteAllTextAsync(Path.Combine(poetryDir, "pyproject.toml"), """
        [tool.poetry]
        name = "poetry-app"
        dependencies = { python = "^3.11", fastapi = "^0.111.0" }
        """);

        var poetryResult = await _detectionService.DetectAsync(poetryDir);
        Assert.True(poetryResult.IsRecognized);
        Assert.Equal("FastAPI", poetryResult.Framework);
        Assert.Equal("Python", poetryResult.Language);
        Assert.Equal("Poetry", poetryResult.PackageManager);

        var uvDir = Path.Combine(_testBaseDir, "py-uv");
        Directory.CreateDirectory(uvDir);
        await File.WriteAllTextAsync(Path.Combine(uvDir, "pyproject.toml"), "[project]\nname = 'uv-app'");
        await File.WriteAllTextAsync(Path.Combine(uvDir, "uv.lock"), "version = 1");

        var uvResult = await _detectionService.DetectAsync(uvDir);
        Assert.True(uvResult.IsRecognized);
        Assert.Equal("Python", uvResult.Language);
        Assert.Equal("uv", uvResult.PackageManager);
    }

    [Fact]
    public async Task DetectAsync_GitRepositoryDetection_DetectsFolderFileAndParent()
    {
        // 1. Root .git directory
        var rootGitDir = Path.Combine(_testBaseDir, "root-git");
        Directory.CreateDirectory(Path.Combine(rootGitDir, ".git"));
        var rootResult = await _detectionService.DetectAsync(rootGitDir);
        Assert.True(rootResult.IsGitRepository);

        // 2. .git file (worktree/submodule)
        var worktreeDir = Path.Combine(_testBaseDir, "worktree-proj");
        Directory.CreateDirectory(worktreeDir);
        await File.WriteAllTextAsync(Path.Combine(worktreeDir, ".git"), "gitdir: ../.git/worktrees/sub");
        var worktreeResult = await _detectionService.DetectAsync(worktreeDir);
        Assert.True(worktreeResult.IsGitRepository);

        // 3. Parent ancestor detection (up to 32 levels)
        var parentRepo = Path.Combine(_testBaseDir, "parent-repo");
        Directory.CreateDirectory(Path.Combine(parentRepo, ".git"));
        var nestedChild = Path.Combine(parentRepo, "src", "client", "app");
        Directory.CreateDirectory(nestedChild);
        var nestedResult = await _detectionService.DetectAsync(nestedChild);
        Assert.True(nestedResult.IsGitRepository);
    }

    [Fact]
    public async Task DetectAsync_UnknownFolder_ReturnsSafeUnrecognizedResult()
    {
        var unknownDir = Path.Combine(_testBaseDir, "empty-folder");
        Directory.CreateDirectory(unknownDir);

        var result = await _detectionService.DetectAsync(unknownDir);

        Assert.False(result.IsRecognized); // Empty folder is not recognized as a framework
        Assert.Null(result.Framework);
        Assert.Null(result.Language);
        Assert.Null(result.PackageManager);
        Assert.False(result.IsGitRepository);
    }

    [Fact]
    public async Task DetectAsync_GitOnlyUnknownFolder_IsRecognizedIsFalse()
    {
        // User correction 2: ProjectDetectionResult.IsRecognized must NOT become true only because IsGitRepository is true
        var gitOnlyDir = Path.Combine(_testBaseDir, "git-only-empty");
        Directory.CreateDirectory(Path.Combine(gitOnlyDir, ".git"));

        var result = await _detectionService.DetectAsync(gitOnlyDir);

        Assert.True(result.IsGitRepository);
        Assert.False(result.IsRecognized); // Still unsupported/unknown project type!
    }

    [Fact]
    public async Task DetectAsync_MalformedPackageJson_DoesNotCrash()
    {
        var malformedDir = Path.Combine(_testBaseDir, "malformed-json");
        Directory.CreateDirectory(malformedDir);
        await File.WriteAllTextAsync(Path.Combine(malformedDir, "package.json"), "{ invalid json: true, ");

        var result = await _detectionService.DetectAsync(malformedDir);

        Assert.NotNull(result);
        Assert.False(result.IsRecognized);
    }

    [Fact]
    public async Task DetectAndApplyAsync_PersistsDetectedMetadataAndPreservesUserCommands()
    {
        var projDir = Path.Combine(_testBaseDir, "persisted-app");
        Directory.CreateDirectory(projDir);

        await File.WriteAllTextAsync(Path.Combine(projDir, "package.json"), """
        {
          "name": "persisted-app",
          "scripts": {
            "dev": "vite",
            "build": "vite build",
            "test": "vitest"
          },
          "dependencies": {
            "vite": "^5.0.0"
          }
        }
        """);

        var initialProject = new DeveloperProject
        {
            Name = "Persisted App",
            Path = projDir,
            RunCommand = "npm run custom-dev", // Manually configured command
            DefaultPort = 8080 // Manually configured port
        };

        var added = await _projectService.AddProjectAsync(initialProject);
        // AddProjectAsync will have preserved custom-dev and port

        var detectionResult = await _projectService.DetectAndApplyAsync(added.Id);

        Assert.True(detectionResult.IsRecognized);
        Assert.Equal("Vite", detectionResult.Framework);

        var updated = await _projectService.GetProjectByIdAsync(added.Id);
        Assert.NotNull(updated);
        Assert.Equal("Vite", updated.Framework);
        Assert.Equal("JavaScript", updated.Language);
        Assert.Equal("npm", updated.PackageManager);
        Assert.Equal("npm run custom-dev", updated.RunCommand); // MUST NOT be overwritten!
        Assert.Equal("npm run build", updated.BuildCommand); // Was empty, populated with suggestion
        Assert.Equal("npm test", updated.TestCommand); // Was empty, populated with suggestion
        Assert.Equal(8080, updated.DefaultPort); // MUST NOT be overwritten!
    }

    [Fact]
    public async Task DetectAsync_WinExeAndSlnx_RecognizesRunnableDesktopProject()
    {
        var projDir = Path.Combine(_testBaseDir, "WpfDesktopApp");
        Directory.CreateDirectory(projDir);

        // Modern .slnx solution marker alongside WinExe project
        await File.WriteAllTextAsync(Path.Combine(projDir, "App.slnx"), "<Solution />");
        await File.WriteAllTextAsync(Path.Combine(projDir, "WpfDesktopApp.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows</TargetFramework>
            <UseWPF>true</UseWPF>
          </PropertyGroup>
        </Project>
        """);

        var result = await _detectionService.DetectAsync(projDir);

        Assert.True(result.IsRecognized);
        Assert.Equal(".NET 10.0-windows", result.Framework);
        Assert.Equal("C#", result.Language);
        Assert.Equal("NuGet", result.PackageManager);
        Assert.Equal("dotnet run", result.SuggestedRunCommand); // WinExe is safely recognized as runnable!
        Assert.Equal("dotnet build", result.SuggestedBuildCommand);
        Assert.Null(result.SuggestedTestCommand);
    }

    [Fact]
    public async Task DetectAsync_MalformedDetectorInput_DoesNotCrashOrchestration()
    {
        var brokenDir = Path.Combine(_testBaseDir, "broken-detector-input");
        Directory.CreateDirectory(brokenDir);

        // Corrupt XML that cannot be parsed
        await File.WriteAllTextAsync(Path.Combine(brokenDir, "Broken.csproj"), "<?xml><Invalid >>> <<<<<<");

        var result = await _detectionService.DetectAsync(brokenDir);

        Assert.NotNull(result);
        Assert.False(result.IsRecognized);
    }

    [Fact]
    public async Task DetectAsync_CancellationPropagates_ThrowsOperationCanceledException()
    {
        var projDir = Path.Combine(_testBaseDir, "canceled-app");
        Directory.CreateDirectory(projDir);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-canceled token

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _detectionService.DetectAsync(projDir, cts.Token);
        });
    }

    [Fact]
    public async Task DetectAsync_FaultingDetector_IsIsolatedAndDoesNotCrashOrchestration()
    {
        var projDir = Path.Combine(_testBaseDir, "fault-isolation-app");
        Directory.CreateDirectory(projDir);
        await File.WriteAllTextAsync(Path.Combine(projDir, "package.json"), """{"name":"fault-app","dependencies":{"react":"18.0.0"}}""");

        // Service with a faulting detector followed by NodeProjectDetector
        var serviceWithFaulty = new ProjectDetectionService(
            new IProjectDetector[]
            {
                new BrokenMockDetector(),
                new NodeProjectDetector(NullLogger<NodeProjectDetector>.Instance)
            },
            NullLogger<ProjectDetectionService>.Instance);

        var result = await serviceWithFaulty.DetectAsync(projDir);

        Assert.NotNull(result);
        Assert.True(result.IsRecognized);
        Assert.Equal("React", result.Framework);
    }

    [Fact]
    public async Task DetectAsync_RootSlnReferencingNestedWinExe_GeneratesRunCommandWithProject()
    {
        var projDir = Path.Combine(_testBaseDir, "NestedWinExeSolution");
        Directory.CreateDirectory(projDir);

        var nestedAppDir = Path.Combine(projDir, "src", "DesktopApp");
        Directory.CreateDirectory(nestedAppDir);

        var slnContent = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "DesktopApp", "src\DesktopApp\DesktopApp.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        """;
        await File.WriteAllTextAsync(Path.Combine(projDir, "App.sln"), slnContent);

        var csprojContent = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows</TargetFramework>
            <UseWPF>true</UseWPF>
          </PropertyGroup>
        </Project>
        """;
        await File.WriteAllTextAsync(Path.Combine(nestedAppDir, "DesktopApp.csproj"), csprojContent);

        var result = await _detectionService.DetectAsync(projDir);

        Assert.NotNull(result);
        Assert.True(result.IsRecognized);
        Assert.Equal(".NET 10.0-windows", result.Framework);
        Assert.Equal("C#", result.Language);
        Assert.Equal("NuGet", result.PackageManager);
        var expectedRelativePath = Path.Combine("src", "DesktopApp", "DesktopApp.csproj");
        Assert.Equal($"dotnet run --project \"{expectedRelativePath}\"", result.SuggestedRunCommand);
        Assert.Equal("dotnet build", result.SuggestedBuildCommand);
    }

    [Fact]
    public async Task DetectAsync_SlnWithMultipleRunnableProjects_DoesNotGuess()
    {
        var projDir = Path.Combine(_testBaseDir, "MultipleRunnableSolution");
        Directory.CreateDirectory(projDir);

        var app1Dir = Path.Combine(projDir, "src", "AppOne");
        Directory.CreateDirectory(app1Dir);
        var app2Dir = Path.Combine(projDir, "src", "AppTwo");
        Directory.CreateDirectory(app2Dir);

        var slnContent = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AppOne", "src\AppOne\AppOne.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AppTwo", "src\AppTwo\AppTwo.csproj", "{22222222-2222-2222-2222-222222222222}"
        EndProject
        """;
        await File.WriteAllTextAsync(Path.Combine(projDir, "Multi.sln"), slnContent);

        await File.WriteAllTextAsync(Path.Combine(app1Dir, "AppOne.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows</TargetFramework>
          </PropertyGroup>
        </Project>
        """);

        await File.WriteAllTextAsync(Path.Combine(app2Dir, "AppTwo.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """);

        var result = await _detectionService.DetectAsync(projDir);

        Assert.NotNull(result);
        Assert.True(result.IsRecognized);
        Assert.Null(result.SuggestedRunCommand); // Multiple runnable projects -> DO NOT guess!
        Assert.Equal("dotnet build", result.SuggestedBuildCommand);
    }

    [Fact]
    public async Task DetectAsync_SlnWithMalformedOrInvalidReferencedPath_DoesNotCrash()
    {
        var projDir = Path.Combine(_testBaseDir, "MalformedSlnSolution");
        Directory.CreateDirectory(projDir);

        var validAppDir = Path.Combine(projDir, "src", "ValidApp");
        Directory.CreateDirectory(validAppDir);

        var slnContent = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ValidApp", "src\ValidApp\ValidApp.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "OutsideRoot", "..\..\outside\Secret.csproj", "{22222222-2222-2222-2222-222222222222}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "NonExistent", "src\Missing\DoesNotExist.csproj", "{33333333-3333-3333-3333-333333333333}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "InvalidChars", "src\???|*<>\bad.csproj", "{44444444-4444-4444-4444-444444444444}"
        EndProject
        """;
        await File.WriteAllTextAsync(Path.Combine(projDir, "Broken.sln"), slnContent);

        await File.WriteAllTextAsync(Path.Combine(validAppDir, "ValidApp.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows</TargetFramework>
          </PropertyGroup>
        </Project>
        """);

        var result = await _detectionService.DetectAsync(projDir);

        Assert.NotNull(result);
        Assert.True(result.IsRecognized);
        var expectedRelativePath = Path.Combine("src", "ValidApp", "ValidApp.csproj");
        Assert.Equal($"dotnet run --project \"{expectedRelativePath}\"", result.SuggestedRunCommand);
        Assert.Equal("dotnet build", result.SuggestedBuildCommand);
    }

    [Fact]
    public async Task DetectAndApplyAsync_DotNetProject_PreservesManuallyConfiguredRunCommand()
    {
        var projDir = Path.Combine(_testBaseDir, "DotNetCustomCommand");
        Directory.CreateDirectory(projDir);

        var nestedAppDir = Path.Combine(projDir, "src", "MyApp");
        Directory.CreateDirectory(nestedAppDir);

        await File.WriteAllTextAsync(Path.Combine(projDir, "MyApp.sln"), """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MyApp", "src\MyApp\MyApp.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        """);

        await File.WriteAllTextAsync(Path.Combine(nestedAppDir, "MyApp.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0-windows</TargetFramework>
          </PropertyGroup>
        </Project>
        """);

        var initialProject = new DeveloperProject
        {
            Name = "Custom DotNet Project",
            Path = projDir,
            RunCommand = "dotnet run --custom-flag --port 5000",
            BuildCommand = "dotnet build -c Release"
        };

        var added = await _projectService.AddProjectAsync(initialProject);

        var detectionResult = await _projectService.DetectAndApplyAsync(added.Id);

        Assert.True(detectionResult.IsRecognized);
        Assert.Equal(".NET 10.0-windows", detectionResult.Framework);

        var updated = await _projectService.GetProjectByIdAsync(added.Id);
        Assert.NotNull(updated);
        // Both RunCommand and BuildCommand must remain strictly preserved!
        Assert.Equal("dotnet run --custom-flag --port 5000", updated.RunCommand);
        Assert.Equal("dotnet build -c Release", updated.BuildCommand);
    }

    [Fact]
    public async Task DetectAsync_DevDeskRepositoryRoot_DetectsCorrectParameters()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        if (File.Exists(Path.Combine(repoRoot, "DevDesk.sln")))
        {
            var result = await _detectionService.DetectAsync(repoRoot);
            Assert.NotNull(result);
            Assert.True(result.IsRecognized);
            Assert.Equal("C#", result.Language);
            Assert.Equal("NuGet", result.PackageManager);
            Assert.Equal("dotnet build", result.SuggestedBuildCommand);
            Assert.Equal(@"dotnet run --project ""src\DevDesk.App\DevDesk.App.csproj""", result.SuggestedRunCommand);
            Assert.Equal("dotnet test", result.SuggestedTestCommand);
        }
    }

    private sealed class BrokenMockDetector : IProjectDetector
    {
        public int Order => 0; // Runs first and throws

        public Task<ProjectDetectionResult?> DetectAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            throw new IOException("Simulated disk read crash");
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<DevDeskDbContext>
    {
        private readonly DbContextOptions<DevDeskDbContext> _options;

        public TestDbContextFactory(DbContextOptions<DevDeskDbContext> options)
        {
            _options = options;
        }

        public DevDeskDbContext CreateDbContext()
        {
            return new DevDeskDbContext(_options);
        }
    }
}
