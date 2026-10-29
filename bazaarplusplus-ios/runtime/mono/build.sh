#!/bin/bash
# Cross-compile the pinned Unity Mono runtime with the JIT disabled, plus the macOS host build that runs the AOT compiler.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
SRC=$ROOT/third_party/mono
KIND=${1:-simulator-aot}
EXTRA_CONFIG=()
case "$KIND" in
  simulator-aot) SDK_NAME=iphonesimulator; TRIPLE=arm64-apple-ios15.0-simulator; FLAVOR=sim-aot; PLATFORM_DEFINE=-DTARGET_IPHONE_SIMULATOR=1; EXTRA_CONFIG=(--enable-minimal=jit,remoting) ;;
  host-aot) SDK_NAME=macosx; TRIPLE=arm64-apple-macos11.0; FLAVOR=host-aot; PLATFORM_DEFINE=-DTARGET_IPHONE_SIMULATOR=1; EXTRA_CONFIG=(--enable-minimal=remoting) ;;
  device) SDK_NAME=iphoneos; TRIPLE=arm64-apple-ios15.0; FLAVOR=device; PLATFORM_DEFINE=; EXTRA_CONFIG=(--enable-minimal=jit,remoting) ;;
  *) echo "usage: build-mono.sh [simulator-aot|device|host-aot]" >&2; exit 2 ;;
esac
python3 "$ROOT/runtime/mono/bootstrap.py" --check
OUT=$ROOT/build/mono-$FLAVOR-arm64
BLD=$ROOT/build/mono-$FLAVOR-arm64-obj
export PATH="/opt/homebrew/opt/libtool/libexec/gnubin:/opt/homebrew/bin:$PATH"

SDK=$(xcrun --sdk "$SDK_NAME" --show-sdk-path)
TARGET="-target $TRIPLE -isysroot $SDK"
export CC="$(xcrun --sdk "$SDK_NAME" -f clang) $TARGET"
export CXX="$(xcrun --sdk "$SDK_NAME" -f clang++) $TARGET"
export CPPFLAGS="-DMONOTOUCH=1 -DHOST_IOS $PLATFORM_DEFINE -D_XOPEN_SOURCE -DHAVE_LARGE_FILE_SUPPORT=1"
export CFLAGS="-g -O2 -std=gnu11 -fexceptions -Wno-error=implicit-function-declaration -Wno-error=incompatible-function-pointer-types -Wno-error=int-conversion"
export CXXFLAGS="-g -O2"
export LDFLAGS="-framework CoreFoundation -lobjc -lc++"

if [ ! -f "$SRC/configure" ]; then
  (cd "$SRC" && NOCONFIGURE=1 ./autogen.sh)
fi

mkdir -p "$BLD"
cd "$BLD"
CONFIG_DIGEST=$(shasum -a 256 "$ROOT/runtime/mono/build.sh" | cut -d' ' -f1)
if [ ! -f Makefile ] || [ "$(cat .bpp-config 2>/dev/null || true)" != "$CONFIG_DIGEST" ]; then
  "$SRC/configure" \
    "${EXTRA_CONFIG[@]}" \
    --build=x86_64-apple-darwin20 --host=aarch64-apple-darwin \
    --prefix="$OUT" \
    --with-glib=embedded --disable-nls --disable-mcs-build --with-mcs-docs=no \
    --enable-no-threads-discovery=yes --enable-ignore-dynamic-loading=yes \
    --enable-dont-register-main-static-data=yes --enable-thread-local-alloc=no \
    --enable-unity-define=yes \
    --disable-btls --disable-executables --disable-visibility-hidden \
    --without-ikvm-native --with-tls=pthread --with-sigaltstack=no \
    --with-lazy-gc-thread-creation=yes --enable-dtrace=no \
    --disable-crash-reporting --disable-cooperative-suspend --disable-hybrid-suspend \
    --disable-shared-handles --with-large-heap=yes \
    ac_cv_c_bigendian=no ac_cv_func_fstatat=no ac_cv_func_readlinkat=no \
    ac_cv_func_getpwuid_r=no ac_cv_func_posix_getpwuid_r=yes \
    ac_cv_header_curses_h=no ac_cv_header_localcharset_h=no ac_cv_header_sys_user_h=no \
    ac_cv_func_getentropy=no ac_cv_func_futimens=no ac_cv_func_utimensat=no \
    ac_cv_func_shm_open_working_with_mmap=no ac_cv_func_pthread_jit_write_protect_np=no \
    ac_cv_func_preadv=no ac_cv_func_pwritev=no ac_cv_func_clock_nanosleep=no \
    ac_cv_func_system=no \
    mono_cv_sizeof_sunpath=104 mono_cv_uscore=yes
  printf '%s\n' "$CONFIG_DIGEST" > .bpp-config
fi
make -j"$(sysctl -n hw.ncpu)"
make install
