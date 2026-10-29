# AGENTS.md

BazaarPlusPlus for iOS runs The Bazaar's Mac build on iOS with the BazaarPlusPlus mod loaded. Read `docs/architecture.md` before changing a seam between `app/`, `service/`, `runtime/`, `middleware/`, `game/` and the mod. Name things with the terms in `GLOSSARY.md`.

## Where a change goes

- Facts about the game build (version, depots, plugins, platform admission, managed adapters) live in `game/game.json`. Runtime and middleware code works by plugin ABI and Unity build, not by game name.
- Mod features belong in `bazaarplusplus-mod/`, not here. The iOS host is `bazaarplusplus-mod/src/BazaarPlusPlus/Host/Ios/`; this project only builds, stages and loads it (`tools/stage-mod.py`, `runtime/helper/entrypoints.json`).
- A build script lives next to the module it builds. `tools/build.py` is the single entry point and calls each one in order; a new build step joins that sequence.
- Change names, formats and layouts directly. Ship the new version only, with no compatibility shims, version suffixes or migrations.
- A change to `runtime/mono/patches/` lands with the rebuilt Mono: `just ios::check`, then `bash runtime/mono/build.sh` for `host-aot`, `simulator-aot` and `device`. Patch numbers are never reused.
- The Unity shell's scene stays as it is: the engine classes it uses decide which classes the export registers, and the game relies on them. After `runtime/engine/export.py`, revert Unity's `iPhoneSdkVersion` rewrite in `ProjectSettings.asset`.

## Verification

A change to the service, runtime, middleware, packaging or the iOS host is done when the simulator regression passes, not when it compiles:

1. `python3 tools/build.py simulator --test-control`.
2. `just ios::regress`: signs in from the saved login, downloads until `ready`, launches, and fails unless the game log shows `RunTasks complete … Failed: 0`.
3. For a mod change, the log also shows `plugin.initialization.succeeded` and no `plugin.patches.degraded`.

Also run `just ios::test`. For device-side changes, `python3 tools/build.py device` then `just ios::audit-device`.

Moving a directory leaves stale caches with absolute paths behind: delete `build/mono-*-obj`, `build/alembic-*/cmake`, the Xcode `derived` folders under `build/steam-app/`, and the `bin/` and `obj/` folders of moved .NET projects before rebuilding.

## Gotchas

- Simulator builds stay ad hoc signed. A development team makes Xcode replace `app/Resources/simulator.entitlements`, which moves the keychain access group and loses the saved login.
- Changing the bundle id (`tools/generate-xcodeproj.py::BUNDLE_ID`) starts a new sandbox and keychain: the saved login and the downloaded game stay behind.
- The user types Steam passwords and approves sign-ins on their phone. Start a QR or password sign-in and wait for them.
- Game logs can hold tickets and account data. Quote only the lines you need; never commit or share raw logs.
