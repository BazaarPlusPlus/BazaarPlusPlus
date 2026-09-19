<div align="center">

# BazaarPlusPlus

**Born of Passion** · A BepInEx mod and desktop installer for [*The Bazaar*](https://www.playthebazaar.com)

[中文](README.md) · [Website](https://bazaarplusplus.com) · [Download](https://bazaarplusplus.com/download?lang=en) · [Tutorial](https://bazaarplusplus.com/tutorial?lang=en) · [Release Notes](https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases) · [Ko-fi](https://ko-fi.com/cauyxy)

[![Version](https://img.shields.io/badge/version-5.5.0-6dd9a0?style=flat-square)](https://bazaarplusplus.com)
[![License](https://img.shields.io/badge/license-MIT-e8c87a?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-c1875a?style=flat-square)](https://bazaarplusplus.com/download)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.x-8a6d3b?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1-512bd4?style=flat-square)](https://learn.microsoft.com/dotnet/standard/net-standard)
[![Tauri](https://img.shields.io/badge/Tauri-2.x-24c8d8?style=flat-square)](https://tauri.app)
[![React](https://img.shields.io/badge/React-19-61dafb?style=flat-square)](https://react.dev)

</div>

---

BazaarPlusPlus is an open-source project for *The Bazaar*. The in-game BepInEx mod adds a card collection browser, run history, combat replays, tooltip previews, anonymous mode, Chinese terminology, and related quality-of-life features. The companion desktop installer handles download, install, repair, auto-update, and the stream overlay. This repository also holds the upload backend, the metrics analyzer, and the public website.

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
├── JUSTFILE                                 # Unified development, checks, tests, and release commands
├── VERSION / release.mjs / release/         # Product version, release entry point, shared Payload Inventory
├── bazaarplusplus-mod/                       # BepInEx mod source
│   ├── mod.just / scripts/                   # just mod::… recipes and their build/test/decompile scripts
│   └── src/
│       ├── BazaarPlusPlus/                   # Main mod: Game, Patches, Resources, Data
│       ├── BazaarPlusPlus.ModApi/            # HTTP client for the mod backend
│       ├── BazaarPlusPlus.Storage/           # Local run logs, screenshots, and SQLite storage
│       └── BazaarPlusPlus.Localization/      # Chinese terminology and localization engine
├── bazaarplusplus-installer/                 # Desktop installer
│   ├── src/                                  # Vite + React frontend
│   ├── src-tauri/                            # Tauri 2 / Rust backend
│   └── build.sh                              # Local development and release packaging entry point
├── bazaarplusplus-server/                    # Cloudflare Worker: Bundle upload and Ghost discovery
├── bazaarplusplus-analyzer/                  # Turns Bundles into heroes / builds snapshots
└── bazaarplusplus-site/                      # bazaarplusplus.com
```

Run `just` from any repository subdirectory to list development, check, test, and release commands. Projects retain their native toolchains; product release rules live in the root `release.mjs`. See the [development command guide](docs/development.md) for setup and command scope. The root [`AGENTS.md`](AGENTS.md) records cross-project conventions and contract owners; each project also has its own `AGENTS.md`.

## Building From Source

### Prerequisites

- **Mod**: .NET SDK 10 and a local Steam install of *The Bazaar* so game assemblies can be resolved.
- **Unified commands**: [just](https://just.systems/man/en/packages.html); install with `brew install just` on macOS.
- **Installer / server / site**: Use the Node version in the root `.nvmrc` and the npm version in `packageManager` of `bazaarplusplus-installer/package.json`; run `npm ci` in each project directory.
- **Native installer builds**: The Rust toolchain and the system dependencies listed in the [Tauri prerequisites](https://tauri.app/start/prerequisites/).
- **Analyzer**: Python 3.14 and `uv`.
- **Windows**: Run just commands in Git Bash; native build scripts also require PowerShell 7.6.0 or newer.

### Build the Mod

```bash
# Compile without changing the installed game
just mod::build
just mod::test

# Override the game assembly directory
just mod::build "-p:ManagedPath=<Steam>/steamapps/common/The Bazaar/.../Managed"
```

To deploy development DLLs into the game, explicitly run `just mod::deploy`.

### Build the Installer

```bash
cd bazaarplusplus-installer

npm ci
just installer::dev # Vite frontend dev server
npm run tauri dev  # full Tauri desktop app

just installer::check
just installer::test
npm run format
```

Run `just fmt` from the root to format every project; `just hooks-install` installs the Git hooks defined in the root `lefthook.yml`.

```bash
cd bazaarplusplus-server
npm ci
just server::test
# just server::dev requires the project's gitignored .dev.vars
```

```bash
cd bazaarplusplus-analyzer
uv sync --locked
just analyzer::check
just analyzer::test
```

```bash
cd bazaarplusplus-site
npm ci
just site::test
just site::build
```

Release signing, notarization, and R2 upload flows depend on local environment variables and `signing-secrets/`, which are intentionally not committed. A full release build also requires a local game install, signing material, and the platform dependencies — the public source tree alone is not enough. Game decompilation output, `decompiled/`, `.env`, and `.dev.vars` are also kept out of this tree.

## Product Releases

The mod and installer share the root `VERSION`. Run `just release::sync` after changing it. Build each platform with `just release::build macos` (or `windows`), then run `just release::upload <platform>` for its immutable artifacts. `just release::promote` advances latest only when both platforms have the same version and Git commit. The underlying `node release.mjs …` commands remain available. See the [product release guide](docs/release.md) for credentials, sequencing, and recovery.

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
