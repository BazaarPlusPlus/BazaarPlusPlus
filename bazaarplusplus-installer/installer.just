set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Run the source-only gate, including Rust/JS tests and the frontend build.
check:
    npm run verify -- --source-only
    npm run docs:check

# Pre-commit subset; full Rust checks, tests, binding generation and builds run in check.
check-fast:
    npm run format:check
    npm run lint
    npm run check:ts
    cargo fmt --manifest-path src-tauri/Cargo.toml -- --check
    npm run docs:check

# Generate bindings and run the Rust and JavaScript tests.
test:
    npm test

# Run the macOS install and repair acceptance tests against a packaged resource directory and a copied game.
acceptance:
    [ "$(uname -s)" = Darwin ] || { echo "installer::acceptance runs on macOS only" >&2; exit 1; }
    : "${BPP_ACCEPTANCE_RESOURCE_DIR:?set it to a packaged macOS resource directory holding BepInExSource/BepInEx.zip and Trampoline/bpp_launcher}"
    : "${BPP_TEST_GAME_ROOT:?set it to a The Bazaar installation whose TheBazaar.app is copied; the source is never modified}"
    TAURI_CONFIG='{"bundle":{"resources":[]}}' cargo build --manifest-path src-tauri/Cargo.toml --bin bppinstaller
    BPP_TEST_INSTALLER_CLI="$(pwd)/src-tauri/target/debug/bppinstaller" TAURI_CONFIG='{"bundle":{"resources":[]}}' cargo test --manifest-path src-tauri/Cargo.toml --lib -- --ignored fresh_install_writes_payload_and_signed_trampoline_without_quitting_steam copied_steam_bundle_repair_acceptance

# Run the headless installer from source; use --help for commands.
cli *args:
    node scripts/headless.mjs "$@"

# Start the frontend development server (not the desktop app).
dev:
    npm run dev

# Start the full Tauri desktop app; installs npm dependencies only when the tree is invalid.
app:
    node scripts/checks/check-toolchain.mjs "$(node --version)" "$(npm --version)"
    npm ls --depth=0 >/dev/null 2>&1 || npm ci
    npm run tauri dev

# Format installer sources in place.
fmt:
    npm run format
