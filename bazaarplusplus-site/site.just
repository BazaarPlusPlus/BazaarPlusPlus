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

# Build once and reuse the checked output for deploy validation and browser tests.
ci: check test
    npm exec -- wrangler deploy --dry-run
    npm exec -- wrangler deploy --env preview --dry-run
    BPP_SITE_PREBUILT=1 npm run e2e -- --project=golden

# Run Playwright against the preview build. mode: golden (CI gate) or live (report only).
e2e mode="golden" *args:
    npm run e2e -- --project="$1" "${@:2}"

# Start the development server.
dev:
    npm run dev

# Build without deploying.
build:
    npm run build

# Format site sources in place.
fmt:
    npm run format
