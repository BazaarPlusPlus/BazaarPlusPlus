#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
output_dir="${1:-$script_dir/build}"
output="$output_dir/libBppMacAudio.dylib"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "BppMacAudio can only be built on macOS." >&2
  exit 1
fi

mkdir -p "$output_dir"
sdk_path="$(xcrun --sdk macosx --show-sdk-path)"
clang_path="$(xcrun --sdk macosx --find clang)"

# Output is lib-prefixed (libBppMacAudio.dylib) so [DllImport("BppMacAudio")]
# resolves on Unity-Mono via the lib{name}.dylib probe, exactly like
# libe_sqlite3.dylib.
#
# -mmacosx-version-min=12.0 (NOT 14.2) is required: CATapDescription exists on
# macOS 12, while the 14.2 tap functions are weak-imported. The dylib therefore
# loads on macOS 12-14 and IsSupported cleanly returns false there. The tap
# functions are only ever called after the >=15 gate passes.
"$clang_path" -isysroot "$sdk_path" -arch arm64 -fobjc-arc \
  -framework CoreAudio -framework Foundation \
  -mmacosx-version-min=12.0 \
  -Werror=unguarded-availability -Werror=unguarded-availability-new \
  -O2 -dynamiclib -install_name @rpath/libBppMacAudio.dylib \
  -o "$output" "$script_dir/BppMacAudio.m"

codesign --force --sign - --timestamp=none "$output"
"$script_dir/../macos/verify.sh" mac-audio "$output"

echo "built $output"
