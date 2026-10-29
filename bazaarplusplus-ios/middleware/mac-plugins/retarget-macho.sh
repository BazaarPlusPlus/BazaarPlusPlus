#!/bin/bash
# Retarget a macOS arm64 Mach-O (a game's native plugin) to the arm64 iOS simulator: thin it, rewrite the
# platform in LC_BUILD_VERSION, point macOS framework paths at their iOS layout, and ad-hoc sign it.
# Frameworks iOS lacks are remapped: AudioUnit's functions live in AudioToolbox on iOS, and frameworks a
# plugin links without importing anything (Carbon) point at support/libbpp_empty.dylib, which the runner
# installs next to the plugins.
# usage: retarget-macho.sh <in> <out>
set -euo pipefail
IN=$1; OUT=$2
lipo "$IN" -thin arm64 -output "$OUT" 2>/dev/null || cp "$IN" "$OUT"
xcrun vtool -set-build-version iossim 15.0 18.0 -replace -output "$OUT" "$OUT"
otool -L "$OUT" | tail -n +2 | awk '{print $1}' | while read -r dep; do
  case "$dep" in
    /System/Library/Frameworks/*.framework/Versions/*/*)
      fw=${dep#/System/Library/Frameworks/}; name=${fw%%.framework*}
      case "$name" in
        AudioUnit) new=/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox ;;
        Carbon) new=@loader_path/support/libbpp_empty.dylib ;;
        *) new=/System/Library/Frameworks/$name.framework/$name ;;
      esac
      install_name_tool -change "$dep" "$new" "$OUT" 2>/dev/null ;;
  esac
done
codesign -f -s - "$OUT" >/dev/null 2>&1
