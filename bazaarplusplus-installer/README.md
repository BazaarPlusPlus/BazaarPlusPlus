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

Prerequisites:

- **Node.js 24** and npm (versions pinned in `package.json` `engines`)
- **Rust** via rustup (toolchain pinned by `rust-toolchain.toml`)

Run the desktop app in development mode:

```bash
./build.sh
```

This installs dependencies and runs `npm run tauri dev` for you. To run the steps manually:

```bash
npm ci
npm run tauri dev
```

If you only need the frontend (no native shell), `npm run dev` starts a Vite dev server in the browser.

## Everyday development

| Command | What it does |
| --- | --- |
| `npm run check` | Regenerate bindings, then TypeScript type-check (`tsc --noEmit`) |
| `npm run test` | Rust tests (`src-tauri`) + frontend Vitest |
| `npm run format` | Prettier across the configured globs |
| `npm run prebuild-check` | Validate versioning, bundled resources, and Tauri config |
| `npm run docs:check` | Validate documentation structure and citations |

One thing to know: the TypeScript client for Tauri commands is **generated** from the Rust command signatures into `src/types/generated/`. `dev`, `build`, `check`, and `test` regenerate it automatically — never edit those files by hand.

## Release build

```bash
./build.sh --prod               # release bundle for the current host platform
./build.sh --prod --clean-deps  # same, but reinstall npm dependencies first
./build.sh --prod --upload      # build, then upload artifacts to Cloudflare R2
./build.sh --upload             # upload previously built artifacts only
```

`--prod` runs version sync and prebuild checks, and requires updater signing secrets (read from `signing-secrets/` or environment variables).

macOS additionally requires:

- A Developer ID Application signing identity
- Apple notarization API credentials
- The Apple Silicon Rust target: `rustup target add aarch64-apple-darwin`

Artifacts land under:

- **Windows:** `src-tauri/target/release/bundle/nsis/`
- **macOS:** `src-tauri/target/aarch64-apple-darwin/release/bundle/` (`app`, `dmg`)

Platform facts (bundle paths, updater keys, Rust targets) are defined in `scripts/release/release-platforms.mjs`. Read `docs/release.md` before changing anything release-related.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/` | React frontend |
| `src-tauri/` | Rust backend: native commands, services, packaging |
| `static/` | Frontend media and fonts imported by Vite |
| `scripts/` | Build tooling — `release/` holds the packaging pipeline, `checks/` the verification entry points; behavior tests are colocated as `*.test.mjs` |
| `docs/` | Project documentation (see below) |

## Documentation

- **Start with [`CONTEXT.md`](CONTEXT.md)** — the entry map: vocabulary plus pointers telling you which topic doc to open for which kind of work.
- `docs/*.md` — current behavior, split by topic (architecture, install/reset, updater, release, …).
- `docs/adr/` — architectural decisions that still constrain work.
- Doc-layout policy lives in `CLAUDE.md`; platform smoke-test gaps are tracked as GitHub issues labelled `manual-validation`.
