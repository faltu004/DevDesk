# DevDesk Product Requirements Document

## Product
**DevDesk — Windows Developer Control Center**

A local-first Windows desktop application that makes the development project the central object for managing runtime processes, ports, Git state, commands, logs, and environment status.

## Problem
Developers frequently switch between Task Manager, PowerShell, Command Prompt, Git Bash, VS Code, Explorer, `netstat`, `taskkill`, `.env` files, and browser tabs. DevDesk unifies the most useful project-oriented workflows.

## Primary workflow
```text
Select Project
→ Detect Project
→ Check Environment
→ Check Git
→ Check Required Port
→ Detect Conflict
→ Start
→ Capture Logs
→ Show PID + Port
→ Open Browser / VS Code / Terminal / Explorer
→ Stop or Restart Safely
```

## MVP requirements
### Shell
- Modern WPF shell
- Sidebar navigation
- Dark theme
- loading/empty/error states

### Dashboard
- CPU, RAM, disk, uptime/Windows info
- active projects
- recent projects
- active developer ports
- Git-change and port-conflict summary

### Projects
- add/edit/remove project folders
- path validation
- persistent configuration
- project details
- recent projects

### Detection
Recognize useful signals such as `package.json`, `next.config.*`, `vite.config.*`, `.csproj`, `requirements.txt`, `pyproject.toml`, `Cargo.toml`, `pom.xml`, `build.gradle`, `.git`, and `.env*`.

### Launchers
- VS Code
- terminal
- Explorer
- browser

### Runner
- configured run command
- start/stop/restart
- working directory
- PID/start time
- port association
- stdout/stderr

### Ports
- discover listeners
- map port → PID → process
- associate known project where possible
- detect conflicts
- never automatically kill the occupant

### Processes
- name, PID, CPU, RAM
- start time/path/command line when available
- safe termination with confirmation
- protected-process handling

### Git
Read-only MVP:
- repository detection
- branch
- modified/added/deleted/untracked
- recent commits
- remote

### Commands
- saved project commands
- optional global commands
- working directory
- structured execution where possible
- explicit shell mode when needed
- output capture

### Persistence
SQLite for durable app/project configuration.

### Settings
Theme, editor, terminal, refresh intervals, confirmations, log retention.

## Out of MVP
AI features, plugins, cloud accounts/sync, developer service manager, secret editor, integrated terminal emulator, advanced Git write operations, GPU/temperature telemetry.

## Success criterion
A user can add a project, detect it, inspect Git/port state, launch it, view logs/PID/port, safely stop/restart it, inspect related ports/processes, and reopen it in normal developer tools from a polished Windows UI.
