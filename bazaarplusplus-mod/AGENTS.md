# AGENTS.md

The BepInEx mod. Repo-wide rules (commits, pull requests, docs policy, contracts) are in `../AGENTS.md`; this file holds what is specific to the mod and points at everything else.

| When you are about to | Read |
|---|---|
| Touch any code | `docs/MEMORY.md` |
| Name a domain concept | `CONTEXT.md` |
| Find who owns or constructs something | `docs/ARCHITECTURE.md` |
| Reopen a settled decision | `docs/adr/` |
| Add, move, or delete a document | `docs/README.md` |

## Build and test

`./run.sh` with no arguments lists every subcommand. Build through it: it repairs the macOS trampoline after every game update, which a raw `dotnet build` skips. Game assemblies resolve via `ManagedPath`, auto-detected from common Steam install paths (`build/ManagedPath.props`) or passed as `-p:ManagedPath=...`.

What `run.sh` cannot tell you:

- `ScenarioRunner.Tests` owns the closed list of source-shadow executable capsules and runs each in a child process; use `dotnet run --project tests/<Name>/<Name>.csproj` only to diagnose one capsule directly.
- Review every changed `packages.lock.json` between `./run.sh restore-locks` and `./run.sh restore-locked`. The locked restore makes graph drift fail here rather than in the installer build.
- In an isolated worktree, pass `-p:BPPInstallerSourcePath="<absolute-path>/bazaarplusplus-installer/src-tauri/resources"` to projects referencing the main mod; the default sibling installer path does not exist beside a worktree.
- A deletion is proved by deleting: `./run.sh build` and `./run.sh test` must both pass, because test projects compile fakes and source-shadow capsules that `build` never touches.

## Logs and debugging

Runtime console output goes to `<GameDir>/BepInEx/LogOutput.log`, the sibling of the `BepInEx/plugins/` folder the build copies into. Mod log lines are structured events shaped `[BPP][<Scope>] event=<id> field=value ...`. `Debug` events emit only from Debug builds; `Info`, `Warning`, and `Error` always emit.

Runtime validation that needs the game running launches The Bazaar through Steam (App ID 1617400) so Steam runtime state is present: `open "steam://run/1617400"` on macOS, `start steam://run/1617400` on Windows. Launching `TheBazaar.app` directly, or via `run_bepinex.sh` on macOS, bypasses that state and fails in subtle ways.

## Where new code goes

The layering traps, not a map of what exists:

- Reusable adapters over The Bazaar and Unity runtime surfaces go in `GameInterop/`. Feature workflows, UI state, product policy, filtering and classification rules, upload decisions, and storage orchestration go in `Game/`, even when they mention a game enum or DTO.
- When two features need the same runtime, prefab, or static-data behavior, extract the adapter to `GameInterop/<Concept>/` and have both consume that seam instead of one importing the other's internals.
- Patches reach feature services only through `BppPatchHost`, the static service locator. Shared Harmony reflection helpers and native runtime adapters live in `GameInterop/` or `Infrastructure/`, outside any feature directory.
- A boundary the compiler cannot enforce gets an architecture test over compiled artifacts in the same change (ADR-0009).

## Game behavior

- The current repo code and `decompiled/` are the source of truth. Read `decompiled/` for game behavior and APIs and leave it unedited; design docs may be stale, so re-check them against live code.
- Root-cause game-behavior bugs against the decompiled game source before forming a hypothesis. Ground every conclusion in `file:line` citations, and when the obvious fix fails, enumerate alternative cause mechanisms before writing another patch.
- When the user says a problem has failed repeatedly, stop reading implementation and decompiled source and first write a doc capturing background, the current problem, candidate approaches, and the verification method.
- Validate a hypothesis with a temporary probe on the main path (the user builds and reloads to verify), or record it as a to-verify item in the design doc and ship. Delete a probe in the commit that acts on its measurement.
- A long-running automation task self-heals: relaunch the game process on crash or exit and continue until the goal is met.
