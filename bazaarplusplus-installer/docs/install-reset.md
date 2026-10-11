# Install And Reset

## State And Planning

- `InstallState` in `src-tauri/src/services/install/types.rs` is the native/frontend contract for selected paths, game and mod readiness, warnings, resettable data, and action gates.
- `install_state_from_snapshot` in `src-tauri/src/services/install/mod.rs` derives the contract from one `InstallEnvironmentSnapshot`; `detect_for_install` in `src-tauri/src/services/detect/mod.rs` refreshes platform bootstrap facts on every detection.
- `DefaultInstallWorkflow` in `src/features/install/installWorkflow.ts` owns stale-response rejection, single-flight operations, fixed confirmation targets, retry, and the one primary action. An installed but non-ready state routes to Repair.
- Warnings and failures cross the boundary as stable codes and parameters. `presentInstallWarning`, `presentInstallProblem`, and `presentInstallNotice` in `src/features/install/installProblems.ts` own localized presentation.

## Install And Uninstall

- `plan_install` in `src-tauri/src/services/install/plan.rs` computes effects; `execute_and_refresh` in `src-tauri/src/services/install/operation.rs` stops on the first failed effect and returns a freshly detected final state.
- `prepare_install_target` in `src-tauri/src/services/bepinex/payload.rs` removes only BPP-owned files absent from the incoming payload and preserves BPP configuration. `extract_zip` in `src-tauri/src/services/bepinex/zip_archive.rs` skips byte-identical files and replaces differing files.
- `BPP_PRIVATE_RELATIVE_PATHS` and `BPP_BUNDLED_DEPENDENCY_RELATIVE_PATHS` in `src-tauri/src/services/bepinex/payload.rs` define payload ownership.
- `uninstall_bpp` in `src-tauri/src/services/bepinex/mod.rs` always removes private BPP files. It removes shared BepInEx and platform bootstrap state only when no third-party plugin or patcher remains. Uninstall preserves the BPP data root.

On macOS, `plan_install` keeps Steam running when detected launch options are empty; only non-empty launch options add Steam shutdown and cleanup. `ensure_game_stopped` checks the selected game process before install effects and again before trampoline replacement.

## Running-Game Refusal

`ensure_game_stopped` in `src-tauri/src/services/game_process.rs` is the fail-closed check every operation that rewrites or deletes game-directory files runs first: install, Reset, BepInEx reset, Legacy Root deletion, and later the V5 import. It refuses when the game is running and when the process list cannot be inspected. Each `GameStoppedOperation` reports its own semantic problem code (`install_blocked_by_game`, `reset_blocked_by_game`, and so on); an inspection failure carries its cause as the diagnostic. `SystemGameProcessProbe` matches `TheBazaar.exe` on Windows and, on macOS, an executable under the selected installation's `TheBazaar.app/Contents/MacOS/`, including the trampoline's `.orig`. The game has no build for other platforms, where the probe reports it stopped. Tests inject a `GameProcessProbe` instead.

## Data Maintenance Lock

`DataMaintenanceLock` in `src-tauri/src/services/data_maintenance.rs` serializes the installer's data-mutating operations: History cleanup, Reset, BepInEx reset, and Legacy Root deletion wait for one another rather than interleave. Reset also stops the OBS overlay through `StreamRuntime::exclusive_maintenance`; it always takes the data maintenance lock first and the stream lifecycle gate second, and nothing holding the lifecycle gate waits for the data lock, so the two cannot deadlock.

## Steam Launch Boundary

The installer launches The Bazaar through `launch_game_via_steam` in `src-tauri/src/services/install/mod.rs`, which opens the fixed Steam game URL. Detection resolves Steam installations through `detect_installation_paths` in `src-tauri/src/services/detect/steam.rs`; there is no alternate launch-mode state. The Steam-only product boundary lives in [ADR-0003](adr/0003-steam-only-launch.md).

## macOS Trampoline Invariant

