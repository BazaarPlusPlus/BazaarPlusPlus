set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments

# Check the Bot's types, lint and formatting.
check:
    bun run check

# Exercise the Bot with local Workers and D1, then the Card Art index shape (stdlib Python).
test:
    bun run test

# Bundle both Workers locally without deploying.
build:
    bun run build

# Refresh shared credentials and run both Workers locally.
dev:
    bun ../scripts/workspace.ts run bot -- bun run dev

# Render the report images from one player's real Bundles; never sends a QQ message.
preview *args:
    bun run preview "$@"

# Format Bot sources.
fmt:
    bun run format

# Extract Card Art from a `just release::card-art download` directory into <out> (tools/card-art/extract.py).
card-art game out *args:
    uv run --locked --script tools/card-art/extract.py "$1/Data" "$1/GameData.db.zip" "$2" "${@:3}"
