// BazaarPlusPlus macOS launch trampoline.
//
// Installed as the game bundle's CFBundleExecutable on macOS 27+ (where Steam no
// longer spawns a prefix executable before %command%, killing the run_bepinex.sh
// launch path). The real Unity bootstrap is renamed to "<exe>.orig" beside us and
// re-signed with the JIT entitlements at install time; this stub sets the same
// DOORSTOP_* env that run_bepinex.sh would, prepends libdoorstop.dylib to
// DYLD_INSERT_LIBRARIES (preserving Steam's overlay), and execs the real binary.
//
// Compiled arm64 at build time (see build.sh). Validated end-to-end on macOS 27 +
// Steam 2026-06-09. Keep the DOORSTOP_* keys in sync with run_bepinex.sh.
#include <stdlib.h>
#include <unistd.h>
#include <string.h>
#include <stdio.h>
#include <errno.h>
#include <limits.h>
#include <libgen.h>
#include <mach-o/dyld.h>

int main(int argc, char **argv) {
    char raw[PATH_MAX]; uint32_t sz = sizeof(raw);
    if (_NSGetExecutablePath(raw, &sz) != 0) { fprintf(stderr, "bpp: exe path too long\n"); return 70; }
    char exe[PATH_MAX];
    if (!realpath(raw, exe)) { strncpy(exe, raw, sizeof(exe)); exe[sizeof(exe)-1]=0; }
    char exe_copy[PATH_MAX]; strncpy(exe_copy, exe, sizeof(exe_copy)); exe_copy[sizeof(exe_copy)-1]=0;
    char macos_dir[PATH_MAX]; strncpy(macos_dir, dirname(exe_copy), sizeof(macos_dir)); macos_dir[sizeof(macos_dir)-1]=0;
    char gd_raw[PATH_MAX]; snprintf(gd_raw, sizeof(gd_raw), "%s/../../..", macos_dir);
    char game_dir[PATH_MAX];
    if (!realpath(gd_raw, game_dir)) { fprintf(stderr, "bpp: cannot resolve game dir from %s\n", gd_raw); return 71; }

    char buf[PATH_MAX*2];
    setenv("DOORSTOP_ENABLED", "1", 1);
    snprintf(buf, sizeof(buf), "%s/BepInEx/core/BepInEx.Preloader.dll", game_dir);
    setenv("DOORSTOP_TARGET_ASSEMBLY", buf, 1);
    setenv("DOORSTOP_BOOT_CONFIG_OVERRIDE", "", 1);
    setenv("DOORSTOP_IGNORE_DISABLED_ENV", "0", 1);
    setenv("DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE", "", 1);
    setenv("DOORSTOP_MONO_DEBUG_ENABLED", "0", 1);
    setenv("DOORSTOP_MONO_DEBUG_ADDRESS", "127.0.0.1:10000", 1);
    setenv("DOORSTOP_MONO_DEBUG_SUSPEND", "0", 1);
    setenv("DOORSTOP_CLR_RUNTIME_CORECLR_PATH", ".dylib", 1);
    setenv("DOORSTOP_CLR_CORLIB_DIR", "", 1);
    { const char *cur = getenv("DYLD_LIBRARY_PATH");
      if (cur && cur[0]) snprintf(buf, sizeof(buf), "%s:%s", game_dir, cur); else snprintf(buf, sizeof(buf), "%s", game_dir);
      setenv("DYLD_LIBRARY_PATH", buf, 1); }
    { char dylib[PATH_MAX]; snprintf(dylib, sizeof(dylib), "%s/libdoorstop.dylib", game_dir);
      const char *cur = getenv("DYLD_INSERT_LIBRARIES");
      if (cur && cur[0]) snprintf(buf, sizeof(buf), "%s:%s", dylib, cur); else snprintf(buf, sizeof(buf), "%s", dylib);
      setenv("DYLD_INSERT_LIBRARIES", buf, 1); }

    // Real binary sits beside us as "<our own basename>.orig" — derived from the launched
    // name (= current CFBundleExecutable), so no game-specific name is baked into the stub.
    char base_copy[PATH_MAX]; strncpy(base_copy, exe, sizeof(base_copy)); base_copy[sizeof(base_copy)-1]=0;
    char real[PATH_MAX]; snprintf(real, sizeof(real), "%s/%s.orig", macos_dir, basename(base_copy));
    execv(real, argv);
    fprintf(stderr, "bpp: execv(%s) failed: %s (run installer Repair)\n", real, strerror(errno));
    return 127;
}
