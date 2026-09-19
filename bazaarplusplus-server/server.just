set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Run the Worker's type, lint, and formatting checks.
check:
    npm run check

# Run Worker tests in the local Cloudflare test runtime.
test:
    npm test

# Start the local Worker; requires this project's .dev.vars.
dev:
    npm run dev

# Format Worker sources in place.
fmt:
    npm run format
