<div align="center">

<img src="bazaarplusplus-installer/src-tauri/icons/icon.png" alt="BazaarPlusPlus" width="128" height="128" />

# BazaarPlusPlus

**Born of Passion** · An in-game mod for [_The Bazaar_](https://www.playthebazaar.com) on Windows, macOS, and Android

[中文](README.md) · [Website](https://bazaarplusplus.com) · [Download](https://bazaarplusplus.com/download?lang=en) · [Tutorial](https://bazaarplusplus.com/tutorial?lang=en) · [Release Notes](https://github.com/BazaarPlusPlus/BazaarPlusPlus/releases) · [Ko-fi](https://ko-fi.com/cauyxy)

[![Version](https://img.shields.io/badge/version-6.0.0-6dd9a0?style=flat-square)](https://bazaarplusplus.com)
[![License](https://img.shields.io/badge/license-MIT-e8c87a?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Android-c1875a?style=flat-square)](https://bazaarplusplus.com/download)
[![BepInEx](https://img.shields.io/badge/BepInEx-5.x-8a6d3b?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![MelonLoader](https://img.shields.io/badge/MelonLoader-Android-8a6d3b?style=flat-square)](https://github.com/LavaGang/MelonLoader)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.1%20%7C%2010-512bd4?style=flat-square)](https://learn.microsoft.com/dotnet/)
[![Tauri](https://img.shields.io/badge/Tauri-2.x-24c8d8?style=flat-square)](https://tauri.app)
[![React](https://img.shields.io/badge/React-19-61dafb?style=flat-square)](https://react.dev)

</div>

---

BazaarPlusPlus is an open-source project for _The Bazaar_. It leaves game balance alone and adds information and records: a card collection browser, ten-win build recommendations, run history with combat replays, enchant and upgrade previews, a combat status bar, and more. On the desktop (Windows, macOS) it is a BepInEx mod plus a desktop installer; on Android it is the game APK with BazaarPlusPlus injected, installed directly. This repository also holds the upload backend, the data analyzer, and the public website.

Most players should get the installer or APK from [bazaarplusplus.com/download](https://bazaarplusplus.com/download?lang=en); this repository is for developers who want to inspect the implementation, contribute changes, or build locally.

> [!NOTE]
> The bulk of the codebase is led by [Codex](https://openai.com/codex), with [Claude Code](https://claude.com/product/claude-code) contributing in collaboration.

## Quick Start

### Windows and macOS

1. Open [bazaarplusplus.com/download](https://bazaarplusplus.com/download?lang=en) and choose the Windows `.exe` or macOS `.dmg`.
2. Close the game before running the installer. For updates, uninstall the old build before installing the new one.
3. Launch _The Bazaar_ once after installation so BazaarPlusPlus can finish setup.
4. On the main menu, confirm that the **Card Collection** button appears and the footer version text includes `BPP version`.

After upgrading from 5.x, one click on the installer's Install page imports your old runs, screenshots, and replays into the new 6.0 data folder; imported runs stay local and are never uploaded, the old folder is left unchanged, and you can delete it from the installer later.

### Android

1. Download the `.apk` (arm64) from the Android card on the [download page](https://bazaarplusplus.com/download?lang=en). Until the first Android release, the card reads "Coming soon".
2. Uninstall the official _The Bazaar_ first: Android will not install over an app signed by someone else. Your account and progress stay on the server; you only need to sign in again.
3. Install the downloaded APK and sign in. BazaarPlusPlus settings live in the game's own Settings dialog.
4. When the game updates, download the new APK and install it over this one; no uninstall needed. When only the mod changes, it updates itself in game.

Feature guides, hotkeys, and installation details live at [bazaarplusplus.com/tutorial](https://bazaarplusplus.com/tutorial?lang=en).

## Feature Matrix

✅ supported　◐ partial　— not available

### In Game

| Feature                              | What it does                                                                                                                                       | Windows | macOS |             Android              |
| ------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------- | :-----: | :---: | :------------------------------: |
| **Card Collection**                  | Browse items and skills at any time, filter by hero, tier, size, source, and day, and compare tier values and available enchantments               |   ✅    |  ✅   |                ✅                |
| **Ten-Win Builds**                   | Compare your shop, board, and stash with matching ten-win final builds                                                                             |   ✅    |  ✅   |                ✅                |
| **Run History**                      | Browse local runs, PvP battles, and Ghost Battles with both boards as saved                                                                        |   ✅    |  ✅   |       ◐ no hero portraits        |
| **Combat Replays and Ghost Battles** | Replay saved battles from History, or download battles other players uploaded                                                                      |   ✅    |  ✅   |                ✅                |
| **Favorites and Notes**              | Favorite key battles and write notes; a favorite outlives its deleted run                                                                          |   ✅    |  ✅   |                ✅                |
| **Replay Video**                     | Record a replay to a video file                                                                                                                    |   ✅    |  ✅   |                —                 |
| **Combat Status Bar**                | Combat time, pause, and 0.5× / 0.67× / 1× speed                                                                                                    |   ✅    |  ✅   |                ✅                |
| **Enchant Preview**                  | See an item's enchanted effects in its tooltip                                                                                                     |   ✅    |  ✅   |                ✅                |
| **Upgrade Preview**                  | See an item's next-tier values; holding Shift also marks where music notes land                                                                    |   ✅    |  ✅   | ◐ tooltip toggle, no music notes |
| **Detailed Preview**                 | Event choices and random outcomes, quest rewards, missing types for aggregate items; the desktop adds merchant tier odds and hero level-up rewards |   ✅    |  ✅   |                ◐                 |
| **Monster Board Preview**            | Hover an encounter to see the monster's board                                                                                                      |   ✅    |  ✅   |                —                 |
| **Combat Impact**                    | After a fight, hover an item to see its damage, healing, and other contributions                                                                   |   ✅    |  ✅   |                —                 |
| **Bilingual Item Names**             | Show the English and Chinese names together in tooltips                                                                                            |   ✅    |  ✅   |                —                 |
| **Cosmetic Names**                   | Label cosmetic thumbnails in the loadout list                                                                                                      |   ✅    |  ✅   |                —                 |
| **Voice Subtitles**                  | English and Chinese subtitles for character voice lines                                                                                            |   ✅    |  ✅   |                —                 |
| **Anonymous Mode**                   | Show the local player as Anonymous for screenshots, recordings, and streams                                                                        |   ✅    |  ✅   |                —                 |
| **Legendary Rank Display**           | Hide the rank, show an over-the-top value, or show rank and rating together                                                                        |   ✅    |  ✅   |                —                 |
| **FSR Upscaling**                    | Render at a lower resolution and upscale with FSR 1 for higher frame rates                                                                         |   ✅    |  ✅   |                —                 |
| **Chinese Terminology**              | Mainland Simplified or Taiwan Traditional terms in the BazaarPlusPlus UI                                                                           |   ✅    |  ✅   |                ✅                |
| **End-of-Run Screenshot**            | Capture the summary screen once its animation settles                                                                                              |   ✅    |  ✅   |                —                 |
| **Run Upload**                       | Upload runs and replays in the background for Ghost Battles and hero statistics, never during a run                                                |   ✅    |  ✅   |                ✅                |
| **BazaarDB Screenshot Upload**       | Share end-of-run screenshots with BazaarDB; off by default                                                                                         |   ✅    |  ✅   |                —                 |
| **Supporter Credits**                | Show the supporter list                                                                                                                            |   ✅    |  ✅   |                ✅                |
| **Mod Self-Update**                  | Update the mod in game when only the mod changed; Android also points to the new APK after a game update                                           |   ✅    |  ✅   |                ✅                |

### Install and Beyond

| Feature                             | What it does                                                                                                                   |  Windows  |   macOS   | Android |
| ----------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ | :-------: | :-------: | :-----: |
| **Install**                         | The desktop installer finds the Steam copy of the game; Android installs an APK                                                | Installer | Installer |   APK   |
| **Repair / Uninstall / Reset Data** | Fix a broken install or return to a clean state                                                                                |    ✅     |    ✅     |    —    |
| **5.x History Import**              | Import old run history when upgrading to 6.0 and manage the old data folders                                                   |    ✅     |    ✅     |    —    |
| **History Management**              | View, locate, and clean up local history and replay videos from the installer                                                  |    ✅     |    ✅     |    —    |
| **Stream Overlay**                  | A local browser-source server that shows run info in OBS and similar tools                                                     |    ✅     |    ✅     |    —    |
| **Installer Auto-Update**           | Check for and install new versions through the Tauri updater                                                                   |    ✅     |    ✅     |    —    |
| **Hero Statistics (website)**       | [bazaarplusplus.com/heroes](https://bazaarplusplus.com/heroes?lang=en) aggregates hero win rates and trends from uploaded runs |    ✅     |    ✅     |   ✅    |

## Repository Layout

| Directory                            | Contents                                                                     |
| ------------------------------------ | ---------------------------------------------------------------------------- |
| `bazaarplusplus-mod/`                | The in-game mod: shared Core, desktop BepInEx Host, Android MelonLoader Host |
| `bazaarplusplus-installer/`          | The desktop installer (Tauri 2 + React)                                      |
| `bazaarplusplus-server/`             | Cloudflare Worker: Bundle upload, Ghost discovery, and BazaarDB delivery     |
| `bazaarplusplus-analyzer/`           | Turns Bundles into hero and build snapshots                                  |
| `bazaarplusplus-site/`               | bazaarplusplus.com                                                           |
| `design/`                            | Design tokens, shared styles, and the brand book                             |
| `VERSION`, `release.mjs`, `release/` | Product version and the release pipeline for all three platforms             |

Each project keeps its own toolchain, `README.md`, and `AGENTS.md`; the root [`AGENTS.md`](AGENTS.md) records cross-project conventions and contract owners.

## Building From Source

Install the toolchains listed in the [development guide](docs/development.md) (just, .NET, Node, Rust, Python/uv), then:

```bash
just setup                  # Shared local config, locked dependencies, Git hooks
just doctor                 # What is still missing on this machine
just mod::fetch macos online  # Game assemblies for the mod (Windows: windows)
just mod::fetch android online  # Interop Snapshot and loader for the Android Host
just                        # Every command, grouped by project
```

The mod compiles against the game assemblies pinned by the Snapshot Lock `bazaarplusplus-mod/build/game-libs.lock.json`: `mod::fetch` accepts a local Steam install of _The Bazaar_ at the locked game version and otherwise pulls the snapshot from private storage; details are in the [development guide](docs/development.md#游戏程序集) (Chinese). Gate one project with `just <project>::check` and `just <project>::test`; `just fmt` formats every project. `just mod::build` only compiles; deploy a development DLL into the game explicitly with `just mod::build --deploy`. The Android Host builds with `just mod::android-build` and goes to a connected device with `just mod::android-deploy`; device smoke tests are in the [development guide](docs/development.md#android-冒烟) (Chinese).

Release builds (desktop installers and the Android APK) are compiled, signed, and uploaded by the `release.yml` GitHub Actions workflow on hosted runners; the signing, notarization, Android release key, and R2 credentials exist only as GitHub secrets and in the maintainers' local configuration, never in the public repository. Game assembly snapshots, decompiled game output, `.env`, and `.dev.vars` are not in the tree either. The release flow is in the [product release guide](docs/release.md) (Chinese).

## Derivative Work Notice

> [!IMPORTANT]
> If you plan to build on top of this project or release derivative mods, make sure your work complies with _The Bazaar_ official [Mod Policy](https://www.playthebazaar.com/mod-policy).

## Acknowledgements

|                          |                                                                                                                                                                                                                                                                                                                       |
| ------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Inspiration**          | [BazaarHelper](https://github.com/Duangi/BazaarHelper) · [BazaarPlannerMod](https://github.com/oceanseth/BazaarPlannerMod)                                                                                                                                                                                            |
| **Data reference**       | [bazaardb.gg](https://bazaardb.gg)                                                                                                                                                                                                                                                                                    |
| **Runtime dependencies** | [BepInEx](https://github.com/BepInEx/BepInEx) · [MelonLoader](https://github.com/LavaGang/MelonLoader) · [Harmony](https://github.com/pardeike/Harmony) · [Tauri](https://tauri.app) · [React](https://react.dev) · [Vite](https://vite.dev) · [Tailwind CSS](https://tailwindcss.com) · [FFmpeg](https://ffmpeg.org) |
| **Co-creators**          | [Codex](https://openai.com/codex) · [Claude Code](https://claude.com/product/claude-code)                                                                                                                                                                                                                             |

## Supporters

Thanks to everyone who supports BazaarPlusPlus. The full supporter list lives at [bazaarplusplus.com/support](https://bazaarplusplus.com/support?lang=en).

If you would like to support continued maintenance, head to [Ko-fi](https://ko-fi.com/cauyxy) or check the in-app sponsor options.

## License

Released under the [MIT License](LICENSE).

---

<div align="center">

<img src="bazaarplusplus-installer/src-tauri/icons/icon.png" alt="" width="48" height="48" />

**BazaarPlusPlus** · Born of Passion

[Website](https://bazaarplusplus.com) · [Download](https://bazaarplusplus.com/download?lang=en) · [Tutorial](https://bazaarplusplus.com/tutorial?lang=en) · [Ko-fi](https://ko-fi.com/cauyxy)

<sub>Archived history: [mod](https://github.com/BazaarPlusPlus/bazaarplusplus-mod) · [installer](https://github.com/BazaarPlusPlus/bazaarplusplus-installer) · [server](https://github.com/BazaarPlusPlus/bazaarplusplus-server) · [analyzer](https://github.com/BazaarPlusPlus/bazaarplusplus-analyzer) · [site](https://github.com/BazaarPlusPlus/bazaarplusplus-site)</sub>

</div>
