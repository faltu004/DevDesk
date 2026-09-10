# DevDesk Development Workflow

For each major feature:
1. Design
2. Define contracts/data
3. Review security/permission implications
4. Implement the smallest working vertical slice
5. Build
6. Test
7. Manual UI/system QA where needed
8. Refactor only where justified
9. Commit

## Definition of done
- solution builds
- relevant tests pass
- UI thread remains responsive
- expected errors handled
- security rules respected
- user-facing errors are useful
- changed files + verification summarized

## Coding principles
- correct MVVM
- minimal code-behind
- purposeful DI
- minimal dependencies
- async I/O
- cancellation where lifecycle/background work warrants it
- no giant detector/process/port classes
- no placeholder implementations in completed features

## Git
Recommended:
```text
main
└─ feature/<feature-name>
```

Before commit:
```powershell
dotnet build
dotnet test
git status
git diff
```
