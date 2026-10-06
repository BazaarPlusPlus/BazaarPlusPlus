# AGENTS.md

The BepInEx mod. Repo-wide rules are in `../AGENTS.md`; this file holds what is specific to the mod and points at everything else.

| When you are about to | Read |
|---|---|
| Touch any code | `docs/MEMORY.md` |
| Change a feature | its `docs/architecture/` doc, Gotchas included; `docs/ARCHITECTURE.md` routes by feature |
| Name a domain concept | `CONTEXT.md` |
| Find who owns or constructs something | `docs/ARCHITECTURE.md` |
| Reopen a settled decision | `docs/adr/` |
| Add, move, or delete a document | `docs/README.md` |

## Build and test

`just --list mod` lists every command; recipes live in `mod.just` and run `scripts/*.sh`. Deploy only through `just mod::build --deploy`: a raw `dotnet build` skips the macOS trampoline repair every game update needs. Game assemblies resolve via `ManagedPath` from the Snapshot Lock (`build/ManagedPath.props`, root `CONTEXT.md`); override with `-p:ManagedPath=...`, and `just mod::fetch <platform> <channel>` explains a mismatch.

What `just --list mod` cannot tell you:

- `just mod::locks-check` runs a locked restore so NuGet graph drift fails in the mod, not later in the installer build.
- Doc edits are gated by `just mod::test`: `tests/Architecture.Tests/DocsHygieneTests.cs` enforces byte budgets on this file and `docs/MEMORY.md` and checks that links resolve; stay under a budget by merging entries, not appending.
- A deletion is proved by deleting: `just mod::build` and `just mod::test` must both pass, because test projects compile fakes and source-shadow capsules that `build` never touches.

## Logs and debugging

Runtime console output goes to `<GameDir>/BepInEx/LogOutput.log`, the sibling of the `BepInEx/plugins/` folder the build copies into. Mod log lines are structured events shaped `[BPP][<Scope>] event=<id> field=value ...`. `Debug` events emit only from Debug builds; `Info`, `Warning`, and `Error` always emit.

Runtime validation that needs the game running launches The Bazaar through Steam (App ID 1617400) so Steam runtime state is present: `open "steam://run/1617400"` on macOS, `start steam://run/1617400` on Windows. Launching `TheBazaar.app` directly, or via `run_bepinex.sh` on macOS, bypasses that state and fails in subtle ways. A long-running automation task relaunches the game through the same Steam URL after a crash or exit and continues until the goal is met.

## Where new code goes

The layering traps, not a map of what exists:

- Reusable adapters over The Bazaar and Unity runtime surfaces go in `GameInterop/`. Feature workflows, UI state, product policy, filtering and classification rules, upload decisions, and storage orchestration go in `Game/`, even when they mention a game enum or DTO.
- When two features need the same runtime, prefab, or static-data behavior, extract the adapter to `GameInterop/<Concept>/` and have both consume that seam instead of one importing the other's internals.
- Patches reach feature services only through `BppPatchHost`, the static service locator. Shared Harmony reflection helpers and native runtime adapters live in `GameInterop/` or `Infrastructure/`, outside any feature directory.
- A boundary the compiler cannot enforce gets an architecture test over compiled artifacts in the same change (ADR-0009).

## Game behavior

- The current repo code and `decompiled/` are the source of truth for game behavior and APIs. `decompiled/` is local only (generate it with `just mod::decompile <channel>`) and stays unedited; re-check `docs/` claims against live code.
- Root-cause game-behavior bugs against the decompiled game source before forming a hypothesis. Ground every conclusion in `file:line` citations, and when the obvious fix fails, enumerate alternative cause mechanisms before writing another patch.
- When the user says a problem has failed repeatedly, stop reading implementation and decompiled source and first open or update a GitHub issue recording the background, the current problem, candidate approaches, and the verification method.
- Validate a hypothesis with a temporary probe on the main path (the user builds and reloads to verify), or record it as a to-verify item on that issue and ship. Delete a probe in the commit that acts on its measurement.
