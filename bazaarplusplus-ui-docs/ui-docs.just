set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

check:
    bun run typecheck
    bun run lint
    bun run format:check
    bun run build

test:
    bun run test
    bun run e2e

dev:
    bun run dev

build:
    bun run build

# Refresh the committed Run and Card Art demo snapshots from the fixture and CDN.
refresh-demo:
    bun tools/export-run-demo.ts
    bun tools/pin-card-art.ts

fmt:
    bun run format
