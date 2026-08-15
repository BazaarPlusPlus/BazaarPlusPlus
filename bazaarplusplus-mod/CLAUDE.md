# CLAUDE.md

How to work in this repository (`AGENTS.md` is a symlink to this file). This file holds process; it points at everything else rather than restating it.

| When you are about to | Read |
|---|---|
| Touch any code | `docs/MEMORY.md` — invariants and traps that fail silently |
| Name a concept, in code or an issue title | `CONTEXT.md` |
| Find who owns or constructs something | `docs/ARCHITECTURE.md`, then its per-feature pointer table |
| Re-litigate a decision this repo already made | `docs/adr/` |
| File, read, or label an issue | `docs/agents/issue-tracker.md` |
| Write, promote, or delete a document | `docs/README.md` |

## Build & Test Commands

`./run.sh` with no arguments lists every subcommand and what it does; it works on macOS and Windows (Git Bash). Build through it rather than calling `dotnet build` directly — the raw invocation skips the macOS trampoline repair that `run.sh` performs after every game update. The mod targets `netstandard2.1` (C# 12), and game assemblies resolve via `ManagedPath`, auto-detected from common Steam install paths or passed as `-p:ManagedPath=...`.

Below is only what `run.sh` cannot tell you:

- The default suite has 12 xUnit projects in `tests/BazaarPlusPlus.Tests.slnx`. `ScenarioRunner.Tests` owns the closed list of source-shadow executable capsules and runs each in a child process; use `dotnet run --project tests/<Name>/<Name>.csproj` only to diagnose one capsule directly.
- After `./run.sh restore-locks`, review the six changed `src/**/packages.lock.json` files before `./run.sh restore-locked` — the locked restore is what makes graph drift fail locally rather than in the installer build.
- Building from an isolated ticket worktree: pass `-p:BPPInstallerSourcePath="<absolute-path>/bazaarplusplus-installer/src-tauri/resources"` to projects referencing the main mod, because the default sibling installer path does not exist beside a worktree.
- `./run.sh test` resolves remote data from `tests/TestData/remote-data` and excludes the decompiled-source compatibility premises — those run only under `./run.sh test-compat`, which reports any premise it skips.

## Logs & Debugging

This mod is a **BepInEx 5.x plugin** (`BepInEx.Core` 5.\*). At runtime, BepInEx writes all console output to `<GameDir>/BepInEx/LogOutput.log` — the sibling of the `BepInEx/plugins/` folder the build copies into. Read that file to debug; mod log lines are structured events shaped `[BPP][<Scope>] event=<id> field=value ...`. `Debug`-level events emit only from Debug builds; `Info`/`Warning`/`Error` always emit.

Runtime validation that needs the game running must launch The Bazaar through Steam (App ID 1617400), so Steam runtime state is present: `open "steam://run/1617400"` on macOS, `start steam://run/1617400` on Windows. Launching `TheBazaar.app` directly, or via `run_bepinex.sh` on macOS, bypasses that state and fails in subtle ways.

## Architecture — layering traps

Where new code goes. This is the trap list, not a map of what exists:

- Reusable adapters over The Bazaar/Unity runtime surfaces go in `GameInterop/`. Feature workflows, UI state, product policy, filtering and classification rules, upload decisions, and storage orchestration go in `Game/`. Mentioning a game enum or DTO is not on its own a reason to move logic into `GameInterop/`.
- When two features need the same runtime, prefab, or static-data behavior, extract the adapter to `GameInterop/<Concept>/` and have both consume that seam. Reuse the seam rather than importing another feature's internals.
- Patches may target feature services through `BppPatchHost`, the static service locator — never constructor injection. Shared Harmony reflection helpers and native runtime adapters live in `GameInterop/` or `Infrastructure/`, not inside a feature directory.
- Establishing a boundary the compiler cannot enforce means adding or extending an architecture test in the same change.

## Project Rules

- Treat `decompiled/` as read-only reference for game behavior and APIs.
- For game-behavior bugs, root-cause against the decompiled game source before forming a hypothesis. Ground every conclusion in `file:line` citations rather than prose reasoning, and when the obvious fix fails, enumerate alternative cause mechanisms rather than writing another speculative patch.
- Treat the current repo code and `decompiled/` as the source of truth; design docs are reference and may be stale — re-check them against live code.
- When the user says a problem has failed repeatedly, stop reading implementation and decompiled source and first write a doc capturing background, the current problem, candidate approaches, and the verification method.
- Run an independent red-team review of a large refactor or design plan before implementing, and revise from it. Keep such a review review-only: it surfaces weaknesses, risks, and bad assumptions with `file:line` evidence and applies no patches. Send the revised plan back for confirmation before implementing.
- When replacing a subsystem or migrating to a prototype, remove the old implementation entirely and ship only the new version in place. A fallback path or a merged build chain running both is not a migration.
- Validate a hypothesis with a temporary probe on the main path (the user builds and reloads to verify), or record it as a to-verify item in the design doc and ship. Standalone probe scaffolding is not the way.
- Touch only the named target of a delete or change request. Widening scope or adjusting unrelated config belongs in its own change.
- A long-running automation task must self-heal: relaunch the game process on crash or exit and continue until the goal is met.
- Format every Git commit message as Conventional Commits: `<type>(<scope>): <description>`.
- Keep commits scoped: when `./run.sh format`/csharpier reformats files outside your change, revert those formatter-only edits before committing.
- Commit only when the user asked for one, and only after reviewing your own diff. The wrap-up flow is then: commit, open a PR with `gh pr create`, merge it, and delete branches already merged — `master` is protected, so a direct push to it is rejected.

## Rules hygiene

These rules are read by every agent session, so they stay high-signal. When you discover a non-obvious pattern worth keeping, put it under a **"Suggested rule additions"** heading in your wrap-up summary and let the user decide; the criteria and the review flow are in `docs/agents/rules-hygiene.md`.
