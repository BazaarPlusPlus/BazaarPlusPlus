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
