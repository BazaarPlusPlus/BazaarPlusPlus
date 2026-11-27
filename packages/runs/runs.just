set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments
set working-directory := '../..'

# Check that the Game Message Schema is in generated form and types pass.
check:
    bun packages/runs/tools/message-schema/generate.ts check
    bun x tsc -p packages/runs/tsconfig.json

# Run the shared Run projection and Game Message Schema tests.
test:
    bun run test --project runs

# Merge Game Assembly Snapshots under bazaarplusplus-mod/game-libs/ into the Game Message Schema
# (default: the lock's macOS and Android entries on disk). Needs the .NET SDK.
message-schema *snapshots:
    bun packages/runs/tools/message-schema/generate.ts "$@"
