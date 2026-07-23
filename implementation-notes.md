# Implementation notes — Azure DevOps Codex Review

Plan: `docs/implementation-plan.md`

## Decisions

- 2026-07-20: Use Native Messaging as the only production browser transport. This keeps the Bridge off the TCP network surface.
- 2026-07-20: Treat browser selection data as an untrusted locator. Azure DevOps and the detached worktree remain authoritative.
- 2026-07-20: Target `net8.0` while pinning the development SDK through `global.json` to the installed .NET 10 SDK.
- 2026-07-20: Keep completed answers only in the active Native Messaging host process. Explicit publish requests reference the original review ID; the browser cannot replace the answer being posted.
- 2026-07-20: Filter App Server `commentary` items from the answer and normalize local worktree links before the answer becomes publishable.
- 2026-07-23: Package the existing release payload into a per-user Inno Setup EXE. Keep the browser extension unpacked because Chrome/Edge do not allow a normal installer to silently load it.
- 2026-07-23: Use a stable `%LOCALAPPDATA%\Programs\DevOpsReview\app` install path so upgrades replace the active Bridge while preserving `%LOCALAPPDATA%\DevOpsReview` data.

## Deviations

None.

## Surprises

- The supplied App Server example used `sandbox: "readOnly"`; the installed `0.144.5` schema requires `sandbox: "read-only"`.
- The current Codex CLI marks `app-server` itself as experimental, while the stdio protocol and generated version-specific schema are available.
- Live App Server smoke test passed with ChatGPT subscription authentication: first delta 8.35 seconds, total 8.46 seconds on this machine.
- A Git child inherited the long-lived Native Messaging stdin pipe and waited indefinitely; redirecting and immediately closing Git stdin fixed the worktree preparation hang.
- App Server JSON was corrupted when a native host inherited the Windows code page; explicit UTF-8 on all three redirected streams fixed Chinese prompts and responses.
- Azure DevOps Server 2022 Monaco reports a non-empty selection string while DOM `isCollapsed` remains true. The stable line source is its hidden textarea `selectionStart`/`selectionEnd`, not selected text-node ancestry.
- Isolated Edge QA found and fixed the panel initialization ordering bug that left Ask disabled after the Native Host connected.
- NuGet audit found the bundled `SQLitePCLRaw.lib.e_sqlite3` chain affected by CVE-2025-6965 with no patched package version. Because the product is Windows-only, the Bridge now uses `Microsoft.Data.Sqlite.Core` with `SQLitePCLRaw.bundle_winsqlite3`; the follow-up audit reports no vulnerable packages.
- The generated Native Messaging manifest is not tracked by Inno Setup's `[Files]` section; an explicit `[UninstallDelete]` entry is required to avoid leaving the install directory behind.

## Questions for review

None.
