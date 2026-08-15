#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
    exit 0
fi

GAME_ROOT="${BPP_GAME_ROOT:-$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar}"
TRAMPOLINE_STUB="${BPP_TRAMPOLINE_STUB:-}"

MARKER="$GAME_ROOT/.bpp-launch-mode"
if [[ ! -f "$MARKER" ]]; then
    exit 0
fi

MODE="$(tr -d '[:space:]' < "$MARKER")"
if [[ "$MODE" != "trampoline" ]]; then
    exit 0
fi

APP_PATH="$GAME_ROOT/TheBazaar.app"
INFO_PLIST="$APP_PATH/Contents/Info.plist"
if [[ ! -d "$APP_PATH" || ! -f "$INFO_PLIST" ]]; then
    echo "[BPP] Cannot repair macOS trampoline: TheBazaar.app is missing under $GAME_ROOT" >&2
    exit 1
fi

EXE_NAME="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$INFO_PLIST")"
EXE_PATH="$APP_PATH/Contents/MacOS/$EXE_NAME"
ORIG_PATH="$EXE_PATH.orig"
STAGED_STUB="$EXE_PATH.bpp-stub"
PREFIX_SCRIPT="$GAME_ROOT/run_bepinex.sh"
BUNDLE_ROOT_STASH="$GAME_ROOT/.bpp-bundle-root-stash"

links_unity() {
    local path="$1"
    [[ -f "$path" ]] && otool -L "$path" 2>/dev/null | grep -q 'UnityPlayer.dylib'
}

game_running() {
    # pgrep -f patterns are EREs matched as substrings; escape the path and
    # anchor it so unrelated command lines containing it don't abort the repair.
    local escaped
    escaped="$(printf '%s' "$EXE_PATH" | sed -e 's/[^^]/[&]/g' -e 's/\^/\\^/g')"
    pgrep -f "^${escaped}( |\$)" >/dev/null 2>&1
}

disable_prefix_launcher() {
    if [[ -f "$PREFIX_SCRIPT" ]]; then
        chmod a-x "$PREFIX_SCRIPT" 2>/dev/null || true
    fi
}

write_entitlements() {
    local path="$1"
    cat > "$path" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-jit</key><true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
    <key>com.apple.security.cs.disable-library-validation</key><true/>
</dict>
</plist>
EOF
}

# codesign refuses to sign *or* verify an app bundle that holds anything other
# than Contents/ at its root ("unsealed contents present in the bundle root").
# The Bazaar ships TheBazaar_ARM64.app there, so every codesign call has to run
# with the bundle root emptied out. Stash outside the .app: writing into the
# bundle is what breaks re-signing in the first place.
stash_bundle_root_extras() {
    local entry name
    for entry in "$APP_PATH"/* "$APP_PATH"/.[!.]*; do
        [[ -e "$entry" ]] || continue
        name="$(basename "$entry")"
        [[ "$name" == "Contents" ]] && continue
        mkdir -p "$BUNDLE_ROOT_STASH"
        rm -rf "${BUNDLE_ROOT_STASH:?}/$name"
        mv "$entry" "$BUNDLE_ROOT_STASH/$name"
    done
}

restore_bundle_root_extras() {
    [[ -d "$BUNDLE_ROOT_STASH" ]] || return 0
    local entry name
    for entry in "$BUNDLE_ROOT_STASH"/* "$BUNDLE_ROOT_STASH"/.[!.]*; do
        [[ -e "$entry" ]] || continue
        name="$(basename "$entry")"
        rm -rf "${APP_PATH:?}/$name"
        mv "$entry" "$APP_PATH/$name"
    done
    rmdir "$BUNDLE_ROOT_STASH" 2>/dev/null || true
}

sign_and_verify_bundle() {
    local entitlements
    entitlements="$(mktemp -t bpp-ents)"
    write_entitlements "$entitlements"
    stash_bundle_root_extras
    codesign --force --sign - --entitlements "$entitlements" "$ORIG_PATH" \
        && codesign --force --sign - "$APP_PATH" \
        && codesign --verify --deep --strict "$APP_PATH"
    local status=$?
    restore_bundle_root_extras
    rm -f "$entitlements"
    return "$status"
}

restore_vanilla_layout() {
    if [[ -f "$ORIG_PATH" ]]; then
        rm -f "$EXE_PATH"
        mv "$ORIG_PATH" "$EXE_PATH"
        stash_bundle_root_extras
        codesign --force --sign - "$APP_PATH" >/dev/null 2>&1 || true
        restore_bundle_root_extras
    fi
}

cleanup_failed_repair() {
    restore_bundle_root_extras
    rm -f "$STAGED_STUB"
    restore_vanilla_layout
}

# An earlier run killed mid-signing leaves the bundle-root entries stashed.
restore_bundle_root_extras

if links_unity "$EXE_PATH"; then
    if game_running; then
        echo "[BPP] Cannot repair macOS trampoline while The Bazaar is running. Close the game and retry." >&2
        exit 1
    fi
    if [[ ! -f "$TRAMPOLINE_STUB" ]]; then
        echo "[BPP] Cannot repair macOS trampoline: missing stub '$TRAMPOLINE_STUB' (set BPP_TRAMPOLINE_STUB)" >&2
        exit 1
    fi

    echo "[BPP] Repairing macOS launch trampoline after game executable reverted."
    # From here until the trap is cleared, any exit (failure, Ctrl-C, kill)
    # must put the vanilla executable back. ORIG_PATH is deleted before the
    # first mutation so restore_vanilla_layout stays a no-op until EXE_PATH
    # has actually been moved aside.
    trap cleanup_failed_repair EXIT
    trap 'exit 1' INT TERM
    rm -f "$ORIG_PATH" "$STAGED_STUB"
    cp "$TRAMPOLINE_STUB" "$STAGED_STUB"
    chmod 755 "$STAGED_STUB"
    xattr -d com.apple.quarantine "$STAGED_STUB" 2>/dev/null || true
    mv "$EXE_PATH" "$ORIG_PATH"
    if ! {
        mv "$STAGED_STUB" "$EXE_PATH" \
            && disable_prefix_launcher \
            && sign_and_verify_bundle
    }; then
        echo "[BPP] macOS trampoline repair failed; restoring the vanilla game executable." >&2
        exit 1
    fi
    trap - EXIT INT TERM

    echo "[BPP] macOS launch trampoline repaired."
    exit 0
fi

if [[ -f "$ORIG_PATH" ]] && links_unity "$ORIG_PATH"; then
    disable_prefix_launcher
    # Native Unity plugins live inside the app bundle. A Debug build may refresh one after the
    # trampoline was installed, invalidating the outer resource seal; re-sign the real binary
    # with its required entitlements and reseal the app before returning.
    sign_and_verify_bundle
    exit 0
fi

echo "[BPP] Cannot repair macOS trampoline: neither $EXE_PATH nor $ORIG_PATH is the real Unity executable." >&2
exit 1
