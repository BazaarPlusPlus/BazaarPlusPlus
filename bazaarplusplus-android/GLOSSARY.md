# Glossary

Cross-project terms (Android App, Game Code Hash, Interop Package, Launch Record, Compatibility Mode) are in the root `GLOSSARY.md`.

**Installed game**: the official `com.tempogames.thebazaar` as the package manager reports it: its base APK, split APKs, `versionCode` and `lastUpdateTime`. The App reads it and never writes to it.

**Game class loader**: the `PathClassLoader` the App builds over `bridge.dex` and every installed game APK, with the library links as its native search path. The game's Java code and the bridge Activity run in it.

**Bridge Activity**: `GameActivity` in `src/bridge/`, a subclass of the game's `UnityPlayerActivity` that the game class loader defines. `GameComponentFactory` instantiates it; it wraps its base Context in a Game context.

**Game context**: `GameContext`, the `ContextWrapper` that reports the game's package name, `ApplicationInfo` and resources while every writable directory stays in the App's sandbox.

**Library links**: the directory under the App's `no_backup/` that links each of the game's native libraries, and the App's own `libmain.so` and `libe_sqlite3.so`, into one search path for the game class loader.

**Android App Runtime**: what the App APK carries for LemonLoader: `assets/lemon-runtime.zip` (the Loader Release's assets and the Updater as the packaged deployment), `assets/bpp-runtime.json`, and the native libraries under `jniLibs/`. `release/android-app.mjs stage-runtime` writes it.

**Loader deployment state**: the `.lemonloader-*` markers and generated files under the App's `MelonLoader/` files directory. `LoaderDeployment` resets them when the App's `versionCode` or the interop changes, so LemonLoader deploys the new runtime instead of silently keeping the old one.

**Launch tier**: the outcome of `launch/LaunchGate` for the installed game against the Platform Release Manifest's `game`: `NORMAL`, `NEWER_COMPAT_AVAILABLE`, `NEWER_WAITING`, `OLDER`, `NOT_INSTALLED`, plus `APP_UPDATE_REQUIRED`, `OFFLINE_NO_CACHE` and `NO_RELEASE`. ADR 0012's table ("三档启动规则") names the first five; the launcher shows one per tier.

**Release cache**: `release/ReleaseCache`, the last Platform Release Manifest and Pre-Adaptation records that decoded, kept in internal storage (`files/release/`, `files/release-dev/` for a Development origin) for a launch without network.

**Staged version**: the Managed Payload version the App last put in the Data Root's `Updates/` (`files/managed-staged.txt`). When the installed Mods are still older and that `Updates/<version>/` is gone, the Updater rejected it, and the launcher says the last plugin install failed.

**Development origin**: a debug build's release source `http://127.0.0.1:<port>`, set by `files/dev/manifest-port` (`just android::dev-manifest-port`) and reached through `adb reverse`. It serves the public bucket's `installer/` layout; a debug build also trusts `files/dev/trusted-cert.der` (`just android::dev-signing-key`) for interop statements. Release builds have neither (`src/release/.../DevOverrides`).

**Development interop**: an Interop Package at `files/dev/interop.zip` in the debug App's internal storage, put there by `just android::push-interop`. While it exists, BPP launch uses it and skips the manifest, the Managed Payload and the Launch Record (a stale record is deleted), whatever the Development origin says.
