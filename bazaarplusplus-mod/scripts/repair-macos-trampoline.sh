#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
    exit 0
fi

GAME_ROOT="${BPP_GAME_ROOT:-$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar}"
TRAMPOLINE_STUB="${BPP_TRAMPOLINE_STUB:-}"

if [[ -z "$TRAMPOLINE_STUB" ]]; then
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    TRAMPOLINE_STUB="$SCRIPT_DIR/../../bazaarplusplus-installer/src-tauri/resources/Trampoline/macos/bpp_launcher"
fi

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

EXE_NAME="$(defaults read "$INFO_PLIST" CFBundleExecutable)"
EXE_PATH="$APP_PATH/Contents/MacOS/$EXE_NAME"
ORIG_PATH="$EXE_PATH.orig"
PREFIX_SCRIPT="$GAME_ROOT/run_bepinex.sh"

links_unity() {
    local path="$1"
    [[ -f "$path" ]] && otool -L "$path" 2>/dev/null | grep -q 'UnityPlayer.dylib'
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

sign_and_verify_bundle() {
    local entitlements
    entitlements="$(mktemp -t bpp-ents-XXXXXX.plist)"
    write_entitlements "$entitlements"
    codesign --force --sign - --entitlements "$entitlements" "$ORIG_PATH" \
        && codesign --force --sign - "$APP_PATH" \
        && codesign --verify --deep --strict "$APP_PATH"
    local status=$?
    rm -f "$entitlements"
    return "$status"
}

restore_vanilla_layout() {
    if [[ -f "$ORIG_PATH" ]]; then
        rm -f "$EXE_PATH"
        mv "$ORIG_PATH" "$EXE_PATH"
        codesign --force --sign - "$APP_PATH" >/dev/null 2>&1 || true
    fi
}

if links_unity "$EXE_PATH"; then
    if pgrep -f "$EXE_PATH" >/dev/null 2>&1; then
        echo "[BPP] Cannot repair macOS trampoline while The Bazaar is running. Close the game and retry." >&2
        exit 1
    fi
    if [[ ! -f "$TRAMPOLINE_STUB" ]]; then
        echo "[BPP] Cannot repair macOS trampoline: missing stub $TRAMPOLINE_STUB" >&2
        exit 1
    fi

    echo "[BPP] Repairing macOS launch trampoline after game executable reverted."
    rm -f "$ORIG_PATH"
    mv "$EXE_PATH" "$ORIG_PATH"
    if ! {
        cp "$TRAMPOLINE_STUB" "$EXE_PATH" \
            && chmod 755 "$EXE_PATH" \
            && { xattr -d com.apple.quarantine "$EXE_PATH" 2>/dev/null || true; } \
            && disable_prefix_launcher \
            && sign_and_verify_bundle
    }; then
        restore_vanilla_layout
        echo "[BPP] macOS trampoline repair failed; restored the vanilla game executable." >&2
        exit 1
    fi

    printf 'trampoline' > "$MARKER"
    echo "[BPP] macOS launch trampoline repaired."
    exit 0
fi

if [[ -f "$ORIG_PATH" ]] && links_unity "$ORIG_PATH"; then
    disable_prefix_launcher
    exit 0
fi

echo "[BPP] Cannot repair macOS trampoline: neither $EXE_PATH nor $ORIG_PATH is the real Unity executable." >&2
exit 1
