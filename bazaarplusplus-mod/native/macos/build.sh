#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
build_dir="${1:-$script_dir/build}"
bundle_name="GfxPluginBppReplayVideoToolbox.bundle"
bundle_dir="$build_dir/$bundle_name"
executable_dir="$bundle_dir/Contents/MacOS"
executable="$executable_dir/GfxPluginBppReplayVideoToolbox"

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "BppReplayVideoToolbox can only be built on macOS." >&2
    exit 1
fi

mkdir -p "$executable_dir"
cp "$script_dir/Info.plist" "$bundle_dir/Contents/Info.plist"
sdk_path="$(xcrun --sdk macosx --show-sdk-path)"
clang_path="$(xcrun --sdk macosx --find clang++)"

"$clang_path" \
    -isysroot "$sdk_path" \
    -arch arm64 \
    -std=c++17 \
    -fobjc-arc \
    -fvisibility=hidden \
    -mmacosx-version-min=12.0 \
    -Werror=unguarded-availability -Werror=unguarded-availability-new \
    -O2 \
    -Wall -Wextra -Werror -Wno-deprecated-declarations \
    -bundle \
    -framework AVFoundation \
    -framework AudioToolbox \
    -framework CoreMedia \
    -framework CoreVideo \
    -framework Foundation \
    -framework Metal \
    -framework VideoToolbox \
    "$script_dir/BppReplayVideoToolbox.mm" \
    "$script_dir/BppReplayAudioMuxer.mm" \
    -o "$executable"

# Development output is deliberately ad-hoc. The installer repository is the only production
# signing authority and must re-sign this bundle with Team 9Z44S3N293 during release packaging.
codesign --force --sign - --timestamp=none "$bundle_dir"
codesign --verify --deep --strict "$bundle_dir"
"$script_dir/verify.sh" mac-replay "$executable" "$bundle_dir"

echo "built $bundle_dir"
