# DevDesk v1.0.0

Welcome to the initial release of **DevDesk**, a local-first Windows developer control center engineered to consolidate daily project workflows, process inspection, port management, Git oversight, command execution, and live environment telemetry into a single workstation dashboard.

---

## Highlights

- **Unified Developer Dashboard**: Real-time overview of active projects, listening dev ports, repository change tracking, and machine resource telemetry (CPU, RAM, storage, network).
- **Zero-Dependency Portable Release**: Self-contained win-x64 build requiring no preinstalled .NET runtime.
- **Safety-First Architecture**: Standard user execution, Job Object-isolated process management, structured process argument tokens, and zero credential exposure.
- **Local-First SQLite Engine**: High-performance local persistence stored directly on your machine with automatic schema migration.
- **Streamlined Workflow**: Search filtering and global `Ctrl + K` hotkey for immediate navigation across all workspaces, processes, ports, and commands.

---

## Project Management

- Register local code repositories and workspaces with custom aliases, descriptions, and run configurations.
- Heuristic auto-detection for supported project frameworks and stacks:
  - **.NET**: C#, F#, VB.NET solutions and projects (`dotnet run`, `dotnet build`, `dotnet test`).
  - **Node.js / Web**: Next.js, Vite, React, Node.js (`npm`, `pnpm`, `yarn`, `bun`).
  - **Python**: Django, Flask, FastAPI (`python`, `pytest`, `poetry`, `uv`, `pipenv`, `pip`).
- Automatic detection of entry scripts, package managers, and suggested build/run/test commands.
- Edit project properties or unregister workspaces without deleting source code on disk.

---

## Developer Tooling

- Quick-launch buttons to open registered project folders in:
  - **Visual Studio Code** (`Code.exe` detection with manual override in Settings).
  - **Terminal** (Auto-detects Windows Terminal, PowerShell 7, Windows PowerShell, Command Prompt, or Git Bash).
  - **File Explorer** (Navigates directly to the project root on disk).
- Copy project paths, command definitions, and port numbers with one click.

---

## Process & Port Inspection

- **Process Inspector**:
  - System-wide inventory of active processes with PID, sampled CPU utilization, and private memory working set.
  - Ownership mode filtering to distinguish DevDesk-managed processes from external system processes.
  - Real-time text search filtering across process names, PIDs, and associated project paths.
  - Standard-user execution requiring no administrative elevation for normal inspection.
- **TCP Listener & Port Inspector**:
  - Live scan of all local TCP endpoints in `Listening` state using native IP Helper APIs (`GetExtendedTcpTable`).
  - Correlates listening ports to owning PIDs and executable process names.
  - Proactive port conflict detection against ports assigned to registered projects.

---

## Project Runner & Logs

- Start, stop, and restart project run commands directly from the DevDesk interface.
- Supervised execution inside dedicated Windows Job Objects, ensuring complete and reliable tree-scoped process termination when stopped.
- Bounded, high-throughput stdout and stderr capture.
- Virtualized log viewer with stream-level filtering (All, stdout, stderr), real-time search, clear view watermarking, and auto-scroll.
- Prevents concurrent runs of the same project while maintaining distinct session logs.

---

## Git Integration

- Read-only Git repository inspection powered by the local Git CLI.
- Displays active branch, detached HEAD state, and configured upstream tracking branch.
- Ahead and behind commit counters relative to remote tracking branches.
- Working tree modification breakdown: Staged, Modified, Untracked, and Conflicted files.
- Displays author, relative timestamp, commit hash, and summary of the most recent commit.
- Non-destructive and safe: never performs automated Git write actions (commits, merges, or pushes).

---

## Saved Commands

- Centralized library of project-scoped and global developer commands.
- Organizable by categories (e.g., `Build`, `Test`, `Lint`, `Deploy`, `Database`).
- Structured executable and argument token persistence without shell string interpolation.
- Dedicated output stream capture with copy-to-clipboard functionality.

---

## System Monitoring

- Continuous background telemetry measuring host performance metrics:
  - Total system CPU utilization across all logical cores.
  - Physical RAM consumption and total system capacity.
  - Primary drive utilization, free space, and storage percentage.
  - Active network adapter throughput (activity indicator).
- Configurable polling intervals to conserve background workstation resources.

---

## Settings

- Preferences panel organized into distinct categories:
  - **General**: Theme configuration (DevDesk Native Dark theme).
  - **Editors & Terminals**: Custom VS Code path override and preferred terminal emulator selection with safe fallback.
  - **Monitoring**: Customizable live telemetry refresh rates (0.5s, 1s, 1.5s default, 2s, 5s).
  - **Data & About**: App version display (1.0.0), platform diagnostics, database health verification, Git CLI detection, Node.js runtime detection, and direct shortcut to the local data directory.

---

## Safety & Security

- **Standard-User Execution**: Does not require permanent administrator privileges to run or manage projects.
- **Safe Process Boundaries**: Never terminates processes by generic names or blind port kills. Runner terminations are strictly restricted to processes launched by DevDesk inside assigned Job Objects.
- **Structured Command Invocations**: All processes are spawned using structured argument lists, eliminating command-line injection vectors.
- **Privacy by Default**: No telemetry or user information is transmitted across the internet. No passwords, tokens, or environment secrets are stored or logged.

---

## Local Data & Privacy

- DevDesk stores all metadata, registered workspaces, and preferences in a local SQLite database:
  ```text
  %LOCALAPPDATA%\DevDesk\Data\devdesk.db
  ```
- Backups can be made simply by copying the `devdesk.db` file when DevDesk is closed.

---

## System Requirements

- **Operating System**: Windows 10 64-bit (version 1809 or higher) or Windows 11 64-bit.
- **Architecture**: x64.
- **Dependencies**: None required for the portable release package. (The .NET runtime is bundled inside the distribution).
- **Optional Tools**: Git for Windows, VS Code, Node.js, or .NET SDK (detected automatically if available in your system PATH).

---

## Known Limitations

- DevDesk is built exclusively for Windows environments; Linux and macOS are not supported.
- Version 1.0 supports the native dark developer theme only.
- Initial release is distributed as a self-contained portable ZIP archive rather than a Windows installer (MSIX / Inno Setup installer packaging planned for future releases).
