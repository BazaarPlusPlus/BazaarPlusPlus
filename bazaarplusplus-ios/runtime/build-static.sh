#!/bin/bash
# Build the shim as a static library for ordinary UnityFramework linkage.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"
KIND=${1:-simulator}
SOURCES=(runtime/bridge/il2cpp_on_mono.c runtime/bridge/engine_hooks.c runtime/bridge/ui_diag.m runtime/bridge/gc_stats.c middleware/fmod/coreaudio_compat.c middleware/steamworks/steam_api_stub.c middleware/steamworks/steam_api_runtime.c
 service/Native/bridge.m service/Native/keychain.m service/Native/chunks.c)
case "$KIND" in
 simulator) SDK=iphonesimulator; TARGET=arm64-apple-ios15.0-simulator; FLAVOR=sim-aot ;;
 device) SDK=iphoneos; TARGET=arm64-apple-ios15.0; FLAVOR=device ;;
 *) echo 'usage: build-static.sh [simulator|device]' >&2; exit 2 ;;
esac
MONO=$ROOT/build/mono-$FLAVOR-arm64
grep -q '^#define DISABLE_JIT 1' "$ROOT/build/mono-$FLAVOR-arm64-obj/config.h" || { echo 'Static shim requires a JIT-disabled Mono build' >&2; exit 1; }
OUT=$ROOT/build/shim-static-$KIND
mkdir -p "$OUT"
CC=(xcrun --sdk "$SDK" clang -target "$TARGET" -isysroot "$(xcrun --sdk "$SDK" --show-sdk-path)" -O1 -g -std=gnu11 -fvisibility=hidden -I"$MONO/include/mono-2.0" -I"$ROOT/build/vendor/zstd-1.5.7/lib")
OBJECTS=()
for SOURCE in "${SOURCES[@]}"; do
 OBJECT="$OUT/$(basename "$SOURCE").o"
 "${CC[@]}" -fobjc-arc -c "$ROOT/$SOURCE" -o "$OBJECT"
 OBJECTS+=("$OBJECT")
done
if [ "$KIND" = device ]; then
 xcrun --sdk "$SDK" clang++ -target "$TARGET" -isysroot "$(xcrun --sdk "$SDK" --show-sdk-path)" \
  -O2 -c "$ROOT/runtime/engine/device_texture.cpp" -o "$OUT/device_texture.o"
 OBJECTS+=("$OUT/device_texture.o")
fi
xcrun --sdk "$SDK" clang++ -target "$TARGET" -isysroot "$(xcrun --sdk "$SDK" --show-sdk-path)" \
  -std=c++14 -fobjc-arc -O2 -c "$ROOT/middleware/fmod/fmod_static.mm" -o "$OUT/fmod_static.o"
OBJECTS+=("$OUT/fmod_static.o")
PLUGIN=$(python3 -c 'import json; from pathlib import Path; j=json.loads(Path("toolchain.lock.json").read_text()); print("build/vendor/"+j["alembicNative"]["unityAlembic"]["folder"]+"/Source/abci")' )
SQLITE=$(python3 -c 'import json; from pathlib import Path; print("build/vendor/"+json.loads(Path("toolchain.lock.json").read_text())["sqlite"]["folder"])')
xcrun --sdk "$SDK" clang++ -target "$TARGET" -isysroot "$(xcrun --sdk "$SDK" --show-sdk-path)" \
  -std=c++14 -O2 -I"$ROOT/$PLUGIN" -I"$ROOT/$PLUGIN/Foundation" -I"$ROOT/$SQLITE" -c "$ROOT/middleware/preflight.cpp" -o "$OUT/preflight.o"
OBJECTS+=("$OUT/preflight.o")
"${CC[@]}" '-DMONO_API=__attribute__((visibility("default")))' -DHAVE_SYS_ZLIB -I"$ROOT/build/mono-$FLAVOR-arm64-obj" -I"$ROOT/build/mono-$FLAVOR-arm64-obj/mono/eglib" -I"$ROOT/third_party/mono/mono/eglib" -c "$ROOT/third_party/mono/support/zlib-helper.c" -o "$OUT/zlib-helper.o"
xcrun libtool -static -o "$OUT/libshim.a" "${OBJECTS[@]}" "$OUT/zlib-helper.o"
