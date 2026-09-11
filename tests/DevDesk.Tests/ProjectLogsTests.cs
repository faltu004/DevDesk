using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using DevDesk.App.ViewModels.Projects;
using DevDesk.Core.Models;
using DevDesk.Core.Runner;
using DevDesk.Core.Services;
using DevDesk.Infrastructure.Runner;

namespace DevDesk.Tests;

public sealed class ProjectLogsTests
{
    // ==========================================
    // 1. SEQUENCE NUMBER & DETERMINISTIC MERGED ORDER
    // ==========================================

    [Fact]
    public async Task ProjectRunner_AssignsMonotonicallyIncreasingSequenceNumbers_PerSession()
    {
        var (runner, launcher, project) = CreateTestSetup();

        var runResult = await runner.StartProjectAsync(project.Id);
        Assert.True(runResult.Success);

        var proc = launcher.CreatedProcesses.Single();
        var capturedEvents = new List<ProcessOutputEvent>();
        runner.OutputReceived += (_, e) => capturedEvents.Add(e);

        proc.SimulateOutput("line 1");
        proc.SimulateError("err 1");
        proc.SimulateOutput("line 2");
        proc.SimulateError("err 2");

        Assert.Equal(4, capturedEvents.Count);
        Assert.Equal(1, capturedEvents[0].SequenceNumber);
        Assert.Equal(2, capturedEvents[1].SequenceNumber);
        Assert.Equal(3, capturedEvents[2].SequenceNumber);
        Assert.Equal(4, capturedEvents[3].SequenceNumber);

        Assert.False(capturedEvents[0].IsError);
        Assert.True(capturedEvents[1].IsError);
        Assert.False(capturedEvents[2].IsError);
        Assert.True(capturedEvents[3].IsError);
    }

    [Fact]
    public async Task DeterministicMergedOrder_OrderableBySessionIdAndSequence()
    {
        var (runner, launcher, project) = CreateTestSetup();

        await runner.StartProjectAsync(project.Id);
        var proc = launcher.CreatedProcesses.Single();

        var captured = new List<ProcessOutputEvent>();
        runner.OutputReceived += (_, e) => captured.Add(e);

        for (int i = 0; i < 20; i++)
        {
            if (i % 2 == 0)
                proc.SimulateOutput($"stdout {i}");
            else
                proc.SimulateError($"stderr {i}");
        }

        // Validate merged deterministic ordering by SequenceNumber
        var ordered = captured.OrderBy(c => c.SequenceNumber).ToList();
        Assert.Equal(captured, ordered);
        for (int i = 0; i < captured.Count; i++)
        {
            Assert.Equal(i + 1, captured[i].SequenceNumber);
        }
    }

    // ==========================================
    // 2. SESSION ISOLATION & BOUNDED SESSION HISTORY
    // ==========================================

    [Fact]
    public async Task RestartProject_CreatesNewSession_WithIsolatedSequenceAndLogs()
    {
        var (runner, launcher, project) = CreateTestSetup();

        // 1. Run Session 1
        var start1 = await runner.StartProjectAsync(project.Id);
        var proc1 = launcher.CreatedProcesses[0];
        proc1.SimulateOutput("session 1 log");

        var session1Logs = runner.GetSessionLogs(project.Id, start1.Session!.SessionId);
        Assert.Single(session1Logs);
        Assert.Equal("session 1 log", session1Logs[0].Text);

        // 2. Restart (Session 2)
        var restart = await runner.RestartProjectAsync(project.Id);
        Assert.True(restart.Success);
        Assert.NotEqual(start1.Session.SessionId, restart.Session!.SessionId);

        var proc2 = launcher.CreatedProcesses[1];
        proc2.SimulateOutput("session 2 log");

        var session2Logs = runner.GetSessionLogs(project.Id, restart.Session.SessionId);
        Assert.Single(session2Logs);
        Assert.Equal("session 2 log", session2Logs[0].Text);
        Assert.Equal(1, session2Logs[0].SequenceNumber);

        // Session 1 logs remain distinct
        var oldLogs = runner.GetSessionLogs(project.Id, start1.Session.SessionId);
        Assert.Single(oldLogs);
        Assert.Equal("session 1 log", oldLogs[0].Text);
    }

