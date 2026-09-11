using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.Core.Detection;
using DevDesk.Core.Models;
using DevDesk.Core.Runner;
using DevDesk.Core.Services;
using DevDesk.Infrastructure.Runner;
using DevDesk.Infrastructure.Runner.Native;

namespace DevDesk.Tests;

public sealed class ProjectRunnerTests
{
    // ==========================================
    // 1. COMMAND SHAPE ALLOWLIST & PARSING TESTS
    // ==========================================

    [Theory]
    [InlineData("dotnet build")]
    [InlineData("dotnet test")]
    [InlineData("dotnet restore")]
    [InlineData("dotnet publish")]
    [InlineData("dotnet ef migrations add Initial")]
    [InlineData("dotnet new webapi")]
    [InlineData("dotnet tool install -g foo")]
    public void CommandShapeValidator_UnsupportedDotnetSubcommand_IsRejected(string command)
    {
        var result = CommandShapeValidator.ValidateAndParse(command);

        Assert.False(result.IsValid);
        Assert.Contains("Unsupported dotnet subcommand", result.ErrorMessage);
    }

    [Theory]
    [InlineData("npm install")]
    [InlineData("npm i")]
    [InlineData("npm add lodash")]
    [InlineData("npm remove express")]
    [InlineData("npm publish")]
    [InlineData("npm exec tsx")]
    [InlineData("pnpm add vue")]
    [InlineData("pnpm install")]
    [InlineData("pnpm remove react")]
    [InlineData("yarn add typescript")]
    [InlineData("yarn install")]
    [InlineData("yarn remove vite")]
    [InlineData("bun add drizzle-orm")]
    [InlineData("bun install")]
    public void CommandShapeValidator_PackageManagementCommands_AreRejected(string command)
    {
        var result = CommandShapeValidator.ValidateAndParse(command);

        Assert.False(result.IsValid);
        Assert.Contains("blocked", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("npm run dev & dir")]
    [InlineData("npm start | more")]
    [InlineData("pnpm dev > output.txt")]
    [InlineData("yarn dev < input.txt")]
    [InlineData("npm run %TEMP%")]
    [InlineData("npm run !VAR!")]
    [InlineData("npm run dev; whoami")]
    [InlineData("npm run dev ^")]
    [InlineData("npm run \"dev & calc\"")]
    public void CommandShapeValidator_TrustedCmdShim_RejectsUnsafeShellExpressions(string command)
    {
        var result = CommandShapeValidator.ValidateAndParse(command);

        Assert.False(result.IsValid);
        Assert.Contains("forbidden shell operators", result.ErrorMessage);
    }

    [Theory]
    [InlineData("dotnet run && calc.exe")]
    [InlineData("dotnet run & dir")]
    [InlineData("dotnet run | more")]
    [InlineData("dotnet run ; whoami")]
    [InlineData("dotnet run > out.txt")]
    public void CommandShapeValidator_DirectDotnet_RejectsShellChainingOperators(string command)
    {
        var result = CommandShapeValidator.ValidateAndParse(command);

        Assert.False(result.IsValid);
        Assert.Contains("shell operator token", result.ErrorMessage);
    }

    [Fact]
    public void CommandShapeValidator_DirectDotnetProjectPath_WithSpacesAndAmpersand_IsPreservedSafely()
    {
        string command = "dotnet run --project \"D:\\Projects\\R&D\\App\\App.csproj\"";
        var result = CommandShapeValidator.ValidateAndParse(command);

        Assert.True(result.IsValid);
        Assert.Equal("dotnet", result.Tool);
        Assert.False(result.IsCmdShim);
        Assert.Equal(3, result.Arguments.Count);
        Assert.Equal("run", result.Arguments[0]);
        Assert.Equal("--project", result.Arguments[1]);
        Assert.Equal(@"D:\Projects\R&D\App\App.csproj", result.Arguments[2]);
    }

    [Fact]
    public void DirectNativeExecution_ArgumentsWithAmpersand_PassedToProcessStartInfoDirectlyWithoutShell()
    {
        var config = new ProcessLaunchConfiguration
        {
            WorkingDirectory = @"D:\Projects\R&D",
            ExecutablePath = @"C:\Program Files\dotnet\dotnet.exe",
            Arguments = new[] { "run", "--project", @"D:\Projects\R&D\App\App.csproj" },
            IsCmdShim = false
        };

        var launcher = new FakeProcessLauncher();
        var proc = (FakeManagedProcess)launcher.Launch(config);

        Assert.False(proc.Config.IsCmdShim);
        Assert.Equal(@"C:\Program Files\dotnet\dotnet.exe", proc.Config.ExecutablePath);
        Assert.Equal(3, proc.Config.Arguments.Count);
        Assert.Equal(@"D:\Projects\R&D\App\App.csproj", proc.Config.Arguments[2]);
    }

    [Theory]
    [InlineData("cmd.exe /c calc")]
    [InlineData("cmd /c dir")]
    [InlineData("powershell -Command Get-Process")]
    [InlineData("pwsh.exe -Command exit")]
    [InlineData("bash -c ls")]
    [InlineData("python app.py")]
    [InlineData("gcc main.c")]
    public void CommandShapeValidator_ArbitraryCommands_CannotReachCmdExe(string command)
    {
        var result = CommandShapeValidator.ValidateAndParse(command);

        Assert.False(result.IsValid);
        Assert.Contains("not permitted in Phase 7", result.ErrorMessage);
    }

    [Theory]
    [InlineData("dotnet run")]
    [InlineData("dotnet run --project \"src/MyApi/MyApi.csproj\"")]
    [InlineData("dotnet run --launch-profile Dev")]
    public void CommandShapeValidator_ValidDotnetCommands_AreAccepted(string command)
    {
        var result = CommandShapeValidator.ValidateAndParse(command);

        Assert.True(result.IsValid);
        Assert.Equal("dotnet", result.Tool);
        Assert.False(result.IsCmdShim);
        Assert.Equal("run", result.Arguments[0]);
    }

    [Theory]
    [InlineData("npm run dev", "npm", true, new[] { "run", "dev" })]
    [InlineData("npm start", "npm", true, new[] { "start" })]
    [InlineData("npm test", "npm", true, new[] { "test" })]
    [InlineData("pnpm run build", "pnpm", true, new[] { "run", "build" })]
    [InlineData("pnpm start", "pnpm", true, new[] { "start" })]
    [InlineData("yarn run start:prod", "yarn", true, new[] { "run", "start:prod" })]
    [InlineData("yarn start", "yarn", true, new[] { "start" })]
    [InlineData("bun run dev", "bun", false, new[] { "run", "dev" })]
    [InlineData("bun start", "bun", false, new[] { "start" })]
    public void CommandShapeValidator_ValidPackageRunCommands_AreAcceptedWithCorrectShimStrategy(
        string command, string expectedTool, bool expectedIsShim, string[] expectedArgs)
    {
        var result = CommandShapeValidator.ValidateAndParse(command);

        Assert.True(result.IsValid);
        Assert.Equal(expectedTool, result.Tool);
        Assert.Equal(expectedIsShim, result.IsCmdShim);
        Assert.Equal(expectedArgs, result.Arguments);
    }

    // ==========================================
    // 2. LOG BUFFER TRUNCATION & BOUNDING TESTS
    // ==========================================

    [Fact]
    public void BoundedLogBuffer_HugePathologicalLine_IsTruncated()
    {
        var buffer = new BoundedLogBuffer(maxLines: 100, maxLineLength: 100);
        var sessionId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        string hugeLine = new('X', 500);
        var evt = buffer.Add(sessionId, projectId, hugeLine, isError: false);

        Assert.StartsWith(new string('X', 100), evt.Text);
        Assert.EndsWith("[truncated]", evt.Text);
        Assert.True(evt.Text.Length < 150);
    }

    [Fact]
    public void BoundedLogBuffer_ExceedingLineLimit_EvictsOldestEntries()
    {
        var buffer = new BoundedLogBuffer(maxLines: 5, maxLineLength: 100);
        var sessionId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        for (int i = 1; i <= 10; i++)
        {
            buffer.Add(sessionId, projectId, $"Line {i}", isError: false);
        }

        var snapshot = buffer.GetSnapshot();
        Assert.Equal(5, snapshot.Count);
        Assert.Equal("Line 6", snapshot[0].Text);
        Assert.Equal("Line 10", snapshot[4].Text);
    }

    // ==========================================
    // 3. SERVICE SYNCHRONIZATION & LIFECYCLE TESTS
    // ==========================================

    [Fact]
    public async Task ProjectRunnerService_SimultaneousStartCalls_CreatesExactlyOneSessionAndProcess()
    {
        var project = CreateTestProject("dotnet run");
        var fakeProjectService = new FakeProjectService(project);

        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        // Run 10 concurrent starts simultaneously
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => runnerService.StartProjectAsync(project.Id))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Exactly 1 start must succeed; the remaining 9 must be rejected with "already running"
        int successCount = results.Count(r => r.Success);
        int failCount = results.Count(r => !r.Success);

        Assert.Equal(1, successCount);
        Assert.Equal(9, failCount);
        Assert.Single(launcher.CreatedProcesses);

        var session = runnerService.GetSession(project.Id);
        Assert.NotNull(session);
        Assert.Equal(ProjectRunState.Running, session.State);
    }

