#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"

# Output is lib-prefixed (libBppMacAudio.dylib) so [DllImport("BppMacAudio")]
# resolves on Unity-Mono via the proven lib{name}.dylib probe, exactly like
# libe_sqlite3.dylib.
#
# -mmacosx-version-min=11.0 (NOT 14.2) is required: it weak-imports the 14.2 tap
# symbols so the dylib LOADS on macOS 13/14 and IsSupported cleanly returns false
# there. The tap symbols are only ever called after the >=15 gate passes.
clang -arch arm64 -fobjc-arc \
  -framework CoreAudio -framework Foundation \
  -mmacosx-version-min=11.0 \
  -O2 -dynamiclib -o libBppMacAudio.dylib BppMacAudio.m

echo "built libBppMacAudio.dylib"

# Copy the prebuilt into the installer repo — the location the mod build actually consumes
# (BazaarPlusPlus.csproj -> BPPInstallerSourcePath), alongside libe_sqlite3.dylib. The default
# assumes the standard sibling-repo workspace layout; override with BPP_INSTALLER_PLUGINS_DIR.
# When the installer repo is absent (e.g. a mod-only checkout) the copy is skipped cleanly — the
# dylib still builds locally and this is not an error.
dest_dir="${BPP_INSTALLER_PLUGINS_DIR:-../../../bazaarplusplus-installer/src-tauri/resources/SourceForBuild/macos/BepInEx/plugins}"
if [ -d "$dest_dir" ]; then
  cp libBppMacAudio.dylib "$dest_dir/libBppMacAudio.dylib"
  echo "copied libBppMacAudio.dylib -> $dest_dir/"
  echo "  (remember to commit the refreshed dylib in the installer repo if it changed)"
else
  echo "note: installer plugins dir not found, skipped copy:"
  echo "      $dest_dir"
  echo "      set BPP_INSTALLER_PLUGINS_DIR, or copy libBppMacAudio.dylib there before building the mod."
fi