    [Fact]
    public async Task BoundedSessionHistory_RetainsAtMostCurrentPlusTwoCompletedSessions()
    {
        var (runner, launcher, project) = CreateTestSetup();

        // Run and stop session 1
        var r1 = await runner.StartProjectAsync(project.Id);
        var p1 = launcher.CreatedProcesses.Last();
        p1.SimulateOutput("s1");
        await runner.StopProjectAsync(project.Id);

        // Run and stop session 2
        var r2 = await runner.StartProjectAsync(project.Id);
        var p2 = launcher.CreatedProcesses.Last();
        p2.SimulateOutput("s2");
        await runner.StopProjectAsync(project.Id);

        // Run and stop session 3
        var r3 = await runner.StartProjectAsync(project.Id);
        var p3 = launcher.CreatedProcesses.Last();
        p3.SimulateOutput("s3");
        await runner.StopProjectAsync(project.Id);

        // Run and stop session 4
        var r4 = await runner.StartProjectAsync(project.Id);
        var p4 = launcher.CreatedProcesses.Last();
        p4.SimulateOutput("s4");
        await runner.StopProjectAsync(project.Id);

        var recent = runner.GetRecentSessions(project.Id);

        // Retained max 2 completed sessions: s3 and s4 (s1 and s2 evicted FIFO)
        Assert.Equal(2, recent.Count);
        Assert.Contains(recent, s => s.SessionId == r4.Session!.SessionId);
        Assert.Contains(recent, s => s.SessionId == r3.Session!.SessionId);
        Assert.DoesNotContain(recent, s => s.SessionId == r1.Session!.SessionId);
        Assert.DoesNotContain(recent, s => s.SessionId == r2.Session!.SessionId);

        // Evicted sessions return empty snapshot
        Assert.Empty(runner.GetSessionLogs(project.Id, r1.Session!.SessionId));
        Assert.NotEmpty(runner.GetSessionLogs(project.Id, r4.Session!.SessionId));
    }

    // ==========================================
    // 3. BOUNDED LOG BUFFER LIMITS
    // ==========================================

    [Fact]
    public void BoundedLogBuffer_PreservesPhase7Limits_AndEvictsOldest()
    {
        var buffer = new BoundedLogBuffer(maxLines: 5, maxLineLength: 20, maxTotalBytes: 5000);
        var sessionId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        for (int i = 1; i <= 10; i++)
        {
            buffer.Add(sessionId, projectId, $"line {i}", isError: false, sequenceNumber: i);
        }

        var snapshot = buffer.GetSnapshot();
        Assert.Equal(5, snapshot.Count);
        Assert.Equal("line 6", snapshot[0].Text);
        Assert.Equal("line 10", snapshot[4].Text);
    }

    [Fact]
    public void BoundedLogBuffer_TruncatesOversizedLines()
    {
        var buffer = new BoundedLogBuffer(maxLines: 10, maxLineLength: 10, maxTotalBytes: 5000);
        var item = buffer.Add(Guid.NewGuid(), Guid.NewGuid(), "1234567890EXTRA_TEXT", isError: false);

        Assert.StartsWith("1234567890 [truncated]", item.Text);
    }

    // ==========================================
    // 4. TRUTHFUL TERMINATION REASON & FINAL DRAIN
    // ==========================================

    [Fact]
    public async Task StopProject_SetsTerminationReason_StoppedByDevDesk()
    {
        var (runner, launcher, project) = CreateTestSetup();

        await runner.StartProjectAsync(project.Id);
        var stop = await runner.StopProjectAsync(project.Id);
        Assert.True(stop.Success);

        var sessions = runner.GetRecentSessions(project.Id);
        Assert.Single(sessions);
        Assert.Equal(ProjectTerminationReason.StoppedByDevDesk, sessions[0].TerminationReason);
    }

    [Fact]
    public async Task NaturalExit_SetsTerminationReason_NaturalExit_WithTruthfulCode()
    {
        var (runner, launcher, project) = CreateTestSetup();

        await runner.StartProjectAsync(project.Id);
        var proc = launcher.CreatedProcesses.Single();

        proc.SimulateExit(42);

        var sessions = runner.GetRecentSessions(project.Id);
        Assert.Single(sessions);
        Assert.Equal(ProjectTerminationReason.NaturalExit, sessions[0].TerminationReason);
        Assert.Equal(42, sessions[0].ExitCode);
    }

