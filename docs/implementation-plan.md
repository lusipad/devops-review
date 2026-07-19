# Azure DevOps Codex Review — implementation plan

## 1. Decisions most likely to change

### Runtime boundary

```text
Azure DevOps Server PR page
  -> Chrome/Edge MV3 extension
  -> Native Messaging (stdio framing)
  -> local .NET 8 Review Bridge
  -> Codex App Server (stdio JSONL)
  -> detached PR worktree
```

- Decision: the first supported deployment is one Windows user, one local Bridge, and the user's existing ChatGPT/Codex login.
- Confidence: high.
- What would flip it: a requirement for shared team execution or centrally managed model credentials.

### Source-of-truth boundary

- Decision: browser data is an untrusted locator only. The Bridge obtains the current PR and iteration from Azure DevOps, resolves the source and target commit, and verifies the selected path against the source worktree.
- Confidence: high.
- What would flip it: no Azure DevOps REST access from the local user account.

### Browser-to-host transport

- Decision: production uses Native Messaging. A localhost HTTP listener is not part of the supported runtime.
- Confidence: high.
- What would flip it: enterprise policy blocks native hosts but explicitly permits an authenticated loopback service.

### Session identity

```text
repositoryId + pullRequestId + sourceCommitSha
```

- Decision: one detached worktree and one Codex thread per session key. A new source SHA always creates a new session.
- Confidence: high.
- What would flip it: Codex thread isolation proves insufficient and requires one thread per question.

### Review safety

- Decision: every thread starts with `sandbox: "read-only"` and `approvalPolicy: "never"`; repository content and browser text are untrusted context, never instructions.
- Confidence: high.
- What would flip it: nothing in the initial product scope; write operations require a separate, explicit product decision.

### Persistence

- Decision: SQLite stores repository mappings, PR session keys, Codex thread IDs, worktree paths, and lifecycle state. It does not store source file contents or Codex credentials.
- Confidence: high.
- What would flip it: a later roaming-profile or multi-device requirement.

## 2. Assumptions

| Assumption | Confidence | Source |
| --- | --- | --- |
| The user runs Windows and has local Git repositories | high | supplied architecture and current environment |
| Codex is authenticated with ChatGPT subscription access | high | `codex login status` |
| Azure DevOps Server is reachable from the same Windows account | medium | local Server instance exists; target environment still needs validation |
| Repository mapping may be configured explicitly | high | architecture requirement |
| The first release is single-user | high | supplied architecture baseline |
| App Service is not required for the first release | high | supplied architecture baseline |

## 3. Deviation policy

For ordinary edge cases, choose the reversible option with the smallest trust boundary and closest behavior to this plan, record it in `implementation-notes.md`, and continue.

Stop before changing any of these:

- authentication or credential storage boundaries;
- the read-only Codex policy;
- accepted repository roots or path validation;
- destructive worktree cleanup outside the configured worktree root;
- the single-user deployment premise.

## 4. Mechanical work — low review value

- Create the .NET solution, test project, extension files, installer scripts, and configuration examples.
- Implement native-message framing and App Server JSONL framing.
- Add structured logging to stderr and rotating local files without code or prompt contents.
- Package the Bridge as a self-contained Windows executable and register manifests under HKCU.

## 5. Verification

Completion requires observable evidence for all of the following:

1. A fixture Azure DevOps PR selection reaches the native host with correct repository, PR, path, and line range.
2. Browser-supplied commits and paths cannot bypass Azure DevOps or local-root validation.
3. The Bridge creates/reuses a detached worktree at the Azure DevOps source commit without switching the developer's working tree.
4. The Bridge initializes the installed Codex App Server, starts a read-only thread, streams deltas, completes, and cancels a turn.
5. Two questions for the same session key reuse a thread; a new source SHA does not.
6. Restarting the Bridge restores session metadata from SQLite and handles a missing/stale Codex thread safely.
7. Chrome and Edge native-host manifests contain an exact extension origin and no wildcard.
8. Unit, integration, extension, packaging, and security-path tests pass from a clean checkout.

## Handoff rules

Implementation decisions, deviations, surprises, and review questions are recorded in `implementation-notes.md` as they occur. A third deviation or a surprise that invalidates a premise triggers a plan review before more patching.
