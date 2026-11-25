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

fmt:
    bun run format