- `install_trampoline` in `src-tauri/src/services/bepinex/trampoline.rs` preserves the Unity executable as `.orig`, installs the bundled Mach-O stub as `CFBundleExecutable`, signs the real executable with the required entitlements, seals the bundle, verifies it, and rolls back the layout if installation fails.
- `is_current_trampoline` in the same module requires the structural `.orig` layout, the same Mach-O build UUID as the bundled stub, and a successful deep, strict signature verification. The UUID remains stable when bundle signing rewrites signature bytes; Steam Verify, game updates, resource changes, or a new stub produce a repairable state.
- `normalize` in `src-tauri/src/services/bepinex/bundle_root.rs` moves the known duplicate `TheBazaar_ARM64.app` outside the application before signing. It checks bundle identity and matching engine, Mono, and boot configuration, preserves the latest contents in a SHA-256-addressed backup, and resumes interrupted moves and cleanup. Existing backups are checked before reuse or removal; unknown or modified contents block repair. The backup remains outside the application after repair and uninstall because restoring it at the bundle root invalidates the signature.
- `inspect_launch_options_for_steam` and `clear_launch_options_for_steam` in `src-tauri/src/services/vdf/launch_options.rs` require every direct The Bazaar `LaunchOptions` value to be empty across Steam accounts. Unreadable configuration blocks mutation.
- `remove_obsolete_macos_artifacts` in `src-tauri/src/services/bepinex/trampoline.rs` removes fixed non-canonical residue by name; those files never select behavior.
- `uninstall_trampoline` restores `.orig`; `uninstall_bpp` invokes it only when BPP is the last installed mod. `with_finalized_bundle` seals and verifies after all uninstall mutations, including when a removal fails or another mod retains the shared trampoline.

`compile_macos_trampoline_stub` in `src-tauri/build.rs` builds the bundled arm64 stub with the deployment target defined by `MACOS_TRAMPOLINE_DEPLOYMENT_TARGET` in `src-tauri/build_support.rs`. Release validation inspects that target before packaging. The rationale for the sole-bootstrap and empty-LaunchOptions choices lives in [ADR-0002](adr/0002-macos-launch-trampoline.md).

## Headless Developer Repair

`run_headless` in `src-tauri/src/lib.rs` dispatches CLI arguments before the
Tauri shell starts. `just installer::cli --help` documents the arguments;
`scripts/headless.mjs` runs that binary from source without a prepared Payload.

The repair command calls `repair_deployed_game` in
`src-tauri/src/services/bepinex/mod.rs`. It requires an installed macOS game
with Doorstop and reuses the trampoline service, including process checks,
backup normalization, signing, and rollback. It preserves the deployed mod
DLLs, user data, and the persistent stash format. Both repair calls in mod
deployment use this entry: before copying files and after the native plugin
changes the application bundle. It does not run the GUI install planner or
replace the Payload with bundled release files.

`tree_manifest` in `src-tauri/src/services/file_manifest.rs` supplies the same
sorted relative path and SHA-256 records to the CLI and acceptance tests.
Symlink records contain their targets without following them. Manifest output
belongs outside the game directory so it cannot include itself.

## Reset Local Data

Reset is the only installer operation that deletes the current Data Root, the directory `BAZAAR_DATA_DIRECTORY` in `src-tauri/src/config.rs` names under the game directory. The name must equal the mod's `PathConstants.DataRootDirectoryName`: a test in `config.rs` pins it to `dataRootDirectoryName` in `src-tauri/history-database-compatibility.json`, and `just release::check` compares that field with the mod's history database contract. The confirmation copy in `src/i18n/messages.ts` names the folder literally and changes with it. `reset_bpp_data` in `src-tauri/src/services/bepinex/mod.rs` holds the data maintenance lock with the OBS overlay stopped, runs the running-game refusal on every platform, and only then delegates filesystem cleanup to `cleanup_bpp_data_directory` in `src-tauri/src/services/bepinex/payload.rs`. `reset_bepinex_folder` holds the same lock and runs the same refusal, but leaves the overlay running because it touches no database.

