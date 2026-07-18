# BppMacAudio — macOS CoreAudio process-tap capture (native)

The native half of Combat Replay's macOS audio capture. All CoreAudio interaction lives
here so the C# side stays a plain pull loop identical to the Windows WASAPI path.

C# consumer: [`src/BazaarPlusPlus/Game/CombatReplay/Audio/CoreAudioProcessTapCaptureTap.cs`](../../src/BazaarPlusPlus/Game/CombatReplay/Audio/CoreAudioProcessTapCaptureTap.cs)

## How it works

`BppMacAudio_Start` translates the current PID into a CoreAudio process object, creates a
private stereo-mixdown process tap on it, wraps the tap in a private aggregate device, and
installs an IOProc that pushes samples into a lock-free single-producer/single-consumer
FIFO (interleaving planar buffers on the fly). The consumer polls `BppMacAudio_Read` from
its own background thread; `BppMacAudio_Stop` tears everything down in reverse creation
order. The IOProc runs on a CoreAudio realtime thread and never allocates, locks, calls
ObjC/Foundation, logs, or blocks — and no CoreAudio realtime thread ever enters the Mono
runtime.

The process-tap APIs are a macOS 14.2+ feature; this module gates itself at macOS 15 via
`BppMacAudio_IsSupported` (NSProcessInfo product version) and weak-imports the tap symbols
so the dylib still loads and degrades cleanly on older systems.

## Files

| File | Role |
| --- | --- |
| `BppMacAudio.m` / `.h` | dylib source + C ABI (`BppMacAudio_IsSupported` / `_Start` / `_Read` / `_Stop`) |
| `build.sh` | builds `libBppMacAudio.dylib` and copies it into the installer repo |
| `libBppMacAudio.dylib` | build output — **gitignored here** (the committed copy lives in the installer repo) |

## Build

```bash
./build.sh
```

Requirements: macOS + Xcode / Command Line Tools SDK, Apple Silicon (arm64). The script
runs one `clang` command, then copies the dylib into the installer repo (see below).

Load-bearing `clang` flags — do not change without understanding why:

- `-arch arm64` — the only supported target (Apple Silicon).
- `-fobjc-arc` — ARC manages the ObjC objects (`CATapDescription`, the aggregate-device dict).
- `-framework CoreAudio -framework Foundation` — the tap APIs are in CoreAudio; the
  `NSProcessInfo` version gate is in Foundation.
- `-mmacosx-version-min=11.0` — **NOT 14.2.** This weak-imports the macOS 14.2 tap symbols so
  the dylib *loads* on macOS 11–14 and `IsSupported` cleanly returns 0 there; the tap symbols
  are only ever called after the in-dylib `>= 15` gate (`NSProcessInfo`). Bumping it to 14.2
  would make the dylib fail to load on older systems instead of degrading to a silent video.

## Where it ships (two-repo split)

The mod build **never reads the copy in this directory.** It reads the prebuilt from the
installer repo, referenced by `BazaarPlusPlus.csproj` via `BPPInstallerSourcePath`, exactly
like `libe_sqlite3.dylib`:

```
bazaarplusplus-installer/src-tauri/resources/SourceForBuild/macos/BepInEx/plugins/libBppMacAudio.dylib
```

`build.sh` auto-copies there. The default destination assumes the standard sibling-repo
workspace layout; override it for a non-standard layout:

```bash
BPP_INSTALLER_PLUGINS_DIR=/path/to/installer/.../BepInEx/plugins ./build.sh
```

If the installer dir is absent (e.g. a mod-only checkout) the copy is skipped with a note —
the dylib still builds locally and that is not an error.

> **After changing the native source, rebuild and commit the refreshed dylib in the installer
> repo** — it is a committed prebuilt, like `libe_sqlite3.dylib`; the mod repo carries source
> only. `clang` output is deterministic for a given toolchain/SDK, so unchanged source yields
> a byte-identical dylib and no installer diff; a toolchain update alone can change the bytes.

## Naming + packaging

- The artifact is **`libBppMacAudio.dylib`** (lib-prefixed) but C# binds
  `[DllImport("BppMacAudio")]`; Unity-Mono resolves it via its `lib{name}.dylib` probe — the
  same path that loads `libe_sqlite3.dylib` from `[DllImport("e_sqlite3")]`.
- The csproj mirrors sqlite: a **Debug-target** `<Copy>` into the game's `BepInEx/plugins/`,
  and **no Release-target `<Copy>`** — the committed dylib is packed by the Release
  `ZipDirectory` step.

## Verify

```bash
./build.sh
nm -gU libBppMacAudio.dylib | grep BppMacAudio   # expect the four _BppMacAudio_* exports
```

A bare-process `Start`/`Read`/`Stop` smoke test exercises the tap / aggregate-device / IOProc /
FIFO plumbing (it captures silence — a CLI process emits no audio). The real acceptance check
is `ffmpeg volumedetect` on an in-game recording.
