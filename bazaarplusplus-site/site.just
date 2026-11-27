set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Check types, lint, formatting, and the production build.
check:
    bun run typecheck
    bun run lint
    bun run format:check
    bun run build

# Run site tests once.
test:
    bun run test

# Build once and reuse the checked output for deploy validation and browser tests.
ci: check test
    bun run wrangler deploy --dry-run
    bun run wrangler deploy --env preview --dry-run
    BPP_SITE_PREBUILT=1 bun run e2e --project=golden

# Run Playwright against the preview build. mode: golden (CI gate) or live (report only).
e2e mode="golden" *args:
    bun run e2e --project="$1" "${@:2}"

# Start the development server.
dev:
    bun run dev

# Build without deploying.
build:
    bun run build

# Format site sources in place.
fmt:
    bun run format
