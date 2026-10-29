#!/bin/bash
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"
KIND=${1:-simulator}
case "$KIND" in
 simulator) PLATFORM=iphonesimulator; TARGET=arm64-apple-ios15.0-simulator ;;
 device) PLATFORM=iphoneos; TARGET=arm64-apple-ios15.0 ;;
 *) exit 2 ;;
esac
python3 - <<'PY'
import hashlib,json,subprocess,urllib.request
from pathlib import Path
lock=json.loads(Path('toolchain.lock.json').read_text())['zstd']
archive=Path('build/vendor/zstd-'+lock['version']+'.tar.gz');archive.parent.mkdir(parents=True,exist_ok=True)
if not archive.exists():
    with urllib.request.urlopen(lock['url'],timeout=60) as r: archive.write_bytes(r.read())
if hashlib.sha256(archive.read_bytes()).hexdigest()!=lock['sha256']:raise RuntimeError('zstd checksum mismatch')
subprocess.run(['tar','xzf',str(archive),'-C',str(archive.parent)],check=True)
PY
dotnet restore service/Managed/BazaarPlusPlus.Ios.Service.csproj --locked-mode
dotnet build service/Managed/BazaarPlusPlus.Ios.Service.csproj -c Release --no-restore
SDK=$(xcrun --sdk "$PLATFORM" --show-sdk-path)
OUT="$ROOT/build/steam-native-$KIND"
SRC="$ROOT/build/vendor/zstd-1.5.7/lib"
mkdir -p "$OUT"
OBJECTS=()
for SOURCE in "$SRC"/common/*.c "$SRC"/decompress/*.c; do
 OBJECT="$OUT/$(basename "$SOURCE").o"
 xcrun clang -target "$TARGET" -isysroot "$SDK" -O2 -fvisibility=hidden -DZSTD_DISABLE_ASM -I"$SRC" -I"$SRC/common" -c "$SOURCE" -o "$OBJECT"
 OBJECTS+=("$OBJECT")
done
xcrun libtool -static -o "$OUT/libzstd.a" "${OBJECTS[@]}"
