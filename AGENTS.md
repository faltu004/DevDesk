# DevDesk Agent Instructions

DevDesk is a serious local-first Windows developer control center. Keep it project-centric; do not turn it into a Task Manager clone, generic shell wrapper, or AI wrapper.

## Workflow
1. Read relevant docs.
2. Inspect existing code before editing.
3. Explain design/security implications for major features.
4. Make the smallest coherent change.
5. Keep system/business logic out of WPF code-behind.
6. Build affected projects.
7. Run relevant tests.
8. Report changed files and verification results.

## Architecture
- `DevDesk.Core`: models, contracts, rules; no WPF/EF/SQLite/concrete Windows implementation.
- `DevDesk.Infrastructure`: persistence, filesystem, Git, process, port, command execution, system/Windows integration.
- `DevDesk.App`: WPF views/viewmodels/resources/navigation/composition root.
- `DevDesk.Tests`: important non-UI tests.
- Keep persisted project configuration separate from live runtime state.
- Do not create abstractions without a concrete reason.

## Safety
- Never silently terminate a process.
- Never run the whole app permanently as Administrator.
- Confirm destructive actions.
- Prefer structured executable + argument execution.
- Treat shell commands as explicit trusted-user actions.
- Never interpolate untrusted text into shell commands.
- Never log passwords, API keys, tokens, connection strings, or secret env values.
- Validate project paths.
- Handle protected/access-denied processes gracefully.

## Scope
MVP order: foundation → shell → persistence → projects → detection → launchers → ports → runner → logs → processes → Git → commands → system monitor → settings → polish.

Do not add AI, plugin architecture, cloud sync, integrated terminal emulation, or advanced Git write operations before the roadmap reaches them.

## UI
Use `assets/references/` as the visual source of truth: professional dark developer-tool UI, restrained blue/teal accents, green success/running, red danger/conflict, consistent spacing, readable hierarchy.
