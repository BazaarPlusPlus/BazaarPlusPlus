set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Run the Worker's type, lint, and formatting checks.
check:
    bun run check

# Run the Worker tests in the local Cloudflare test runtime.
test:
    bun run test

# Start the local Worker; requires this project's .dev.vars.
dev:
    bun ../scripts/workspace.ts run server -- bun run dev

# Format Worker sources in place.
fmt:
    bun run format
