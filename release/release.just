set shell := ["bash", "-euo", "pipefail", "-c"]
set positional-arguments
set working-directory := '..'

# Project the authored VERSION and Payload Inventory into toolchain files.
sync:
    node release.mjs sync

# Check product version and Payload Inventory alignment without changing them.
check:
    node release.mjs check

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

# Advance latest only when both platforms have the same version and commit.
promote:
    node release.mjs promote
