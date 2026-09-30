# BazaarPlusPlus Installer

Desktop app for installing and managing the **BazaarPlusPlus** mod for *The Bazaar*.

What it does:

- Detects the Steam installation of The Bazaar
- Installs BepInEx and the mod payload; supports repair and clean uninstall
- Launches the game through Steam
- Shows the mod's local run history (matches, screenshots, videos)
- Serves a local OBS overlay for streaming
- Keeps itself up to date via a built-in updater

**Tech stack:** Tauri 2 (Rust backend) + React 19 / Vite / TypeScript (frontend).

## Quick start

Toolchain versions are pinned by `package.json` (`engines`, `packageManager`) and `rust-toolchain.toml`; workspace setup is in the [development guide](../docs/development.md).

```bash
just installer::app  # Full desktop app (checks the toolchain, installs dependencies if needed)
just installer::dev  # Frontend only, in the browser
just installer::check
```

`just --list installer` and `package.json` list the narrower scripts. The TypeScript client for Tauri commands is **generated** from the Rust command signatures into `src/types/generated/`; `dev`, `build`, `test`, and `just installer::check` regenerate it, so edits go to the Rust signatures.

## Release build

Platform builds run through the workspace release commands (`just release::build <platform>`); the full flow, credentials, and platform facts are in the [workspace release docs](../docs/release.md) and [installer release guide](docs/release.md). `scripts/bundle.sh` runs only inside the product build lock that `release::build` holds.

## Documentation

**Start with [`CONTEXT.md`](CONTEXT.md)**: the vocabulary plus which topic doc to open for which kind of work. Decisions are in `docs/adr/`; [ADR-0007](docs/adr/0007-documentation-contract.md) defines this layout. Platform smoke-test gaps are GitHub issues labelled `manual-validation`.
