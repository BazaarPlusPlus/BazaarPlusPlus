# Android Host

`BazaarPlusPlus.Android` (`net10.0`) is the Android Host (root `CONTEXT.md`, Host). It is a MelonMod (`src/BazaarPlusPlus.Android/Host/AndroidMod.cs`) compiled against the Interop Snapshot and the Loader Release (`build/InteropPath.props`), never against `Managed/`.

## Shape

- `Host/` owns the loader entry, the `BppLog` exit over MelonLogger (`MelonLogSink`, scope `Android`), the settings store over `MelonLoader/UserData/BazaarPlusPlus.cfg` (`IniSettingsStore`) and the Data Root (`AndroidPaths`): `BazaarPlusPlusV6` beside the MelonLoader base directory.
- `AndroidComposition` is the record chain on that Data Root: Core's `RunLifecycleModule` and `RunLoggingModule` over the IL2CPP probes and run signals in `GameInterop/`, and `AndroidPvpBattleCapture`, which frames each PvP battle from the net messages (opening `PVPCombat` game sim, combat sim, next game sim) and saves it through Storage's `PvpBattleSqliteStore`. Board captures and the Replay Payload are not taken yet (#360), so an Android battle has no `battle_snapshots` row.
- `Patches/` install per class under the `[Android] PatchMode` setting (`enabled`, `observe`, `disabled`). A class that fails to install degrades only itself. `NetMessageReceivePatch` is the message intake (run initialization, battle capture); `CombatSimulatePatch` publishes `CombatSimObserved`.
- Shared code comes from Core's `net10.0` output, with Storage and ModApi through it; the host links no Core or desktop source. `just mod::android-deploy` pushes the build to the `melonloader` paths of the Payload Inventory (root `CONTEXT.md`), read through the generated `Payload.targets`: BPP's assemblies into `MelonLoader/Mods/`, third-party ones into `MelonLoader/UserLibs/`, the Updater into `MelonLoader/Plugins/` ([self-update.md](self-update.md)).

## Build and test

`tests/AndroidHost.Tests` fails if the compiled host references BepInEx or a desktop assembly, or defines a BPP type outside `BazaarPlusPlus.Android` (a linked source). It runs in `just mod::test-core`, the suites that need no Managed snapshot, and in the `mod-android` CI lane, with `tests/RunLoggingPipeline.Net10.Tests`: the Run Logging anchor on the `net10.0` binding this host ships, which must reproduce the desktop binding's history-database goldens byte for byte. `just mod::android-*` drives one device; `just mod::android-smoke <mode>` asserts injection, hook hit, and feature on it, and the self-hosted `mod-android-smoke` lane runs it nightly ([development guide](../../../docs/development.md#android-冒烟)).

## Gotchas

- SQLite's native `libe_sqlite3.so` must sit in the APK's `lib/arm64-v8a/` (the Android release's injection step adds it). External storage is `noexec`, so `UserLibs/` cannot carry it; a deploy to an APK without it fails at the first store open.
- A `T?` an IL2CPP getter returns arrives as an `Il2CppSystem.Nullable` proxy over a boxed `T`, whose `HasValue` and `Value` are wrong: every player attribute read as 0 and opponent ranks came out shifted. Read the dictionary or field behind it, or go through `AndroidGameIds.Unboxed`.
- Every Android store opens the database with `AndroidComposition.DatabaseOptions` (`DELETE`): one self-contained file that `adb pull` reads whole. WAL opened, wrote, and read back on the emulator's FUSE Data Root, but that is one process on one device, not a reason to switch.
- The startup `android.build_channel.probe` logs `server_option=missing` on every online build, the Online answer: as on desktop, only the PTR fork of `Config` declares `ServerOption`. It does not show whether Il2CppInterop exposes the nested type; only a PTR or staging Android capture can.