    [Fact]
    public async Task FinalOutput_RemainsInSnapshot_AfterProcessExit()
    {
        var (runner, launcher, project) = CreateTestSetup();

        var run = await runner.StartProjectAsync(project.Id);
        var proc = launcher.CreatedProcesses.Single();

        proc.SimulateOutput("starting...");
        proc.SimulateError("fatal error occurred at shutdown!");
        proc.SimulateExit(1);

        var snapshot = runner.GetSessionLogs(project.Id, run.Session!.SessionId);
        Assert.Equal(2, snapshot.Count);
        Assert.Equal("fatal error occurred at shutdown!", snapshot[1].Text);
        Assert.True(snapshot[1].IsError);
    }

    // ==========================================
    // 5. VIEWMODEL: CLEAR VIEW WATERMARK & SEARCH/FILTER
    // ==========================================

    [Fact]
    public void ProjectLogsViewModel_ClearView_SetsWatermark_AndSurvivesSearchRebuild()
    {
        var (runner, _, project) = CreateTestSetup();
        using var vm = new ProjectLogsViewModel(runner, NullLogger<ProjectLogsViewModel>.Instance);

        var sessionId = Guid.NewGuid();
        var fakeRunner = new FakeRunnerWithLogs();
        var vmWithFake = new ProjectLogsViewModel(fakeRunner, NullLogger<ProjectLogsViewModel>.Instance);

        fakeRunner.Logs[sessionId] =
        [
            new ProcessOutputEvent { SessionId = sessionId, ProjectId = project.Id, Text = "line 1", IsError = false, SequenceNumber = 1, Timestamp = DateTimeOffset.UtcNow },
            new ProcessOutputEvent { SessionId = sessionId, ProjectId = project.Id, Text = "line 2", IsError = false, SequenceNumber = 2, Timestamp = DateTimeOffset.UtcNow },
            new ProcessOutputEvent { SessionId = sessionId, ProjectId = project.Id, Text = "line 3", IsError = false, SequenceNumber = 3, Timestamp = DateTimeOffset.UtcNow }
        ];

        var session = new ProjectRunSession
        {
            SessionId = sessionId,
            ProjectId = project.Id,
            ProjectName = project.Name,
            CommandText = "dotnet run",
            ExecutablePath = "dotnet.exe",
            Arguments = [],
            State = ProjectRunState.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        fakeRunner.Sessions[project.Id] = [session];

        vmWithFake.SetProject(project.Id, sessionId);
        Assert.Equal(3, vmWithFake.VisibleLogs.Count);

        // Clear View at sequence 3
        vmWithFake.ClearView();
        Assert.Empty(vmWithFake.VisibleLogs);

        // Search text change would rebuild visible logs:
        vmWithFake.FilterText = "line";
        // Since lines 1, 2, 3 are <= watermark 3, they MUST remain hidden!
        Assert.Empty(vmWithFake.VisibleLogs);

        // Underlying runner history remains completely intact:
        Assert.Equal(3, fakeRunner.GetSessionLogs(project.Id, sessionId).Count);
    }

    [Fact]
    public void ProjectLogsViewModel_FilterStream_StdoutStderr()
    {
        var project = CreateTestProject("dotnet run");
        var sessionId = Guid.NewGuid();
        var fakeRunner = new FakeRunnerWithLogs();
        using var vm = new ProjectLogsViewModel(fakeRunner, NullLogger<ProjectLogsViewModel>.Instance);

        fakeRunner.Logs[sessionId] =
        [
            new ProcessOutputEvent { SessionId = sessionId, ProjectId = project.Id, Text = "out 1", IsError = false, SequenceNumber = 1, Timestamp = DateTimeOffset.UtcNow },
            new ProcessOutputEvent { SessionId = sessionId, ProjectId = project.Id, Text = "err 1", IsError = true, SequenceNumber = 2, Timestamp = DateTimeOffset.UtcNow },
            new ProcessOutputEvent { SessionId = sessionId, ProjectId = project.Id, Text = "out 2", IsError = false, SequenceNumber = 3, Timestamp = DateTimeOffset.UtcNow }
        ];

        fakeRunner.Sessions[project.Id] =
        [
            new ProjectRunSession
            {
                SessionId = sessionId,
                ProjectId = project.Id,
                ProjectName = "P",
                CommandText = "dotnet run",
                ExecutablePath = "dotnet.exe",
                Arguments = [],
                State = ProjectRunState.Running,
                StartedAt = DateTimeOffset.UtcNow
            }
        ];

        vm.SetProject(project.Id, sessionId);
        Assert.Equal(3, vm.VisibleLogs.Count);

        // Stdout only
        vm.IsFilterStdout = true;
        Assert.Equal(2, vm.VisibleLogs.Count);
        Assert.All(vm.VisibleLogs, l => Assert.False(l.IsError));

        // Stderr only
        vm.IsFilterStderr = true;
        Assert.Single(vm.VisibleLogs);
        Assert.True(vm.VisibleLogs[0].IsError);

        // All
        vm.IsFilterAll = true;
        Assert.Equal(3, vm.VisibleLogs.Count);
    }

    [Fact]
    public void ProjectLogsViewModel_SearchText_FiltersCaseInsensitive()
    {
        var project = CreateTestProject("dotnet run");
        var sessionId = Guid.NewGuid();
        var fakeRunner = new FakeRunnerWithLogs();
        using var vm = new ProjectLogsViewModel(fakeRunner, NullLogger<ProjectLogsViewModel>.Instance);

        fakeRunner.Logs[sessionId] =
        [
            new ProcessOutputEvent { SessionId = sessionId, ProjectId = project.Id, Text = "Listening on http://localhost:5000", IsError = false, SequenceNumber = 1, Timestamp = DateTimeOffset.UtcNow },
            new ProcessOutputEvent { SessionId = sessionId, ProjectId = project.Id, Text = "Application started. Press Ctrl+C to shut down.", IsError = false, SequenceNumber = 2, Timestamp = DateTimeOffset.UtcNow },
            new ProcessOutputEvent { SessionId = sessionId, ProjectId = project.Id, Text = "Hosting environment: Development", IsError = false, SequenceNumber = 3, Timestamp = DateTimeOffset.UtcNow }
        ];

        fakeRunner.Sessions[project.Id] =
        [
            new ProjectRunSession
            {
                SessionId = sessionId,
                ProjectId = project.Id,
                ProjectName = "P",
                CommandText = "dotnet run",
                ExecutablePath = "dotnet.exe",
                Arguments = [],
                State = ProjectRunState.Running,
                StartedAt = DateTimeOffset.UtcNow
            }
        ];

        vm.SetProject(project.Id, sessionId);

        // Filter text
        vm.FilterText = "LOCALHOST";
        vm.ApplyFilterImmediate();

        Assert.Single(vm.VisibleLogs);
        Assert.Contains("5000", vm.VisibleLogs[0].Text);
    }

    [Fact]
    public void ProjectLogsViewModel_AutoScroll_UpdatesOnScrollNotification()
    {
        var fakeRunner = new FakeRunnerWithLogs();
        using var vm = new ProjectLogsViewModel(fakeRunner, NullLogger<ProjectLogsViewModel>.Instance);

        Assert.True(vm.AutoScroll);
        Assert.Equal(0, vm.UnseenLogsCount);

        // User scrolls up
        vm.NotifyUserScrolled(isNearBottom: false);
        Assert.False(vm.AutoScroll);

        // User clicks scroll to bottom
        vm.ScrollToBottom();
        Assert.True(vm.AutoScroll);
        Assert.Equal(0, vm.UnseenLogsCount);
    }

    [Fact]
    public void ProjectLogsViewModel_OutOfOrderDelivery_PreservesBothEventsInSequenceOrder()
    {
        var project = CreateTestProject("dotnet run");
        var sessionId = Guid.NewGuid();
        var fakeRunner = new FakeRunnerWithLogs();
        using var vm = new ProjectLogsViewModel(fakeRunner, NullLogger<ProjectLogsViewModel>.Instance);

        fakeRunner.Sessions[project.Id] =
        [
            new ProjectRunSession
            {
                SessionId = sessionId,
                ProjectId = project.Id,
                ProjectName = "P",
                CommandText = "dotnet run",
                ExecutablePath = "dotnet.exe",
                Arguments = [],
                State = ProjectRunState.Running,
                StartedAt = DateTimeOffset.UtcNow
            }
        ];

        vm.SetProject(project.Id, sessionId);
        Assert.Empty(vm.VisibleLogs);

        // Sequence 2 arrives FIRST (e.g. from stderr thread)
        fakeRunner.EmitOutput(new ProcessOutputEvent
        {
            SessionId = sessionId,
            ProjectId = project.Id,
            SequenceNumber = 2,
            Text = "stderr line 2",
            IsError = true,
            Timestamp = DateTimeOffset.UtcNow
        });

        // Sequence 1 arrives LATER (e.g. from stdout thread)
        fakeRunner.EmitOutput(new ProcessOutputEvent
        {
            SessionId = sessionId,
            ProjectId = project.Id,
            SequenceNumber = 1,
            Text = "stdout line 1",
            IsError = false,
            Timestamp = DateTimeOffset.UtcNow
        });

        // Process batch delivery
        vm.ProcessDeliveryBatch();

        // Verify neither was dropped, and final visible order is strictly SequenceNumber order
        Assert.Equal(2, vm.VisibleLogs.Count);
        Assert.Equal(1, vm.VisibleLogs[0].SequenceNumber);
        Assert.Equal("stdout line 1", vm.VisibleLogs[0].Text);
        Assert.False(vm.VisibleLogs[0].IsError);

        Assert.Equal(2, vm.VisibleLogs[1].SequenceNumber);
        Assert.Equal("stderr line 2", vm.VisibleLogs[1].Text);
        Assert.True(vm.VisibleLogs[1].IsError);
    }

    [Fact]
    public void ProjectLogsViewModel_OutOfOrderDelivery_AcrossMultipleTicks_PreservesOrderAndNoLoss()
    {
        var project = CreateTestProject("dotnet run");
        var sessionId = Guid.NewGuid();
        var fakeRunner = new FakeRunnerWithLogs();
        using var vm = new ProjectLogsViewModel(fakeRunner, NullLogger<ProjectLogsViewModel>.Instance);

        fakeRunner.Sessions[project.Id] =
        [
            new ProjectRunSession
            {
                SessionId = sessionId,
                ProjectId = project.Id,
                ProjectName = "P",
                CommandText = "dotnet run",
                ExecutablePath = "dotnet.exe",
                Arguments = [],
                State = ProjectRunState.Running,
                StartedAt = DateTimeOffset.UtcNow
            }
        ];

        vm.SetProject(project.Id, sessionId);

        // Tick 1: Seq 2 arrives alone
        fakeRunner.EmitOutput(new ProcessOutputEvent
        {
            SessionId = sessionId,
            ProjectId = project.Id,
            SequenceNumber = 2,
            Text = "line 2",
            IsError = false,
            Timestamp = DateTimeOffset.UtcNow
        });
        vm.ProcessDeliveryBatch();

        Assert.Single(vm.VisibleLogs);
        Assert.Equal(2, vm.VisibleLogs[0].SequenceNumber);

        // Tick 2: Seq 1 arrives late (after seq 2 was already processed into VisibleLogs)
        fakeRunner.EmitOutput(new ProcessOutputEvent
        {
            SessionId = sessionId,
            ProjectId = project.Id,
            SequenceNumber = 1,
            Text = "line 1",
            IsError = false,
            Timestamp = DateTimeOffset.UtcNow
        });
        vm.ProcessDeliveryBatch();

        // Both must be visible and properly ordered
        Assert.Equal(2, vm.VisibleLogs.Count);
        Assert.Equal(1, vm.VisibleLogs[0].SequenceNumber);
        Assert.Equal("line 1", vm.VisibleLogs[0].Text);
        Assert.Equal(2, vm.VisibleLogs[1].SequenceNumber);
        Assert.Equal("line 2", vm.VisibleLogs[1].Text);
    }

    [Fact]
    public void ProjectLogsViewModel_ClearView_IsSessionScoped_AndSurvivesSessionSwitch()
    {
        var project = CreateTestProject("dotnet run");
        var sessionAId = Guid.NewGuid();
        var sessionBId = Guid.NewGuid();
        var fakeRunner = new FakeRunnerWithLogs();
        using var vm = new ProjectLogsViewModel(fakeRunner, NullLogger<ProjectLogsViewModel>.Instance);

        var sessionA = new ProjectRunSession
        {
            SessionId = sessionAId,
            ProjectId = project.Id,
            ProjectName = "P",
            CommandText = "dotnet run",
            ExecutablePath = "dotnet.exe",
            Arguments = [],
            State = ProjectRunState.Exited,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var sessionB = new ProjectRunSession
        {
            SessionId = sessionBId,
            ProjectId = project.Id,
            ProjectName = "P",
            CommandText = "dotnet run",
            ExecutablePath = "dotnet.exe",
            Arguments = [],
            State = ProjectRunState.Running,
            StartedAt = DateTimeOffset.UtcNow
        };

        fakeRunner.Sessions[project.Id] = [sessionA, sessionB];
        fakeRunner.Logs[sessionAId] =
        [
            new ProcessOutputEvent { SessionId = sessionAId, ProjectId = project.Id, SequenceNumber = 1, Text = "A1", IsError = false, Timestamp = DateTimeOffset.UtcNow },
            new ProcessOutputEvent { SessionId = sessionAId, ProjectId = project.Id, SequenceNumber = 2, Text = "A2", IsError = false, Timestamp = DateTimeOffset.UtcNow },
            new ProcessOutputEvent { SessionId = sessionAId, ProjectId = project.Id, SequenceNumber = 3, Text = "A3", IsError = false, Timestamp = DateTimeOffset.UtcNow }
        ];

        fakeRunner.Logs[sessionBId] =
        [
            new ProcessOutputEvent { SessionId = sessionBId, ProjectId = project.Id, SequenceNumber = 1, Text = "B1", IsError = false, Timestamp = DateTimeOffset.UtcNow },
            new ProcessOutputEvent { SessionId = sessionBId, ProjectId = project.Id, SequenceNumber = 2, Text = "B2", IsError = false, Timestamp = DateTimeOffset.UtcNow }
        ];

        // 1. View Session A and Clear View at sequence 3
        vm.SetProject(project.Id, sessionAId);
        Assert.Equal(3, vm.VisibleLogs.Count);
        vm.ClearView();
        Assert.Empty(vm.VisibleLogs);
        Assert.Equal(3, vm.GetClearWatermark(sessionAId));

        // 2. Switch to Session B: Session B starts at sequence 1, must NOT be hidden by Session A cutoff
        vm.SetSession(sessionB);
        Assert.Equal(2, vm.VisibleLogs.Count);
        Assert.Equal("B1", vm.VisibleLogs[0].Text);
        Assert.Equal("B2", vm.VisibleLogs[1].Text);
        Assert.Equal(0, vm.GetClearWatermark(sessionBId));

        // 3. Switch back to Session A: Session A's clear cutoff (3) must be preserved
        vm.SetSession(sessionA);
        Assert.Empty(vm.VisibleLogs);
        Assert.Equal(3, vm.GetClearWatermark(sessionAId));

        // 4. Live output arrives on Session A with sequence 4 (above watermark 3)
        fakeRunner.EmitOutput(new ProcessOutputEvent
        {
            SessionId = sessionAId,
            ProjectId = project.Id,
            SequenceNumber = 4,
            Text = "A4 post-clear",
            IsError = false,
            Timestamp = DateTimeOffset.UtcNow
        });
        vm.ProcessDeliveryBatch();

        Assert.Single(vm.VisibleLogs);
        Assert.Equal("A4 post-clear", vm.VisibleLogs[0].Text);
    }

    [Fact]
    public void ProjectLogsViewModel_SnapshotAndLiveBoundary_DeduplicatesExactSequence()
    {
        var project = CreateTestProject("dotnet run");
        var sessionId = Guid.NewGuid();
        var fakeRunner = new FakeRunnerWithLogs();
        using var vm = new ProjectLogsViewModel(fakeRunner, NullLogger<ProjectLogsViewModel>.Instance);

        fakeRunner.Sessions[project.Id] =
        [
            new ProjectRunSession
            {
                SessionId = sessionId,
                ProjectId = project.Id,
                ProjectName = "P",
                CommandText = "dotnet run",
                ExecutablePath = "dotnet.exe",
                Arguments = [],
                State = ProjectRunState.Running,
                StartedAt = DateTimeOffset.UtcNow
            }
        ];

        // Snapshot already has seq 1 and 2
        fakeRunner.Logs[sessionId] =
        [
            new ProcessOutputEvent { SessionId = sessionId, ProjectId = project.Id, SequenceNumber = 1, Text = "snapshot 1", IsError = false, Timestamp = DateTimeOffset.UtcNow },
            new ProcessOutputEvent { SessionId = sessionId, ProjectId = project.Id, SequenceNumber = 2, Text = "snapshot 2", IsError = false, Timestamp = DateTimeOffset.UtcNow }
        ];

        vm.SetProject(project.Id, sessionId);
        Assert.Equal(2, vm.VisibleLogs.Count);

        // Live stream emits duplicate seq 2 and new seq 3
        fakeRunner.EmitOutput(new ProcessOutputEvent
        {
            SessionId = sessionId,
            ProjectId = project.Id,
            SequenceNumber = 2,
            Text = "live duplicate 2",
            IsError = false,
            Timestamp = DateTimeOffset.UtcNow
        });
        fakeRunner.EmitOutput(new ProcessOutputEvent
        {
            SessionId = sessionId,
            ProjectId = project.Id,
            SequenceNumber = 3,
            Text = "live new 3",
            IsError = false,
            Timestamp = DateTimeOffset.UtcNow
        });

        vm.ProcessDeliveryBatch();

        // Total should be 3 (seq 1, 2, 3), not 4
        Assert.Equal(3, vm.VisibleLogs.Count);
        Assert.Equal(1, vm.VisibleLogs[0].SequenceNumber);
        Assert.Equal(2, vm.VisibleLogs[1].SequenceNumber);
        Assert.Equal("snapshot 2", vm.VisibleLogs[1].Text);
        Assert.Equal(3, vm.VisibleLogs[2].SequenceNumber);
        Assert.Equal("live new 3", vm.VisibleLogs[2].Text);
    }

    [Fact]
    public void ProjectLogsViewModel_StaleSessionEvents_AreRejected()
    {
        var project = CreateTestProject("dotnet run");
        var activeSessionId = Guid.NewGuid();
        var staleSessionId = Guid.NewGuid();
        var fakeRunner = new FakeRunnerWithLogs();
        using var vm = new ProjectLogsViewModel(fakeRunner, NullLogger<ProjectLogsViewModel>.Instance);

        fakeRunner.Sessions[project.Id] =
        [
            new ProjectRunSession
            {
                SessionId = activeSessionId,
                ProjectId = project.Id,
                ProjectName = "P",
                CommandText = "dotnet run",
                ExecutablePath = "dotnet.exe",
                Arguments = [],
                State = ProjectRunState.Running,
                StartedAt = DateTimeOffset.UtcNow
            }
        ];

        vm.SetProject(project.Id, activeSessionId);

        // Emit event for stale session
        fakeRunner.EmitOutput(new ProcessOutputEvent
        {
            SessionId = staleSessionId,
            ProjectId = project.Id,
            SequenceNumber = 999,
            Text = "stale event",
            IsError = false,
            Timestamp = DateTimeOffset.UtcNow
        });

        vm.ProcessDeliveryBatch();
        Assert.Empty(vm.VisibleLogs);
    }

    [Fact]
    public void ProjectLogsViewModel_TimestampConversion_AgreesBetweenSessionAndLogLines()
    {
        // Deterministic UTC capture timestamp: 2026-09-11 12:06:32.456 UTC
        var baseUtc = new DateTimeOffset(2026, 9, 11, 12, 6, 32, 456, TimeSpan.Zero);
        var sessionId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        // 1. Session created with UTC capture timestamp in Core model
        var session = new ProjectRunSession
        {
            SessionId = sessionId,
            ProjectId = projectId,
            ProjectName = "TimestampTest",
            CommandText = "dotnet run",
            ExecutablePath = "dotnet.exe",
            Arguments = [],
            State = ProjectRunState.Running,
            StartedAt = baseUtc
        };

        // 2. App presentation wrapper wraps Core model
        var sessionItem = new SessionPresentationItem(session);

        // 3. Log line created 1.5 seconds later with UTC capture timestamp
        var logLine = new LogLineItem
        {
            SessionId = sessionId,
            SequenceNumber = 1,
            Timestamp = baseUtc.AddMilliseconds(1500),
            Text = "Server started",
            IsError = false
        };

        // Assert: Core domain storage is strictly UTC
        Assert.Equal(TimeSpan.Zero, session.StartedAt.Offset);
        Assert.Equal(TimeSpan.Zero, sessionItem.StartedAt.Offset);
        Assert.Equal(TimeSpan.Zero, logLine.Timestamp.Offset);

        // Assert: App presentation conversion to local time is applied consistently
        var expectedSessionLocal = baseUtc.ToLocalTime();
        var expectedLogLocal = baseUtc.AddMilliseconds(1500).ToLocalTime();

        Assert.Equal(expectedSessionLocal, sessionItem.LocalStartedAt);
        Assert.Equal(expectedSessionLocal.ToString("HH:mm:ss"), sessionItem.FormattedStartedAt);

        Assert.Equal(expectedLogLocal, logLine.LocalTimestamp);
        Assert.Equal(expectedLogLocal.ToString("HH:mm:ss.fff"), logLine.FormattedTimestamp);

        // Assert: Timezone offsets match local system offset exactly (no manual or double offset)
        var expectedOffset = TimeZoneInfo.Local.GetUtcOffset(baseUtc.DateTime);
        Assert.Equal(expectedOffset, sessionItem.LocalStartedAt.Offset);
        Assert.Equal(expectedOffset, logLine.LocalTimestamp.Offset);

        // Assert: The elapsed time between session start and log line is preserved exactly
        Assert.Equal(TimeSpan.FromMilliseconds(1500), logLine.LocalTimestamp - sessionItem.LocalStartedAt);
    }

    // ==========================================
    // HELPERS & FAKES
    // ==========================================

    private static (ProjectRunnerService runner, FakeProcessLauncher launcher, DeveloperProject project) CreateTestSetup()
    {
        var project = CreateTestProject("dotnet run");
        var projectService = new FakeProjectService([project]);
        var launcher = new FakeProcessLauncher();
        var toolLocator = new FakeToolLocator(isShimValid: true);

        var runner = new ProjectRunnerService(
            projectService,
            launcher,
            toolLocator,
            NullLogger<ProjectRunnerService>.Instance);

        return (runner, launcher, project);
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

    private sealed class FakeRunnerWithLogs : IProjectRunnerService
    {
        public Dictionary<Guid, List<ProjectRunSession>> Sessions { get; } = new();
        public Dictionary<Guid, List<ProcessOutputEvent>> Logs { get; } = new();

        public event EventHandler<ProjectRunSession>? SessionChanged;
        public event EventHandler<ProcessOutputEvent>? OutputReceived;

        public Task<ProjectRunResult> StartProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProjectStopResult> StopProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ProjectRunResult> RestartProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProjectStopResult>> StopAllAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ProjectRunSession? GetSession(Guid projectId) => Sessions.TryGetValue(projectId, out var list) ? list.FirstOrDefault() : null;
        public IReadOnlyList<ProjectRunSession> GetActiveSessions() => Array.Empty<ProjectRunSession>();
        public IReadOnlyList<ProjectRunSession> GetRecentSessions(Guid projectId) => Sessions.TryGetValue(projectId, out var list) ? list : Array.Empty<ProjectRunSession>();
        public IReadOnlyList<ProcessOutputEvent> GetSessionLogs(Guid projectId, Guid sessionId) => Logs.TryGetValue(sessionId, out var list) ? list : Array.Empty<ProcessOutputEvent>();

        public void EmitOutput(ProcessOutputEvent evt) => OutputReceived?.Invoke(this, evt);
        public void EmitSession(ProjectRunSession session) => SessionChanged?.Invoke(this, session);
    }

    private sealed class FakeProjectService : IProjectService
    {
        private readonly List<DeveloperProject> _projects;
        public FakeProjectService(IEnumerable<DeveloperProject> projects) => _projects = projects.ToList();
        public Task<DeveloperProject?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_projects.FirstOrDefault(p => p.Id == id));
        public Task<IReadOnlyList<DeveloperProject>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DeveloperProject>>(_projects);
        public Task<DeveloperProject> AddProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DeveloperProject> UpdateProjectAsync(DeveloperProject project, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task RemoveProjectAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DevDesk.Core.Detection.ProjectDetectionResult> DetectAndApplyAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeToolLocator : IRunnerToolLocator
    {
        private readonly bool _isShimValid;
        public FakeToolLocator(bool isShimValid) => _isShimValid = isShimValid;
        public string? ResolveToolPath(string toolName, bool isCmdShim) => "C:\\tools\\" + toolName + (isCmdShim ? ".cmd" : ".exe");
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
        public int ProcessId { get; }
        public ProcessLaunchConfiguration Config { get; }
        public bool HasExited { get; set; }
        public int? ExitCode { get; set; }

        public event EventHandler<string>? StandardOutputReceived;
        public event EventHandler<string>? StandardErrorReceived;
        public event EventHandler<int>? Exited;

        public FakeManagedProcess(int processId, ProcessLaunchConfiguration config)
        {
            ProcessId = processId;
            Config = config;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            HasExited = true;
            ExitCode ??= 0;
            return Task.CompletedTask;
        }

        public void KillEntireProcessTree()
        {
            HasExited = true;
            ExitCode ??= 1;
        }

        public void SimulateExit(int code)
        {
            HasExited = true;
            ExitCode = code;
            Exited?.Invoke(this, code);
        }

        public void SimulateOutput(string text) => StandardOutputReceived?.Invoke(this, text);
        public void SimulateError(string text) => StandardErrorReceived?.Invoke(this, text);
        public void Dispose() { }
    }
}
