set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments
set working-directory := '..'

# Project the authored VERSION and Payload Inventory into toolchain files.
sync:
    node release.ts sync

# Check product version and Payload Inventory alignment without changing them.
check:
    node release.ts check

# Run the root-owned Product Release tests without remote publishing.
test:
    npm test -- --project release

# Prepare one platform Payload; optional arguments are MSBuild properties.
[arg('platform', pattern='macos|windows|android')]
prepare platform *args:
    node release.ts prepare --platform "$1" -- "${@:2}"

# Prepare, verify, sign, and bundle on the native host (android: build and sign the Android App and the Interop Package, reading R2); never upload.
[arg('platform', pattern='macos|windows|android')]
build platform *args:
    {{ if platform == "android" { "node scripts/workspace.ts run release --" } else { "" } }} node scripts/workspace.ts run signing -- node release.ts build --platform "$1" -- "${@:2}"

# Upload immutable platform artifacts without advancing latest.
[arg('platform', pattern='macos|windows|android')]
upload platform:
    node scripts/workspace.ts run release -- node release.ts upload --platform "$1"

# Verify one platform's mainland mirror share page and record it for this version.
[arg('platform', pattern='macos|windows')]
mirror platform url *args:
    node scripts/workspace.ts run release -- node release.ts mirror --platform "$1" --url "$2" "${@:3}"

# After both uploads, verify both platforms and record their mainland share pages.
mirror-all windows-url macos-url *args:
    node scripts/workspace.ts run release -- node release.ts mirror-all --windows-url "$1" --macos-url "$2" "${@:3}"

# Re-check the recorded mainland mirrors read-only; no credentials.
verify-mirror *args:
    node release.ts verify-mirror "$@"

# Publish every platform at VERSION; with --platform, publish one platform's manifest.
promote *args:
    node scripts/workspace.ts run release -- node release.ts promote "$@"

# Upload mod runtime data (supporters or voice-lines) to mod-data/ in the public bucket.
[arg('name', pattern='supporters|voice-lines')]
mod-data name file:
    node scripts/workspace.ts run release -- node release/mod-data.ts "$1" "$2"
