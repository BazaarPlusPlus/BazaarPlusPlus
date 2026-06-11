<div align="center">

# BazaarPlusPlus

**Born of Passion** · A BepInEx mod and desktop installer for [*The Bazaar*](https://www.playthebazaar.com)

[中文](README.md) · [Website](https://bazaarplusplus.com) · [Download](https://bazaarplusplus.com/download?lang=en) · [Tutorial](https://bazaarplusplus.com/tutorial?lang=en) · [Release Notes](https://github.com/cauyxy/BazaarPlusPlus/releases) · [Ko-fi](https://ko-fi.com/cauyxy)

[![Version](https://img.shields.io/badge/version-4.2.0-6dd9a0?style=flat-square)](https://bazaarplusplus.com)
[![License](https://img.shields.io/badge/license-MIT-e8c87a?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-c1875a?style=flat-square)](https://bazaarplusplus.com/download)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.x-8a6d3b?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1-512bd4?style=flat-square)](https://learn.microsoft.com/dotnet/standard/net-standard)
[![Tauri](https://img.shields.io/badge/Tauri-2.x-24c8d8?style=flat-square)](https://tauri.app)
[![React](https://img.shields.io/badge/React-19-61dafb?style=flat-square)](https://react.dev)

</div>

---

BazaarPlusPlus is an open-source project for *The Bazaar*. The in-game BepInEx mod adds a card collection browser, run history, combat replays, tooltip previews, anonymous mode, Chinese terminology, and related quality-of-life features. The companion desktop installer handles download, install, repair, auto-update, and the stream overlay.

Most players should install from [bazaarplusplus.com/download](https://bazaarplusplus.com/download?lang=en); this repository is for developers who want to inspect the implementation, contribute changes, or build locally.

> The bulk of the codebase is led by [Codex](https://openai.com/codex), with [Claude Code](https://claude.com/product/claude-code) contributing in collaboration.

## Quick Start

1. Open [bazaarplusplus.com/download](https://bazaarplusplus.com/download?lang=en) and choose the Windows `.exe` or macOS `.dmg`.
2. Close the game before running the installer. For updates, uninstall the old build before installing the new one.
3. Launch *The Bazaar* once after installation so BazaarPlusPlus can finish setup.
4. On the main menu, confirm that the **Card Collection** button appears and the footer version text includes `BPP version`.

Feature guides, hotkeys, and installation details live at [bazaarplusplus.com/tutorial](https://bazaarplusplus.com/tutorial?lang=en).

## Feature Overview

### In-Game Mod

- **Card Collection**: Browse items and skills in-game, with filters for hero, tier, size, merchant, and current run day.
- **BazaarDB Auto Upload**: Community-data contribution that uploads end-of-run screenshots and board data in the background. Disabled by default; opt-in only.
- **Run History and Combat Replay**: Press `F8` to browse past runs and key fights, and watch replays and ghost battles.
- **Combat Status Bar**: Shows combat time and pause state, with speed controls — handy for review, recording, and streaming.
- **Anonymous Mode**: Hide the local player name in screenshots, recordings, and streams.
- **Legendary Rank Display**: Hide your rank, show an exaggerated power value, or display rank and rating together.
- **Enchant and Upgrade Previews**: Preview post-enchant or post-upgrade item values directly in tooltips.
- **Chinese Terminology Modes**: Simplified Chinese plus Taiwan and Hong Kong Traditional terminology styles.

### Desktop Installer

- **Cross-platform install**: Windows and macOS, with automatic Steam game-directory detection.
- **Repair / uninstall / reset local data**: Recover from broken installs, replay-data issues, or local-state corruption.
- **Run history management**: View, locate, and clean up locally saved run records and replay videos.
- **Stream Mode**: Start a localhost browser-source service for OBS and similar tools.
- **Auto-update**: Uses Tauri Updater to check for new releases and prompt when available.

## Repository Layout

```
.
├── bazaarplusplus-mod/                       # BepInEx mod source
│   ├── run.sh                                # Common build/test/format/decompile entry point
│   └── src/
│       ├── BazaarPlusPlus/                   # Main mod: Game, Patches, Resources, Data
│       ├── BazaarPlusPlus.ModApi/            # HTTP client for the mod backend
│       ├── BazaarPlusPlus.Storage/           # Local run logs, screenshots, and SQLite storage
│       └── BazaarPlusPlus.Localization/      # Chinese terminology and localization engine
└── bazaarplusplus-installer/                 # Desktop installer
    ├── src/                                  # Vite + React frontend
    │   ├── pages/ features/ layouts/ api/    # Pages, feature state, shell, and Tauri calls
    │   └── types/generated/                  # Rust -> TypeScript binding snapshot
    ├── src-tauri/                            # Tauri 2 / Rust backend
    │   ├── src/commands/ services/ history/  # Install, detect, history, and stream services
    │   └── resources/                        # BepInEx, FFmpeg, stream overlay, install payload
    ├── scripts/                              # Binding, manifest, and prebuild scripts
    └── build.sh                              # Local development and release packaging entry point
```

## Building From Source

### Prerequisites

- **Mod**: .NET SDK 8+ and a local Steam install of *The Bazaar* so game assemblies can be resolved.
- **Installer**: Node.js 20+, the Rust toolchain, and the system dependencies listed in the [Tauri prerequisites](https://tauri.app/start/prerequisites/).
- **Windows**: PowerShell 7.6.0 or newer for the build scripts and development flow.

### Build the Mod

```bash
cd bazaarplusplus-mod

# Development build: resolves the local game directory and copies the Debug DLL into BepInEx/plugins
./run.sh build

# Build Debug + Release in one pass
./run.sh all

# Override the game assembly directory explicitly
dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj \
  -c Debug \
  -p:ManagedPath="<Steam>/steamapps/common/The Bazaar/.../Managed"
```

### Build the Installer

```bash
cd bazaarplusplus-installer

npm install
npm run dev        # Vite frontend dev server
npm run tauri dev  # full Tauri desktop app

npm run check
npm run test
npm run format

./build.sh --prod  # production package for the host platform
```

Release signing, notarization, and R2 upload flows depend on local environment variables and `signing-secrets/`, which are intentionally not committed. A full release build also requires a local game install, signing material, and the platform dependencies — the public source tree alone is not enough.

## Derivative Work Notice

If you plan to build on top of this project or release derivative mods, make sure your work complies with *The Bazaar* official Mod Policy:

[The Bazaar Mod Policy](https://www.playthebazaar.com/mod-policy)

## Acknowledgements

- **Inspiration**: [BazaarHelper](https://github.com/Duangi/BazaarHelper), [BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)
- **Data reference**: [bazaardb.gg](https://bazaardb.gg)
- **Runtime dependencies**: [BepInEx](https://github.com/BepInEx/BepInEx), [Harmony](https://github.com/pardeike/Harmony), [Tauri](https://tauri.app), [React](https://react.dev), [Vite](https://vite.dev), [Tailwind CSS](https://tailwindcss.com), [FFmpeg](https://ffmpeg.org)
- **Font**: [LXGW WenKai](https://github.com/lxgw/LxgwWenKai) (SIL Open Font License 1.1)
- **Co-creators**: [Codex](https://openai.com/codex), [Claude Code](https://claude.com/product/claude-code)

## Supporters

Thanks to everyone who supports BazaarPlusPlus. The full supporter list lives at [bazaarplusplus.com/support](https://bazaarplusplus.com/support?lang=en).

If you would like to support continued maintenance, head to [Ko-fi](https://ko-fi.com/cauyxy) or check the in-app sponsor options.

## License

Released under the [MIT License](LICENSE).
