set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Run the source-only gate, including Rust/JS tests and the frontend build.
check:
    bun run verify --source-only

# Pre-commit subset; full Rust checks, tests, binding generation and builds run in check.
check-fast:
    bun run format:check
    bun run lint
    bun run check:ts
    cargo fmt --manifest-path src-tauri/Cargo.toml -- --check
    bun run docs:check

# Generate bindings and run the Rust and JavaScript tests.
test:
    bun run test

# Run the macOS install and repair acceptance tests against a packaged resource directory and a copied game.
acceptance:
    [ "$(uname -s)" = Darwin ] || { echo "installer::acceptance runs on macOS only" >&2; exit 1; }
    : "${BPP_ACCEPTANCE_RESOURCE_DIR:?set it to a packaged macOS resource directory holding BepInExSource/BepInEx.zip and Trampoline/bpp_launcher}"
    : "${BPP_TEST_GAME_ROOT:?set it to a The Bazaar installation whose TheBazaar.app is copied; the source is never modified}"
    TAURI_CONFIG='{"bundle":{"resources":[]}}' cargo build --manifest-path src-tauri/Cargo.toml --bin bppinstaller
    BPP_TEST_INSTALLER_CLI="$(pwd)/src-tauri/target/debug/bppinstaller" TAURI_CONFIG='{"bundle":{"resources":[]}}' cargo test --manifest-path src-tauri/Cargo.toml --lib -- --ignored fresh_install_writes_payload_and_signed_trampoline_without_quitting_steam copied_steam_bundle_repair_acceptance

# Run the headless installer from source; use --help for commands.
cli *args:
    bun scripts/headless.ts "$@"

# Start the frontend development server (not the desktop app).
dev:
    bun run dev

# Start the full Tauri desktop app; verifies the locked Bun dependencies before launch.
app:
    bun scripts/checks/check-toolchain.ts "$(bun --version)"
    bun install --frozen-lockfile
    bun run tauri dev

# Format installer sources in place.
fmt:
    bun run format
