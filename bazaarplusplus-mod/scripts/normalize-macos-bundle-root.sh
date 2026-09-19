#!/usr/bin/env bash
set -euo pipefail
shopt -s nullglob dotglob

# Keep this backup format aligned with the installer's bepinex/bundle_root.rs.
# The whole tree is hashed only during a repair; no game files are discarded
# unless another verified copy is already preserved outside the application.
GAME_ROOT="${1:?Usage: normalize-macos-bundle-root.sh GAME_ROOT}"
APP_PATH="$GAME_ROOT/TheBazaar.app"
SOURCE_PATH="$APP_PATH/TheBazaar_ARM64.app"
BACKUPS="$GAME_ROOT/.bpp-bundle-root-stash"

fail() { echo "[BPP] $*" >&2; exit 1; }
real_dir() { [[ -d "$1" && ! -L "$1" ]] || fail "Expected a real directory: $1"; }
plist() { /usr/bin/plutil -extract "$2" raw -o - "$1/Contents/Info.plist"; }

bundle_digest() (
    real_dir "$1"
    local manifest listing hashes item
    manifest="$(mktemp -t bpp-bundle-manifest)" || exit 1
    listing="$(mktemp -t bpp-bundle-listing)" || exit 1
    hashes="$(mktemp -t bpp-bundle-hashes)" || exit 1
    trap 'rm -f -- "$manifest" "$listing" "$hashes"' EXIT
    cd "$1" || exit 1
    /usr/bin/find . -print0 > "$listing" || exit 1
    while IFS= read -r -d '' item; do
        case "$item" in *$'\n'*|*$'\r'*|*\\*) fail "Unsupported backup path: $item" ;; esac
        [[ ! -L "$item" ]] || fail "Unsupported symbolic link: $item"
        if [[ -d "$item" ]]; then
            printf 'D %s\n' "$item" >> "$manifest" || exit 1
        elif [[ ! -f "$item" ]]; then
            fail "Unsupported backup file: $item"
        fi
    done < "$listing"
    /usr/bin/find . -type f -exec /usr/bin/shasum -a 256 {} + > "$hashes" || exit 1
    sed 's/^/F /' "$hashes" >> "$manifest" || exit 1
    LC_ALL=C sort "$manifest" | /usr/bin/shasum -a 256 | awk '{print $1}' || exit 1
)

write_current() {
    printf '%s\n' "$1" > "$BACKUPS/current.tmp"
    mv -f "$BACKUPS/current.tmp" "$BACKUPS/current"
}

retire() {
    local tombstone="$BACKUPS/.delete-$2.app"
    mv "$1" "$tombstone"
    rm -rf -- "$tombstone"
}

real_dir "$APP_PATH"
real_dir "$APP_PATH/Contents"
for entry in "$APP_PATH"/*; do
    case "${entry##*/}" in
        Contents|TheBazaar_ARM64.app) ;;
        .DS_Store) [[ -f "$entry" && ! -L "$entry" ]] || fail "Invalid Finder metadata: $entry" ;;
        *) fail "Unexpected application root entry $entry; move it outside TheBazaar.app before repairing" ;;
    esac
done

incoming=""
if [[ -e "$SOURCE_PATH" || -L "$SOURCE_PATH" ]]; then
    real_dir "$SOURCE_PATH"
    for key in CFBundleIdentifier CFBundleExecutable CFBundleShortVersionString; do
        value="$(plist "$APP_PATH" "$key")"
        [[ -n "$value" && "$value" == "$(plist "$SOURCE_PATH" "$key")" ]] || fail "Unexpected $key in $SOURCE_PATH"
        if [[ "$key" == CFBundleIdentifier ]]; then
            [[ "$value" == com.TempoStorm.TheBazaar ]] || fail "Unrecognized game bundle: $APP_PATH"
        fi
    done
    for relative in Contents/Frameworks/UnityPlayer.dylib Contents/Frameworks/libmonobdwgc-2.0.dylib Contents/Resources/Data/boot.config; do
        cmp -s "$APP_PATH/$relative" "$SOURCE_PATH/$relative" || fail "Duplicate game bundle differs at $relative; no files were moved"
    done
    incoming="$(bundle_digest "$SOURCE_PATH")"
elif [[ ! -e "$BACKUPS" && ! -L "$BACKUPS" ]]; then
    exit 0
fi

[[ -e "$BACKUPS" || -L "$BACKUPS" ]] || mkdir "$BACKUPS"
real_dir "$BACKUPS"
legacy=""
snapshots=()
tombstones=()
for entry in "$BACKUPS"/*; do
    name="${entry##*/}"
    case "$name" in
        .DS_Store)
            [[ -f "$entry" && ! -L "$entry" ]] || fail "Invalid Finder metadata: $entry"
            ;;
        current|current.tmp)
            [[ -f "$entry" && ! -L "$entry" ]] || fail "Invalid backup record: $entry"
            ;;
        TheBazaar_ARM64.app)
            real_dir "$entry"
            [[ "$(plist "$entry" CFBundleIdentifier)" == com.TempoStorm.TheBazaar ]] || fail "Unrecognized legacy backup: $entry"
            legacy="$(bundle_digest "$entry")"
            ;;
        .delete-*)
            [[ "$name" =~ ^\.delete-[a-f0-9]{64}\.app$ ]] || fail "Unexpected backup entry: $entry"
            real_dir "$entry"
            tombstones+=("$entry")
            ;;
        *)
            [[ "$name" =~ ^[a-f0-9]{64}\.app$ ]] || fail "Unexpected backup entry: $entry"
            digest="$(bundle_digest "$entry")"
            [[ "$name" == "$digest.app" ]] || fail "Backup was modified; preserve or move $entry before repairing"
            snapshots+=("$entry")
            ;;
    esac
done
if [[ ${#tombstones[@]} -gt 0 ]]; then
    [[ ${#snapshots[@]} -gt 0 ]] || fail "No verified backup remains in $BACKUPS; pending deletion was preserved"
    for entry in "${tombstones[@]}"; do rm -rf -- "$entry"; done
fi

if [[ -n "$legacy" ]]; then
    target="$BACKUPS/$legacy.app"
    if [[ -z "$incoming" && ! -e "$BACKUPS/current" ]]; then
        write_current "$legacy"
    fi
    if [[ -e "$target" ]]; then
        retire "$BACKUPS/TheBazaar_ARM64.app" "$legacy"
    else
        mv "$BACKUPS/TheBazaar_ARM64.app" "$target"
        snapshots+=("$target")
    fi
fi

if [[ -n "$incoming" ]]; then
    write_current "$incoming"
    target="$BACKUPS/$incoming.app"
    if [[ -e "$target" ]]; then
        retire "$SOURCE_PATH" "$incoming"
    else
        mv "$SOURCE_PATH" "$target"
    fi
    current="$incoming"
elif [[ ${#snapshots[@]} -eq 0 ]]; then
    exit 0
elif [[ ${#snapshots[@]} -eq 1 && ! -e "$BACKUPS/current" ]]; then
    current="${snapshots[0]##*/}"
    current="${current%.app}"
    write_current "$current"
else
    current="$(cat "$BACKUPS/current")"
fi

[[ "$current" =~ ^[a-f0-9]{64}$ && -d "$BACKUPS/$current.app" ]] || fail "Incomplete backup record in $BACKUPS; no backups were removed"
if [[ ${#snapshots[@]} -gt 0 ]]; then
    for entry in "${snapshots[@]}"; do
        if [[ "$entry" != "$BACKUPS/$current.app" ]]; then
            name="${entry##*/}"
            retire "$entry" "${name%.app}"
        fi
    done
fi
