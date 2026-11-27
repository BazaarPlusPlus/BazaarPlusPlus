# BazaarPlusPlus Android App

Android App-specific terms. Cross-project terms (Android App, Game Code Hash, Game Adaptation, Interop Package, Launch Record, Managed Payload, Android Source) are defined in [the root glossary](../GLOSSARY.md).

## Game loading

**Installed Game**:
The official `com.tempogames.thebazaar` as reported by Android's package manager: its base APK, split APKs, `versionCode`, `lastUpdateTime`, and signing certificate sha256s (which identify its Android Source). The App reads it and never writes to it.

**Game Class Loader**:
The `PathClassLoader` the App builds over `bridge.dex` and every Installed Game APK, with the Library Links directory as its native search path. The game's Java code and the Bridge Activity run in it.

**Bridge Activity**:
`GameActivity` in `src/bridge/`, a subclass of the game's `UnityPlayerActivity` defined by the Game Class Loader. `GameComponentFactory` instantiates it; it wraps its base Context in a Game Context.

**Game Context**:
`GameContext`, the `ContextWrapper` that reports the official game's package name, `ApplicationInfo`, and resources while keeping every writable directory inside the App's sandbox.

**Library Links**:
The directory under the App's `no_backup/` that symlinks each of the game's native libraries together with the App's `libmain.so` and `libe_sqlite3.so` into one native search path for the Game Class Loader.

## Runtime and staging

**Android App Runtime**:
What the App APK carries for LemonLoader: `assets/lemon-runtime.zip` (the Loader Release's assets and the Updater as the packaged deployment), `assets/bpp-runtime.json`, and the native libraries under `jniLibs/`. Written by `release/android-app.ts stage-runtime`.

**Carried Release**:
The release bundled inside a release APK so it can run without a download: `assets/bpp-release/<sha256>.zip` for every Game Adaptation's Interop Package and Managed Payload, plus `assets/bpp-release/release.json` (its Platform Release Manifest without `app`, `release/CarriedRelease`). A launch reuses a bundled file when its sha256 matches and falls back to the bundled manifest when no live manifest can be read.

**Loader Deployment State**:
The `.lemonloader-*` markers and generated files under the App's `MelonLoader/` files directory. `LoaderDeployment` resets them whenever the App's `versionCode` or the Interop Package changes so LemonLoader deploys the new runtime instead of silently reusing the old one.

**Launch Tier**:
The `launch/LaunchGate` decision for the Installed Game against the Platform Release Manifest's `adaptations`: `NORMAL`, `NEWER_WAITING`, `OLDER`, `NOT_INSTALLED`, `ANDROID_RELEASE_APK`, plus `OFFLINE_NO_CACHE`. A Game Code Hash matching any adaptation resolves to `NORMAL` with that adaptation; otherwise only the adaptation for the installed game's Android Source (by signer) is compared (root ADR 0019).

**Release Cache**:
`release/ReleaseCache`, the last decoded Platform Release Manifest stored in internal storage (`files/release/`, or `files/release-dev/` for a Development Origin) for offline launches.

**Staged Version**:
The Managed Payload version the App last staged into the Data Root's `Updates/` (`files/managed-staged.txt`). If the installed Mods are still older and `Updates/<version>/` is gone, the Updater rejected it and the launcher reports that the last plugin install failed.

**Staged Game**:
The Game Code Hash of the Game Adaptation whose Managed Payload the App last staged (`files/managed-staged-game.txt`). When the selected adaptation's game differs or none is recorded, launch removes every file listed in the installed Managed Payload Manifest plus the manifest itself and stages the adaptation's Managed Payload regardless of version, because the Updater compares versions only.

## Development overrides

**Development Origin**:
A debug build's local release source `http://127.0.0.1:<port>`, configured by `files/dev/manifest-port` (`just android::dev-manifest-port`) over `adb reverse`, serving the public bucket's `installer/` layout; debug builds also trust `files/dev/trusted-cert.der` (`just android::dev-signing-key`) for interop statements. Release builds have neither (`src/release/.../DevOverrides`).

**Development Interop**:
An Interop Package placed at `files/dev/interop.zip` in the debug App's internal storage by `just android::push-interop`. While present, BPP launch uses it and skips the manifest, Managed Payload staging, and Launch Record (deleting any stale record), overriding any Development Origin.
