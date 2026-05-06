<div align="center">

# BazaarPlusPlus

**Born of Passion** · A BepInEx mod and desktop installer for [*The Bazaar*](https://www.playthebazaar.com)

[中文](README.md) · [Website](https://bazaarplusplus.com) · [Changelog](https://bazaarplusplus.com/changelog) · [Ko-fi](https://ko-fi.com/cauyxy)

[![Version](https://img.shields.io/badge/version-3.3.0-6dd9a0?style=flat-square)](https://bazaarplusplus.com/changelog)
[![License](https://img.shields.io/badge/license-MIT-e8c87a?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS-c1875a?style=flat-square)](https://bazaarplusplus.com/install)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.x-8a6d3b?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1-512bd4?style=flat-square)](https://learn.microsoft.com/dotnet/standard/net-standard)
[![Tauri](https://img.shields.io/badge/Tauri-2.x-24c8d8?style=flat-square)](https://tauri.app)

</div>

---

BazaarPlusPlus layers combat UI enhancements, opponent and tooltip previews, a history panel, and combat replay on top of *The Bazaar*. The companion desktop installer auto-detects the Steam install, deploys BepInEx and the mod, exposes an OBS overlay, and ships an auto-updater.

> The bulk of the codebase is led by [Codex](https://openai.com/codex), with [Claude Code](https://claude.com/product/claude-code) contributing in collaboration.

## Quick Start

The easiest way to install is to grab the desktop installer from [bazaarplusplus.com/install](https://bazaarplusplus.com/install):

1. Launch the installer — it auto-detects *The Bazaar* under the Steam install directory.
2. Click **Install**. The installer deploys BepInEx and BazaarPlusPlus, and patches Steam launch options.
3. Start the game. The installer keeps itself up to date through its built-in updater.

To build manually, see [Building from source](#building-from-source).

## Features

### Mod (`bazaarplusplus-mod`)

- **Combat status bar** — extra runtime stats overlaid during a fight.
- **Tooltip enhancements** — merges the dual-tooltip layout, adds enchant text, and supports upgrade previews.
- **History panel & Ghost Battle Replay** — browse past matches and replay any saved battle; the panel supports Chinese and can be toggled with F8.
- **Legendary rank display** — customize how your legendary-tier ranking is shown (hide it, show `#position | rating`, or use a stream-friendly placeholder).
- **Customizable hotkeys** — bind mod actions to keyboard or mouse buttons.
- **Random hero pool filter** — exclude specific heroes from random hero selection.
- **Localization & settings dock** — built-in Simplified Chinese support and a dedicated settings drawer.

> Step-by-step usage guides for each feature live at [bazaarplusplus.com/tutorial](https://bazaarplusplus.com/tutorial?lang=en).

### Installer (`bazaarplusplus-installer`)

- **Cross-platform one-click install** — Windows and macOS, with automatic Steam path detection.
- **Repair, uninstall, reset history** — recover to a clean state when something goes wrong.
- **Stream Mode (OBS overlay)** — spins up a localhost browser source you can drop into OBS.
- **Auto-update** — built on Tauri Updater; checks at launch and prompts when a new release is available.
- **Bilingual UI (English / 简体中文)** — follows system language, switchable at any time.

## Repository Layout

```
.
├── bazaarplusplus-mod/         # BepInEx plugin (C# / .NET Standard 2.1)
│   ├── BazaarPlusPlus.csproj   # Main project; Debug copies into the game's BepInEx/plugins
│   ├── Plugin.cs               # Mod entry point; wires every Game submodule
│   ├── Core/ Infrastructure/   # Config, logging, runtime plumbing
│   ├── Game/                   # Feature modules (CombatReplay, HistoryPanel, Tooltips ...)
│   ├── Patches/                # Harmony patches
│   └── Models/ Data/           # Data structures and embedded resources
└── bazaarplusplus-installer/   # Desktop installer (Tauri + SvelteKit + TypeScript)
    ├── src/                    # SvelteKit frontend: routes, components, i18n
    ├── src-tauri/              # Rust backend: env detection, filesystem ops, Tauri commands
    ├── scripts/                # Build helpers (binding generation, version sync, ...)
    └── build.sh                # One-shot packaging script
```

## Building from source

### Prerequisites

- **Mod**: .NET SDK 6+ / 8+, plus a local Steam install of *The Bazaar* (used to resolve game assemblies).
- **Installer**: Node.js 20+, the Rust toolchain, and the system dependencies listed in the [Tauri prerequisites](https://tauri.app/start/prerequisites/).
- **Windows**: the build scripts require PowerShell 7.6.0 or newer.

### Build the mod

```bash
cd bazaarplusplus-mod

# Debug: copy the freshly-built DLL straight into the local game's BepInEx/plugins
dotnet build -c Debug

# Release: bundle resources and write into the installer's BepInExSource
dotnet build -c Release

# Build Debug + Release in one go
dotnet msbuild -t:BuildAll
```

If auto-detection picks the wrong game directory, override `ManagedPath` explicitly:

```bash
dotnet build -c Debug -p:ManagedPath="<Steam>/steamapps/common/The Bazaar/.../Managed"
```

### Build the installer

```bash
cd bazaarplusplus-installer

npm install
npm run dev        # SvelteKit dev server (no Tauri runtime)
npm run tauri dev  # full Tauri desktop app

# Verify / test / format
npm run check
npm run test
npm run format

# Production package
./build.sh --prod
```

## Derivative Work Notice

If you plan to build on top of this project or release derivative mods, make sure your work complies with *The Bazaar* official Mod Policy:

[The Bazaar Mod Policy](https://www.playthebazaar.com/mod-policy)

## Acknowledgements

- **Inspiration**: [BazaarHelper](https://github.com/Duangi/BazaarHelper), [BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)
- **Data reference**: [bazaardb.gg](https://bazaardb.gg)
- **Runtime dependencies**: [BepInEx](https://github.com/BepInEx/BepInEx), [Harmony](https://github.com/pardeike/Harmony), [Tauri](https://tauri.app), [SvelteKit](https://kit.svelte.dev)
- **Co-creators**: [Codex](https://openai.com/codex), [Claude Code](https://claude.com/product/claude-code)

## Supporters

Thanks to everyone who supports BazaarPlusPlus — your help keeps the project maintained, improved, and publicly available.

Full supporter list: [bazaarplusplus.com/support](https://bazaarplusplus.com/support?lang=en)

Thanks as well to everyone who contributed without leaving a name. If you'd like to support the project, head to [Ko-fi](https://ko-fi.com/cauyxy) or check the in-app sponsor options.

## License

Released under the [MIT License](LICENSE).
