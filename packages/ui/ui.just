set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Check types, lint, formatting, and the unbundled ESM + dts build.
check: check-fast
    bun run build

# Pre-commit subset; avoids mutating dist/ while parallel hooks lint consumers.
check-fast:
    bun run typecheck
    bun run lint
    bun run format:check

# Run the UI kit unit and CSS class tests.
test:
    bun run test

# Build the publishable dist/ output.
build:
    bun run build

# Format UI kit sources in place.
fmt:
    bun run format