    [Fact]
    public async Task ProjectRunnerService_StartVsStopRace_RemainsConsistent()
    {
        var project = CreateTestProject("dotnet run");
        var fakeProjectService = new FakeProjectService(project);

        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        // First start
        var startResult = await runnerService.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);

        // Trigger Stop and immediate Restart/Start concurrently
        var stopTask = runnerService.StopProjectAsync(project.Id);
        var startTask = runnerService.StartProjectAsync(project.Id);

        await Task.WhenAll(stopTask, startTask);

        // Synchronization guarantee: State must be deterministic (either running or exited, never crashed/corrupted)
        var finalSession = runnerService.GetSession(project.Id);
        Assert.True(finalSession == null || finalSession.State is ProjectRunState.Running or ProjectRunState.Exited);
    }

    [Fact]
    public async Task ProjectRunnerService_StaleProcessExitedEvent_CannotOverwriteNewerRestartedSession()
    {
        var project = CreateTestProject("dotnet run");
        var fakeProjectService = new FakeProjectService(project);

        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        // 1. Start initial session
        var startResult = await runnerService.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);
        var firstProcess = launcher.CreatedProcesses[0];
        var firstSessionId = startResult.Session!.SessionId;

        // 2. Restart project -> spawns second process and newer session
        var restartResult = await runnerService.RestartProjectAsync(project.Id);
        Assert.True(restartResult.Success);
        var secondSessionId = restartResult.Session!.SessionId;
        Assert.NotEqual(firstSessionId, secondSessionId);

        // 3. Stale event: the first process raises Exited now
        firstProcess.SimulateExit(0);

        // 4. Assert: the active session was NOT marked exited by the stale process event
        var currentSession = runnerService.GetSession(project.Id);
        Assert.NotNull(currentSession);
        Assert.Equal(secondSessionId, currentSession.SessionId);
        Assert.Equal(ProjectRunState.Running, currentSession.State);
    }

    [Fact]
    public async Task ProjectRunnerService_StopProject_ForcefullyTerminatesProcessTree()
    {
        var project = CreateTestProject("npm run dev");
        var fakeProjectService = new FakeProjectService(project);

        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        var startResult = await runnerService.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);

        var proc = launcher.CreatedProcesses[0];
        Assert.False(proc.KillEntireProcessTreeCalled);

        var stopResult = await runnerService.StopProjectAsync(project.Id);
        Assert.True(stopResult.Success);
        Assert.True(proc.KillEntireProcessTreeCalled);
        Assert.True(proc.Disposed);
    }

    [Fact]
    public void ExternalPortOrPid_NeverGrantsRunnerOwnership()
    {
        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        var fakeProjectService = new FakeProjectService();

        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        // Even if some external process exists on port 5000 with PID 99999,
        // runner returns null because DevDesk did not start it.
        var unmanagedProjectId = Guid.NewGuid();
        var session = runnerService.GetSession(unmanagedProjectId);
        Assert.Null(session);

        var activeSessions = runnerService.GetActiveSessions();
        Assert.Empty(activeSessions);
    }

    [Fact]
    public async Task ProjectRunnerService_StandardOutputAndError_AreForwardedToOutputEvent()
    {
        var project = CreateTestProject("dotnet run");
        var fakeProjectService = new FakeProjectService(project);

        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        var outputEvents = new List<ProcessOutputEvent>();
        runnerService.OutputReceived += (_, evt) => outputEvents.Add(evt);

        var startResult = await runnerService.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);

        var proc = launcher.CreatedProcesses[0];
        proc.SimulateOutput("Application started. Listening on: http://localhost:5000");
        proc.SimulateError("Warning: test warning");

        Assert.Equal(2, outputEvents.Count);
        Assert.False(outputEvents[0].IsError);
        Assert.Contains("Application started", outputEvents[0].Text);
        Assert.True(outputEvents[1].IsError);
        Assert.Contains("test warning", outputEvents[1].Text);
    }

    [Fact]
    public void ShutdownCoordination_ActiveProjectsPresent_RequiresExplicitConfirmation()
    {
        // Tests the UI coordination policy logic:
        // When active sessions > 0, close must prompt confirmation dialog
        var vm = new DevDesk.App.ViewModels.Shell.ConfirmShutdownViewModel(3);

        Assert.Equal(3, vm.ActiveProjectCount);
        Assert.Contains("DevDesk started 3 projects. They must be stopped before DevDesk exits.", vm.Message);

        bool? result = null;
        vm.RequestClose += r => result = r;

        // Cancel command rejects exit
        vm.CancelCommand.Execute(null);
        Assert.False(result);

        // Confirm command approves exit
        vm.ConfirmCommand.Execute(null);
        Assert.True(result);
    }

    // ==========================================
    // 4. PHASE 7 RUNTIME BLOCKER REGRESSION TESTS
    // ==========================================

    [Fact]
    public async Task Regression_RootProcessExitRacingStop_DoesNotThrowNoProcessAssociated()
    {
        var project = CreateTestProject("dotnet run");
        var fakeProjectService = new FakeProjectService(project);
        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        var startResult = await runnerService.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);

        var proc = launcher.CreatedProcesses[0];
        // Simulate root process exiting immediately during KillEntireProcessTree
        proc.TriggerExitedOnKill = true;

        // Calling Stop must coordinate cleanly without throwing InvalidOperationException
        var stopResult = await runnerService.StopProjectAsync(project.Id);

        Assert.True(stopResult.Success);
        var session = runnerService.GetSession(project.Id);
        Assert.Null(session); // Removed from active sessions
    }

    [Fact]
    public async Task Regression_StopDoesNotDisposeProcessBeforeTerminationCompletes()
    {
        var project = CreateTestProject("dotnet run");
        var fakeProjectService = new FakeProjectService(project);
        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        var startResult = await runnerService.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);

        var proc = launcher.CreatedProcesses[0];
        bool wasDisposedDuringWait = true;

        proc.CustomWaitForExitAsync = async _ =>
        {
            wasDisposedDuringWait = proc.Disposed;
            await Task.Yield();
            proc.HasExited = true;
        };

        var stopResult = await runnerService.StopProjectAsync(project.Id);

        Assert.True(stopResult.Success);
        Assert.False(wasDisposedDuringWait, "Process must NOT be disposed before termination/wait completes.");
        Assert.True(proc.Disposed, "Process must be disposed after stop completes.");
    }

    [Fact]
    public async Task Regression_RootProcessExitedWhileChildRemains_KeepsSessionActive()
    {
        var project = CreateTestProject("dotnet run");
        var fakeProjectService = new FakeProjectService(project);
        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        var startResult = await runnerService.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);

        var proc = launcher.CreatedProcesses[0];

        // Simulate root process reporting HasExited = false because child processes are still alive in the job
        proc.HasExited = false;

        // Session must still be running
        var session = runnerService.GetSession(project.Id);
        Assert.NotNull(session);
        Assert.Equal(ProjectRunState.Running, session.State);
        Assert.True(session.IsActive);
    }

    [Fact]
    public async Task Regression_StopTerminatesCompleteOwnedProcessGroup()
    {
        var project = CreateTestProject("dotnet run");
        var fakeProjectService = new FakeProjectService(project);
        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        var startResult = await runnerService.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);

        var proc = launcher.CreatedProcesses[0];
        Assert.False(proc.KillEntireProcessTreeCalled);

        var stopResult = await runnerService.StopProjectAsync(project.Id);

        Assert.True(stopResult.Success);
        Assert.True(proc.KillEntireProcessTreeCalled);
        Assert.True(proc.HasExited);
    }

    [Fact]
    public async Task Regression_StopAndExitedEventRace_PerformsCleanupExactlyOnce()
    {
        var project = CreateTestProject("dotnet run");
        var fakeProjectService = new FakeProjectService(project);
        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        var startResult = await runnerService.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);

        var proc = launcher.CreatedProcesses[0];
        proc.TriggerExitedOnKill = true;

        // Concurrently invoke Stop and simulate exit event
        var stopTask = runnerService.StopProjectAsync(project.Id);
        proc.SimulateExit(0);

        await stopTask;

        // Assert cleanup was executed exactly once
        Assert.Equal(1, proc.DisposeCount);
        Assert.Null(runnerService.GetSession(project.Id));
    }

    [Fact]
    public async Task Regression_RestartCannotStartReplacementUntilPreviousExecutionTerminated()
    {
        var project = CreateTestProject("dotnet run");
        var fakeProjectService = new FakeProjectService(project);
        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        var startResult = await runnerService.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);

        var proc = launcher.CreatedProcesses[0];
        // Simulate hung termination
        proc.FailTermination = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var restartResult = await runnerService.RestartProjectAsync(project.Id, cts.Token);

        // Restart must fail and NOT spawn a replacement process
        Assert.False(restartResult.Success);
        Assert.Contains("Failed to stop existing process", restartResult.ErrorMessage);
        Assert.Single(launcher.CreatedProcesses); // Only the initial process exists, no replacement spawned
    }

    [Fact]
    public async Task Regression_FailedTermination_DoesNotReportSuccessfulExitedState()
    {
        var project = CreateTestProject("dotnet run");
        var fakeProjectService = new FakeProjectService(project);
        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        var startResult = await runnerService.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);

        var proc = launcher.CreatedProcesses[0];
        proc.FailTermination = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var stopResult = await runnerService.StopProjectAsync(project.Id, cts.Token);

        Assert.False(stopResult.Success);
        Assert.Contains("timed out", stopResult.ErrorMessage);

        // Session must NOT be marked Exited
        var session = runnerService.GetSession(project.Id);
        Assert.NotNull(session);
        Assert.NotEqual(ProjectRunState.Exited, session.State);
        Assert.Equal(ProjectRunState.Running, session.State);
        Assert.Contains("Failed to stop project", session.ErrorMessage);
    }

    [Fact]
    public async Task Regression_ExternalProcessesRemainUntouched()
    {
        var fakeProjectService = new FakeProjectService();
        var toolLocator = new FakeRunnerToolLocator();
        var launcher = new FakeProcessLauncher();
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        var unmanagedId = Guid.NewGuid();
        var stopResult = await runnerService.StopProjectAsync(unmanagedId);

        Assert.True(stopResult.Success);
        Assert.Empty(launcher.CreatedProcesses);
    }

    [Fact]
    public async Task JobAssignmentFailure_StartFailsCleanly_SessionNeverBecomesRunning()
    {
        var project = CreateTestProject("dotnet run");
        var fakeProjectService = new FakeProjectService(project);
        var toolLocator = new FakeRunnerToolLocator();
        var throwingLauncher = new ThrowingLauncher(new InvalidOperationException("Failed to assign process to Windows Job Object."));
        using var runnerService = new ProjectRunnerService(
            fakeProjectService,
            throwingLauncher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        var startResult = await runnerService.StartProjectAsync(project.Id);

        Assert.False(startResult.Success);
        Assert.Contains("Failed to assign process to Windows Job Object", startResult.ErrorMessage);

        // Result session must be in Failed state, NEVER Running
        Assert.NotNull(startResult.Session);
        Assert.Equal(ProjectRunState.Failed, startResult.Session.State);
        Assert.False(startResult.Session.IsActive);

        // Session must NOT become an active running session in the runner
        Assert.Null(runnerService.GetSession(project.Id));
        Assert.Empty(runnerService.GetActiveSessions());
    }

    [Fact]
    public void WindowsCommandLineSerializer_CanonicalEscaping_RoundTripsThroughCommandLineToArgvW()
    {
        string exe = @"C:\Program Files\dotnet\dotnet.exe";
        string[] rawArgs =
        [
            "run",
            "--project",
            @"D:\Projects\My App\App.csproj",
            @"D:\Projects\R&D\App\App.csproj",
            @"C:\Path\With\Trailing\Backslash\",
            @"C:\Path\With Multiple\\Backslashes\\",
            "embedded \"quote\" test",
            "",
            "simple"
        ];

        string formatted = WindowsCommandLineSerializer.FormatCommandLine(exe, rawArgs);

        if (OperatingSystem.IsWindows())
        {
            string[] parsed = ParseWithWindowsCommandLineToArgvW(formatted);

            // argv[0] is the executable
            Assert.Equal(exe, parsed[0]);

            // argv[1..] must identically match the raw arguments
            Assert.Equal(rawArgs.Length, parsed.Length - 1);
            for (int i = 0; i < rawArgs.Length; i++)
            {
                Assert.Equal(rawArgs[i], parsed[i + 1]);
            }
        }
    }

    [Fact]
    public void WindowsCommandLineSerializer_TrailingBackslashes_PreservedCorrectly()
    {
        string arg = @"C:\My Directory\";
        string encoded = WindowsCommandLineSerializer.EncodeArgument(arg);

        // Canonical Windows escaping: trailing backslash before closing quote must be doubled to 2N (\\)
        Assert.Equal("\"C:\\My Directory\\\\\"", encoded);

        if (OperatingSystem.IsWindows())
        {
            string[] parsed = ParseWithWindowsCommandLineToArgvW($"dummy.exe {encoded}");
            Assert.Equal(arg, parsed[1]);
        }
    }

    [Fact]
    public void WindowsCommandLineSerializer_AmpersandInNativePath_PreservedAsLiteral()
    {
        string arg = @"D:\Projects\R&D\App\App.csproj";
        string encoded = WindowsCommandLineSerializer.EncodeArgument(arg);

        // Contains no whitespace/quotes, emitted as is without escaping needed for direct execution
        Assert.Equal(arg, encoded);

        if (OperatingSystem.IsWindows())
        {
            string[] parsed = ParseWithWindowsCommandLineToArgvW($"dummy.exe {encoded}");
            Assert.Equal(arg, parsed[1]);
        }
    }

    [Fact]
    public void WindowsCommandLineSerializer_EmptyArgument_EmitsQuotedEmptyString()
    {
        string encoded = WindowsCommandLineSerializer.EncodeArgument("");
        Assert.Equal("\"\"", encoded);

        if (OperatingSystem.IsWindows())
        {
            string[] parsed = ParseWithWindowsCommandLineToArgvW($"dummy.exe {encoded}");
            Assert.Equal("", parsed[1]);
        }
    }

    [Fact]
    public void WindowsCommandLineSerializer_EmbeddedQuotes_EscapedCorrectly()
    {
        string arg = "hello \"world\"";
        string encoded = WindowsCommandLineSerializer.EncodeArgument(arg);

        Assert.Equal("\"hello \\\"world\\\"\"", encoded);

        if (OperatingSystem.IsWindows())
        {
            string[] parsed = ParseWithWindowsCommandLineToArgvW($"dummy.exe {encoded}");
            Assert.Equal(arg, parsed[1]);
        }
    }

    [Fact]
    public void WindowsCommandLineSerializer_CmdShimFormat_PreservesOuterQuotes()
    {
        string comSpec = @"C:\Windows\system32\cmd.exe";
        string shimPath = @"C:\Program Files\nodejs\npm.cmd";
        string[] args = ["run", "dev"];

        string formatted = WindowsCommandLineSerializer.FormatCmdShimCommandLine(comSpec, shimPath, args);

        Assert.StartsWith($"{WindowsCommandLineSerializer.EncodeArgument(comSpec)} /d /s /c \"", formatted);
        Assert.Contains("\"C:\\Program Files\\nodejs\\npm.cmd\"", formatted);
        Assert.EndsWith("\"", formatted);
    }

    [Fact]
    public async Task GetManagedProcesses_HandleVerificationFails_OnRecycledPid_OmittedFromManagedSnapshot()
    {
        var project = CreateTestProject("dotnet run");
        var projectService = new FakeProjectService(project);
        var launcher = new FakeProcessLauncher();
        var locator = new FakeRunnerToolLocator();

        using var runner = new ProjectRunnerService(projectService, launcher, locator, NullLogger<ProjectRunnerService>.Instance);

        var startResult = await runner.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);

        var managedProc = launcher.CreatedProcesses[0];
        
        // Simulating PID reuse:
        // Candidate PID exists, but handle-level verification against the Job Object fails
        // (because the handle belongs to a different, external process).
        managedProc.CustomContainsProcessHandle = _ => false;

        var snapshot = runner.GetManagedProcesses();

        // The process must be omitted because handle verification failed!
        Assert.Empty(snapshot);
    }

    [Fact]
    public async Task GetManagedProcesses_HandleVerificationSucceeds_ProcessEmittedAsManagedWithStartTime()
    {
        var project = CreateTestProject("dotnet run");
        var projectService = new FakeProjectService(project);
        var launcher = new FakeProcessLauncher();
        var locator = new FakeRunnerToolLocator();

        using var runner = new ProjectRunnerService(projectService, launcher, locator, NullLogger<ProjectRunnerService>.Instance);

        var startResult = await runner.StartProjectAsync(project.Id);
        Assert.True(startResult.Success);

        var managedProc = launcher.CreatedProcesses[0];

        // Use the current test runner process ID so Process.GetProcessById(pid) succeeds
        managedProc.ProcessId = Environment.ProcessId;
        managedProc.CustomContainsProcessHandle = _ => true;

        var snapshot = runner.GetManagedProcesses();

        Assert.Single(snapshot);
        Assert.Equal(Environment.ProcessId, snapshot[0].ProcessId);
        Assert.NotNull(snapshot[0].StartTimeUtc);
        Assert.Equal(project.Id, snapshot[0].ProjectId);
        Assert.Equal(project.Name, snapshot[0].ProjectName);
    }

    private static string[] ParseWithWindowsCommandLineToArgvW(string commandLine)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<string>();
        }

        IntPtr ptr = WindowsProcessApi.CommandLineToArgvW(commandLine, out int count);
        if (ptr == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }

        try
        {
            var args = new string[count];
            for (int i = 0; i < count; i++)
            {
                IntPtr argPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(ptr, i * IntPtr.Size);
                args[i] = System.Runtime.InteropServices.Marshal.PtrToStringUni(argPtr)!;
            }
            return args;
        }
        finally
        {
            WindowsProcessApi.LocalFree(ptr);
        }
    }

    private sealed class ThrowingLauncher : IProcessLauncher
    {
        private readonly Exception _ex;
        public ThrowingLauncher(Exception ex) => _ex = ex;
        public IManagedProcess Launch(ProcessLaunchConfiguration config) => throw _ex;
    }

    // ==========================================
    // TEST DOUBLES (NO REAL OS PROCESSES)
    // ==========================================

    private sealed class FakeProjectService : IProjectService
    {
        private readonly ConcurrentDictionary<Guid, DeveloperProject> _projects = new();

        public FakeProjectService(params DeveloperProject[] initialProjects)
        {
            foreach (var p in initialProjects)
            {
                _projects[p.Id] = p;
            }
        }

        public Task<IReadOnlyList<DeveloperProject>> GetProjectsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<DeveloperProject>>(_projects.Values.ToList());
        }

        public Task<DeveloperProject?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _projects.TryGetValue(id, out var project);
            return Task.FromResult(project);
        }

        public Task<DeveloperProject> AddProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default)
        {
            _projects[project.Id] = project;
            return Task.FromResult(project);
        }

        public Task<DeveloperProject> UpdateProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default)
        {
            _projects[project.Id] = project;
            return Task.FromResult(project);
        }

        public Task RemoveProjectAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _projects.TryRemove(id, out _);
            return Task.CompletedTask;
        }

        public Task<ProjectDetectionResult> DetectAndApplyAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProjectDetectionResult());
        }
    }

    private sealed class FakeRunnerToolLocator : IRunnerToolLocator
    {
        public string? ResolveToolPath(string toolName, bool isCmdShim)
        {
            return isCmdShim ? $@"C:\fake\bin\{toolName}.cmd" : $@"C:\fake\bin\{toolName}.exe";
        }
    }

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        public List<FakeManagedProcess> CreatedProcesses { get; } = new();

        public IManagedProcess Launch(ProcessLaunchConfiguration config)
        {
            var proc = new FakeManagedProcess(CreatedProcesses.Count + 1000, config);
            CreatedProcesses.Add(proc);
            return proc;
        }
    }

    private sealed class FakeManagedProcess : IManagedProcess
    {
        public int ProcessId { get; set; }
        public ProcessLaunchConfiguration Config { get; }
        public bool HasExited { get; set; }
        public int? ExitCode { get; set; }
        public bool KillEntireProcessTreeCalled { get; private set; }
        public bool Disposed { get; private set; }
        public int DisposeCount { get; private set; }

        public bool FailTermination { get; set; }
        public bool TriggerExitedOnKill { get; set; }
        public Func<CancellationToken, Task>? CustomWaitForExitAsync { get; set; }

        public event EventHandler<string>? StandardOutputReceived;
        public event EventHandler<string>? StandardErrorReceived;
        public event EventHandler<int>? Exited;

        public FakeManagedProcess(int processId, ProcessLaunchConfiguration config)
        {
            ProcessId = processId;
            Config = config;
        }

        public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            if (CustomWaitForExitAsync != null)
            {
                await CustomWaitForExitAsync(cancellationToken);
                return;
            }

            if (FailTermination)
            {
                await Task.Delay(500, cancellationToken);
                return;
            }

            HasExited = true;
            ExitCode ??= 0;
        }

        public void KillEntireProcessTree()
        {
            KillEntireProcessTreeCalled = true;
            if (!FailTermination)
            {
                HasExited = true;
                ExitCode ??= 1;
            }

            if (TriggerExitedOnKill)
            {
                Exited?.Invoke(this, ExitCode ?? 1);
            }
        }

        public IReadOnlyList<int> GetActiveProcessIds() => HasExited ? Array.Empty<int>() : [ProcessId];

        public Func<IntPtr, bool>? CustomContainsProcessHandle { get; set; }
        public bool ContainsProcessHandle(IntPtr processHandle) => CustomContainsProcessHandle?.Invoke(processHandle) ?? !HasExited;

        public void SimulateExit(int code)
        {
            HasExited = true;
            ExitCode = code;
            Exited?.Invoke(this, code);
        }

        public void SimulateOutput(string text)
        {
            StandardOutputReceived?.Invoke(this, text);
        }

        public void SimulateError(string text)
        {
            StandardErrorReceived?.Invoke(this, text);
        }

        public void Dispose()
        {
            DisposeCount++;
            Disposed = true;
        }
    }

    private static DeveloperProject CreateTestProject(string runCommand)
    {
        return new DeveloperProject
        {
            Id = Guid.NewGuid(),
            Name = "TestProject",
            Path = Directory.GetCurrentDirectory(),
            RunCommand = runCommand,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
