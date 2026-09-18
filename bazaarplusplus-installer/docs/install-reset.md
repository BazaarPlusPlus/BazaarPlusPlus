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

On macOS, `plan_install` keeps Steam running when detected launch options are empty; only non-empty launch options add Steam shutdown and cleanup. `ensure_bazaar_stopped` in `src-tauri/src/services/game_process.rs` checks the selected game process before install effects and again before trampoline replacement, and fails closed when process inspection fails.

## Steam Launch Boundary

The installer launches The Bazaar through `launch_game_via_steam` in `src-tauri/src/services/install/mod.rs`, which opens the fixed Steam game URL. Detection resolves Steam installations through `detect_installation_paths` in `src-tauri/src/services/detect/steam.rs`; there is no alternate launch-mode state. The Steam-only product boundary lives in [ADR-003](adr/003-steam-only-launch.md).

## macOS Trampoline Invariant

- `install_trampoline` in `src-tauri/src/services/bepinex/trampoline.rs` preserves the Unity executable as `.orig`, installs the bundled Mach-O stub as `CFBundleExecutable`, signs the real executable with the required entitlements, seals the bundle, verifies it, and rolls back the layout if installation fails.
- `is_current_trampoline` in the same module requires the structural `.orig` layout, the same Mach-O build UUID as the bundled stub, and a successful deep, strict signature verification. The UUID remains stable when bundle signing rewrites signature bytes; Steam Verify, game updates, resource changes, or a new stub produce a repairable state.
- `normalize` in `src-tauri/src/services/bepinex/bundle_root.rs` moves the known duplicate `TheBazaar_ARM64.app` outside the application before signing. It checks bundle identity and matching engine, Mono, and boot configuration, preserves the latest contents in a SHA-256-addressed backup, and resumes interrupted moves and cleanup. Existing backups are checked before reuse or removal; unknown or modified contents block repair. The backup remains outside the application after repair and uninstall because restoring it at the bundle root invalidates the signature.
- `inspect_launch_options_for_steam` and `clear_launch_options_for_steam` in `src-tauri/src/services/vdf/launch_options.rs` require every direct The Bazaar `LaunchOptions` value to be empty across Steam accounts. Unreadable configuration blocks mutation.
- `remove_obsolete_macos_artifacts` in `src-tauri/src/services/bepinex/trampoline.rs` removes fixed non-canonical residue by name; those files never select behavior.
- `uninstall_trampoline` restores `.orig`; `uninstall_bpp` invokes it only when BPP is the last installed mod. `with_finalized_bundle` seals and verifies after all uninstall mutations, including when a removal fails or another mod retains the shared trampoline.

`compile_macos_trampoline_stub` in `src-tauri/build.rs` builds the bundled arm64 stub with the deployment target defined by `MACOS_TRAMPOLINE_DEPLOYMENT_TARGET` in `src-tauri/build_support.rs`. Release validation inspects that target before packaging. The rationale for the sole-bootstrap and empty-LaunchOptions choices lives in [ADR-002](adr/002-macos-launch-trampoline.md).

## Reset Local Data

Reset is the only installer operation that deletes the current BPP data root. `reset_bpp_data` in `src-tauri/src/services/bepinex/mod.rs` enters `StreamRuntime::exclusive_maintenance`, refuses deletion while the game is running, and delegates filesystem cleanup to `cleanup_bpp_data_directory` in `src-tauri/src/services/bepinex/payload.rs`.

The Install workflow fixes the target path when confirmation opens. A successful `ResetBppDataResult` installs the returned refreshed state and distinguishes removed data from an already-empty target; a failure retains the target for retry. The durable product boundary is recorded in [ADR-005](adr/005-data-ownership-and-reset.md).

## Isolated Fresh-Install Acceptance

The opt-in `fresh_install_writes_payload_and_signed_trampoline_without_quitting_steam` test in `src-tauri/src/services/install/operation.rs` creates a disposable macOS bundle and Steam config, then executes the production filesystem effects against a packaged resource directory supplied through `BPP_ACCEPTANCE_RESOURCE_DIR`. It checks every payload file, the trampoline UUID and deep signature, and unchanged Steam config. A shutdown effect fails the test before it can reach Steam. The bundle contains a fixture executable, so this verifies installation and signing, not game startup or BPP initialization.
