set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Run the source-only gate, including Rust/JS tests and the frontend build.
check:
    npm run verify -- --source-only
    npm run docs:check

# Generate bindings and run the Rust and JavaScript tests.
test:
    npm test

# Start the frontend development server (not the desktop app).
dev:
    npm run dev

# Format installer sources in place.
fmt:
    npm run format
