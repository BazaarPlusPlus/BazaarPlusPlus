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

## Build

```bash
./build.sh
```

Requirements: macOS + Xcode / Command Line Tools SDK, Apple Silicon (arm64), and Node.js for
reading the shared native artifact catalog. The script builds into `build/` by default and runs
the architecture, deployment-target, weak-import, dependency, ABI export, load, and ad-hoc-signing
checks without writing the installer repository.

Every `clang` flag in `build.sh` is load-bearing, and the two whose reasons are not obvious from
the flag itself — the `lib` output prefix and `-mmacosx-version-min=12.0` — carry that reason in a
comment directly above the command. Read them there before changing the invocation.

Those are producer checks. The final acceptance judgement is not one of them: it remains a
sample-bearing AAC track with audible in-game audio in the finished recording.

## Where it ships (two-repo split)

The mod build **never reads the copy in this directory.** It reads the prebuilt from the
installer repo, referenced by `BazaarPlusPlus.csproj` via `BPPInstallerSourcePath`, exactly
like `libe_sqlite3.dylib`:

```
bazaarplusplus-installer/src-tauri/resources/SourceForBuild/macos/BepInEx/plugins/libBppMacAudio.dylib
```

`./run.sh publish` reuses that staged copy only when both the macOS input digest and the staged
artifacts still match the installer manifest; otherwise it rebuilds through this script and promotes
the result. The freshness contract and the full promotion sequence are in
[`docs/architecture/native-artifacts.md`](../../docs/architecture/native-artifacts.md). A direct
build may choose another side-effect-free output directory with its first argument:

```bash
./build.sh /absolute/output/directory
```

## Naming + packaging

- The artifact is **`libBppMacAudio.dylib`** (lib-prefixed) but C# binds
  `[DllImport("BppMacAudio")]`; Unity-Mono resolves it via its `lib{name}.dylib` probe — the
  same path that loads `libe_sqlite3.dylib` from `[DllImport("e_sqlite3")]`.
- The csproj mirrors sqlite: a **Debug-target** `<Copy>` into the game's `BepInEx/plugins/`,
  and **no Release-target native `<Copy>`** — the promoted dylib is packed by the installer-owned
  archive preparation step.