The Install workflow fixes the target path when confirmation opens. A successful `ResetBppDataResult` installs the returned refreshed state and distinguishes removed data from an already-empty target; a failure retains the target for retry. The durable product boundary is recorded in [ADR-0008](adr/0008-legacy-data-roots-and-v5-import.md).

## Legacy Roots

A Legacy Root (root `CONTEXT.md`) is user-owned. `LegacyRoot::ALL` in `src-tauri/src/services/legacy_data.rs` is the installer's only list of their names and never includes `BAZAAR_DATA_DIRECTORY`. Reset and uninstall never touch one, and nothing deletes one without the user confirming that root, including a successful V5 import.

The Install page lists them, not History: History resolves its game directory only where the current database exists, which a user with only V5 data does not have. `get_legacy_data_state` resolves the selected directory with `GamePathAcceptance::Any` and returns `LegacyDataState`:

- The Legacy Roots that exist as real directories directly under the game directory. A file or symbolic link with a root's name is not one, and the installer's own `BazaarPlusPlusV4` settings folder (`legacy_overlay_settings_path`) is never one. Sizes are measured on the blocking pool without following symbolic links. Files with more than one hard link are counted separately, because deleting the root frees their space only when no other link remains; the page therefore shows the size without promising what deletion reclaims.
- `installed_mod_data_root`: `current` only when `dataRootDirectoryName` in the installed mod's `BazaarPlusPlus.history-database.json` (under `BepInEx/plugins/`) equals `BAZAAR_DATA_DIRECTORY`. A missing file, a 5.x file without the field, corrupt JSON, or another name reads as `reinstall_required`, and the page asks an installed user to reinstall.
- `v5_import_eligible` from `is_v5_import_eligible` in `src-tauri/src/v5_import/mod.rs`: the installed mod writes the current Data Root, `BazaarPlusPlusV5/` exists, and it holds no `V5_IMPORT_MARKER_FILE_NAME` marker.

`delete_legacy_root` accepts only a name from `LegacyRoot::ALL`. It holds the data maintenance lock, runs the running-game refusal (`legacy_delete_blocked_by_game`), and removes the directory with `remove_dir_with_retry`, the removal Reset uses. `DeleteLegacyRootResult` distinguishes a removed root from one already gone and carries the remeasured state; undeleted paths return `install_partial_failure` with operation `delete_legacy_root`. `createLegacyDataWorkflow` in `src/features/install/legacyDataWorkflow.ts` fixes each deletion to one listed root through the confirmed-operation seam.

## Install and Repair Acceptance

`just installer::acceptance` runs two ignored macOS tests. It requires `BPP_ACCEPTANCE_RESOURCE_DIR`, a packaged resource directory (`BepInExSource/BepInEx.zip` and `Trampoline/bpp_launcher`), and `BPP_TEST_GAME_ROOT`, an installed game.

- `fresh_install_writes_payload_and_signed_trampoline_without_quitting_steam` in `src-tauri/src/services/install/operation.rs` creates a disposable macOS bundle and Steam config, then executes the production filesystem effects. It checks every payload file byte for byte and checks that the Steam config is unchanged. A shutdown effect fails the test before it can reach Steam.
- `copied_steam_bundle_repair_acceptance` in `src-tauri/src/services/bepinex/trampoline.rs` clones `TheBazaar.app` and Doorstop from the game root, repairs through the service and CLI repeatedly, verifies the manifest and preservation of developer DLLs and user data, then uninstalls the trampoline. The source is never modified.

Each test compares a committed golden:

- `src-tauri/tests/goldens/acceptance/installed-tree.json` holds the installed path set.
- `repaired-tree.json` holds the Payload Inventory paths present after repair.

Both goldens also record whether the trampoline UUID matches the bundled one and whether `codesign --verify --deep --strict` passed. Signatures change on every signing, and the copied game differs between machines, so file hashes stay out of the goldens. Each run instead writes its full `(path, sha256)` list to `src-tauri/target/acceptance/`, where two runs over the same inputs can be diffed. `BPP_UPDATE_GOLDENS=1` rewrites the goldens.

The fresh bundle contains a fixture executable, so these tests verify installation and signing, not game startup or BPP initialization.
