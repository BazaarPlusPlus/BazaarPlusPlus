# Glossary

Cross-project terms (Android App, Game Code Hash, Interop Package, Launch Record, Compatibility Mode) are in the root `GLOSSARY.md`.

**Installed game**: the official `com.tempogames.thebazaar` as the package manager reports it: its base APK, split APKs, `versionCode` and `lastUpdateTime`. The App reads it and never writes to it.

**Game class loader**: the `PathClassLoader` the App builds over `bridge.dex` and every installed game APK, with the library links as its native search path. The game's Java code and the bridge Activity run in it.

**Bridge Activity**: `GameActivity` in `src/bridge/`, a subclass of the game's `UnityPlayerActivity` that the game class loader defines. `GameComponentFactory` instantiates it; it wraps its base Context in a Game context.

**Game context**: `GameContext`, the `ContextWrapper` that reports the game's package name, `ApplicationInfo` and resources while every writable directory stays in the App's sandbox.

**Library links**: the directory under the App's `no_backup/` that links each of the game's native libraries, and the App's own `libmain.so` and `libe_sqlite3.so`, into one search path for the game class loader.

**Android App Runtime**: what the App APK carries for LemonLoader: `assets/lemon-runtime.zip` (the Loader Release's assets and the Updater as the packaged deployment), `assets/bpp-runtime.json`, and the native libraries under `jniLibs/`. `release/android-app.mjs stage-runtime` writes it.

**Loader deployment state**: the `.lemonloader-*` markers and generated files under the App's `MelonLoader/` files directory. `LoaderDeployment` resets them when the App's `versionCode` or the interop changes, so LemonLoader deploys the new runtime instead of silently keeping the old one.

**Development interop**: an Interop Package at `files/dev/interop.zip` in the debug App's internal storage, put there by `just android::push-interop`. It stands in for a downloaded one.
