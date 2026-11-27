set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments
set working-directory := '..'

# Project the authored VERSION and Payload Inventory into toolchain files.
sync:
    bun release/cli.ts sync

# Check product version and Payload Inventory alignment without changing them.
check:
    bun release/cli.ts check

# Run the root-owned Product Release tests without remote publishing.
test:
    bun run test --project release

# Prepare one platform Payload; optional arguments are MSBuild properties.
[arg('platform', pattern='macos|windows|android')]
prepare platform *args:
    bun release/cli.ts prepare --platform "$1" -- "${@:2}"

# Prepare, verify, sign, and bundle on the native host (android: build and sign the Android App and the Interop Package, reading R2); never upload.
[arg('platform', pattern='macos|windows|android')]
build platform *args:
    bun scripts/workspace.ts run {{ if platform == "android" { "release,signing" } else { "signing" } }} -- bun release/cli.ts build --platform "$1" -- "${@:2}"

# Upload immutable platform artifacts without advancing latest.
[arg('platform', pattern='macos|windows|android')]
upload platform:
    bun scripts/workspace.ts run release -- bun release/cli.ts upload --platform "$1"

# Push one platform's uploaded download to the mainland mirror, check the served bytes and record it for this version.
[arg('platform', pattern='macos|windows|android')]
mirror platform *args:
    bun scripts/workspace.ts run release -- bun release/cli.ts mirror --platform "$1" "${@:2}"

# Re-check the recorded mainland mirrors read-only; no credentials.
verify-mirror *args:
    bun release/cli.ts verify-mirror "$@"

# Publish every platform at VERSION; with --platform, publish one platform's manifest.
promote *args:
    bun scripts/workspace.ts run release -- bun release/cli.ts promote "$@"

# Card Art (.github/workflows/card-art.yml): stale <previous index.json>, download <dir> or publish <dir>.
[arg('command', pattern='stale|download|publish')]
card-art command path:
    bun scripts/workspace.ts run release -- bun release/card-art.ts "$1" "$2"

# Upload maintainer data (supporters, voice-lines or zh-names) to mod-data/ in the public bucket.
[arg('name', pattern='supporters|voice-lines|zh-names')]
mod-data name file:
    bun scripts/workspace.ts run release -- bun release/mod-data.ts "$1" "$2"
