# DevDesk Security Model

DevDesk interacts with processes, commands, files, Git, ports, and eventually environment configuration/services.

## Rules
1. Never silently terminate a process.
2. Do not run DevDesk permanently as Administrator.
3. Use least privilege.
4. Confirm destructive operations.
5. Clearly identify elevation requirements.
6. Mask secrets.
7. Do not log passwords, tokens, API keys, connection strings, or secret env values.
8. Validate project paths.
9. Prefer structured executable + argument execution.
10. Treat shell commands as explicitly trusted user actions.
11. Never interpolate untrusted input into shell strings.
12. Handle access-denied/protected processes gracefully.
13. Never auto-kill a process to solve a port conflict.
14. Do not show raw stack traces to normal users.

## Stop-process flow
```text
User requests Stop
→ revalidate target
→ show identity/details
→ confirmation
→ attempt termination
→ verify
→ refresh state
```

## Secrets
Future `.env` support must mask by default, reveal only explicitly, avoid automatic clipboard copies, redact diagnostics, and avoid duplicating secrets into DevDesk persistence without a deliberate design.

## Elevation
Keep normal operation standard-user. Elevate only isolated actions if a future feature genuinely requires it.
