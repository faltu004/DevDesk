# DevDesk Architecture

## Solution
```text
DevDesk.sln
src/
├─ DevDesk.App
├─ DevDesk.Core
└─ DevDesk.Infrastructure
tests/
└─ DevDesk.Tests
```

## Responsibilities
### DevDesk.App
WPF Views, ViewModels, navigation, dialogs, styles/resources, composition root.

### DevDesk.Core
Models, interfaces, validation/business rules, project-detection contracts. Must not depend on WPF, EF Core, SQLite, Git implementation details, or concrete Windows APIs.

### DevDesk.Infrastructure
SQLite/EF Core, filesystem, detectors, processes, ports, command execution, Git CLI, system monitoring, launchers, Windows integrations.

### DevDesk.Tests
Tests important non-UI logic and infrastructure behavior where practical.

## Dependency direction
```text
App → Core
App → Infrastructure → Core
Tests → Core / Infrastructure
```

## Persisted vs runtime state
Durable: project path/framework/commands/default port/timestamps.

Runtime: PID/current CPU-RAM/listening port/stdout-stderr/current process state/current Git snapshot.

Do not merge them for UI convenience.

## Initial service boundaries
- `IProjectRepository`
- `IProjectService`
- `IProjectDetectionService`
- `IProjectDetector`
- `ILauncherService`
- `ICommandExecutionService`
- `IProjectRunnerService`
- `IPortService`
- `IProcessService`
- `IGitService`
- `ISystemMonitorService`
- `ISettingsService`

## Detection
Prefer small detectors coordinated by one service rather than a giant `if/else` chain.

## Runner
```text
ViewModel
→ IProjectRunnerService
→ validate
→ port check
→ process start
→ stdout/stderr capture
→ runtime session
→ UI
```

Prefer structured executable + arguments; use shell semantics only when intentionally required.

## Logging
- Application diagnostics: `ILogger<T>`
- Project stdout/stderr: project runtime/log model

Do not combine both into one generic logging abstraction.
