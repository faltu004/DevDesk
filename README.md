# DevDesk — Windows Developer Control Center

DevDesk is a local-first Windows desktop application for managing developer projects, processes, ports, Git status, saved commands, logs, and local environment state from one place.

**Product identity:** A developer workspace and environment manager for Windows.

## MVP
- Application shell/navigation
- Dashboard + basic system monitor
- Project manager
- Project auto-detection
- VS Code / Terminal / Explorer launchers
- Run / stop / restart configured project commands
- Live stdout/stderr logs
- Process manager
- Port manager + conflict detection
- Read-only Git status
- Saved commands
- SQLite persistence
- Settings
- Tests and production-quality error handling

## Repository
```text
DevDesk/
├─ assets/references/
├─ docs/
│  ├─ adr/
│  ├─ PRD.md
│  ├─ ARCHITECTURE.md
│  ├─ ROADMAP.md
│  ├─ SECURITY.md
│  ├─ UI-UX.md
│  └─ DEVELOPMENT.md
├─ scripts/
├─ src/
├─ tests/
├─ .editorconfig
├─ .gitignore
├─ AGENTS.md
├─ Directory.Build.props
└─ README.md
```

Place the approved UI mockups in `assets/references/`.

See `docs/PRD.md` before implementing product features and `AGENTS.md` before using a coding agent.
