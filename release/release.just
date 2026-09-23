set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments
set working-directory := '..'

# Project the authored VERSION and Payload Inventory into toolchain files.
sync:
    node release.mjs sync

# Check product version and Payload Inventory alignment without changing them.
check:
    node release.mjs check

# Run the root-owned Product Release tests without remote publishing.
test:
    npm test

# Prepare one platform Payload; optional arguments are MSBuild properties.
[arg('platform', pattern='macos|windows')]
prepare platform *args:
    node release.mjs prepare --platform "$1" -- "${@:2}"

# Prepare, verify, sign, and bundle on the native host; never upload.
[arg('platform', pattern='macos|windows')]
build platform *args:
    node release.mjs build --platform "$1" -- "${@:2}"

# Upload immutable platform artifacts without advancing latest.
[arg('platform', pattern='macos|windows')]
upload platform:
    node release.mjs upload --platform "$1"

# Verify one platform's mainland mirror share page and record it for this version.
[arg('platform', pattern='macos|windows')]
mirror platform url *args:
    node release.mjs mirror --platform "$1" --url "$2" "${@:3}"

# Re-check the recorded mainland mirrors read-only; no credentials.
verify-mirror *args:
    node release.mjs verify-mirror "$@"

# Publish every platform at VERSION; with --platform, publish one platform's manifest.
promote *args:
    node release.mjs promote "$@"
