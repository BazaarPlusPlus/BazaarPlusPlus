set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Check types, lint, formatting, and the production build.
check:
    npm run typecheck
    npm run lint
    npm run format:check
    npm run build

# Run site tests once.
test:
    npm test

# Start the development server.
dev:
    npm run dev

# Build without deploying.
build:
    npm run build

# Format site sources in place.
fmt:
    npm run format
