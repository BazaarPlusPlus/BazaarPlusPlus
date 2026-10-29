#!/bin/bash
# Compile the exact loaded corlib into fixed ARM64 interpreter trampoline pools.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
KIND=${1:-simulator}
MANAGED=${2:-$ROOT/runtime/engine/shell/Builds/MacRGBA/BppShell.app/Contents/Resources/Data/Managed}
case "$KIND" in
 simulator) SDK=iphonesimulator; TARGET=arm64-apple-ios15.0-simulator ;;
 device) SDK=iphoneos; TARGET=arm64-apple-ios15.0 ;;
 *) echo 'usage: build-aot.sh [simulator|device] [Managed directory]' >&2; exit 2 ;;
esac
MANAGED=$(cd "$MANAGED" && pwd)
HOST_MONO=$ROOT/build/mono-host-aot-arm64
[ -f "$HOST_MONO/lib/libmonoboehm-2.0.a" ] || { echo 'Run bash runtime/mono/build.sh host-aot first' >&2; exit 1; }
mkdir -p "$ROOT/build/tools"
COMPILER=$(mktemp "$ROOT/build/tools/mono-aot.XXXXXX")
trap 'rm -f "$COMPILER"' EXIT
xcrun --sdk macosx clang -target arm64-apple-macos11.0 -isysroot "$(xcrun --sdk macosx --show-sdk-path)" \
  "$ROOT/runtime/mono/aot-host/main.c" \
  -Wl,-force_load,"$HOST_MONO/lib/libmonoboehm-2.0.a" \
  -framework CoreFoundation -framework Foundation -framework Security -framework GSS \
  -lz -liconv -lc++ -lobjc -Wl,-export_dynamic -o "$COMPILER"
mv "$COMPILER" "$ROOT/build/tools/mono-aot"
OUT=$ROOT/build/aot-$KIND
mkdir -p "$OUT"
MONO_PATH="$MANAGED" "$ROOT/build/tools/mono-aot" --aot="full,interp,asmonly,static,mtriple=arm64-ios,nopagetrampolines,ntrampolines=16384,nrgctx-trampolines=32768,nimt-trampolines=4096,nunbox-arbitrary-trampolines=8192,nftnptr-arg-trampolines=2048,ngsharedvt-trampolines=4096,outfile=$OUT/mscorlib.s" "$MANAGED/mscorlib.dll"
xcrun --sdk "$SDK" clang -target "$TARGET" -isysroot "$(xcrun --sdk "$SDK" --show-sdk-path)" -c "$OUT/mscorlib.s" -o "$OUT/mscorlib.o"
xcrun ar rcs "$OUT/libcorlib-aot.a" "$OUT/mscorlib.o"
shasum -a 256 "$MANAGED/mscorlib.dll" > "$OUT/corlib.sha256"
