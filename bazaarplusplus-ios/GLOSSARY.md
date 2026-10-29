# Glossary

**Launcher**: the native SwiftUI App in `app/`: game page, Steam account, onboarding. It owns the window until the engine takes over.

**Service**: the C# Steam code in `service/Managed/`, running in the runtime's Mono domain. It signs in, downloads, prepares the game and issues tickets, and owns all launcher state.

**Snapshot**: the JSON state the service returns from `bpp_steam_snapshot`. The launcher renders it and sends commands back; it holds no state of its own beyond view state.

**Runtime**: `UnityFramework.framework`: the iOS Unity engine, static Mono with corlib AOT, the IL2CPP bridge and the middleware adapters.

**Shell**: the Unity project in `runtime/engine/shell/` (`BppShell`) whose iOS export supplies the engine and UnityFramework target. It contains no game.

**Game definition**: `game/game.json`, the single source of the game's tested version, display data, Steam App ID, depots, platform admission and managed adapters. Packaged as `game.json`.

**Content**: the game's downloaded original files, under `Games/content/<app-id>` in the sandbox. Never modified.

**Game runtime directory**: the derived copy the game runs from, under `Games/runtimes/<game-id>`, rebuilt on every prepare from content, managed adapters, the helper and the mod.

**Managed adapter**: a hash-pinned patch to one of the game's assemblies, applied to the game runtime directory only.

**Helper**: `BazaarPlusPlus.Ios.Helper.dll`, injected into the game runtime directory for self-tests and diagnostics.

**Mods**: the folder of managed files the App ships (`build/mods/`, staged by `tools/stage-mod.py`): the BazaarPlusPlus assemblies and the Harmony runtime. Prepare copies them next to the game's assemblies.

**iOS host**: the BazaarPlusPlus Mono host in `bazaarplusplus-mod/src/BazaarPlusPlus/Host/Ios/`. The game's RuntimeInitializeOnLoads start it; BepInEx.dll ships only so the desktop adapter type loads.

**Method redirect**: Mono patch 0008 (`mono_interp_redirect_method`): the interpreter runs a replacement method's code in place of the original. The iOS host's Harmony backend applies every patch through it, because the device cannot write native code.

**Provider**: the in-process source of Steam identity and Web API tickets, installed with `bpp_steam_set_provider`. The only source; there is no desktop fallback.

**Preflight**: the native self-test (`middleware/preflight.cpp`) of SQLite, Alembic and BC decoding that runs before the service starts.

**Test channel**: the opt-in Simulator socket (`--test-control`) that `tools/app-control.py` drives. Never in device or normal builds.
