#!/usr/bin/env bash
# Development only: puts sampled cloud runs into a Data Root's history database, or takes them out
# again, so History can be checked on a device without playing. Entry point:
# `just mod::history-sample`. tools/HistorySampler does the reading and writing; this script finds
# the Data Root, keeps the game off it, and mirrors an Android App's files through run-as.
# shellcheck source=scripts/android.sh
source "$(dirname "${BASH_SOURCE[0]}")/android.sh"

SAMPLER_PROJECT=tools/HistorySampler/HistorySampler.csproj
DATA_ROOT_NAME=BazaarPlusPlusV6
DATABASE=bazaarplusplus.db

usage() {
    cat <<'EOF'
Usage: just mod::history-sample <desktop|android> [import|clear] [tool options]

  import (default)  copy recent cloud runs in as the local player's History-Only Runs
  clear             remove every sampled run, its battles, favorites, videos and replays

desktop writes the installed game's Data Root (BPP_GAME_ROOT or the discovered game); quit the game
first. android uses ANDROID_SERIAL or the only connected device: it stops the debug App, copies the
database out, writes it here, and copies it and the replay files back. Start the game again
afterwards (just mod::android-launch). The cloud feed needs the bundle_sync token in
BPP_BUNDLE_SYNC_TOKEN: the root's `just history-sample` supplies it, or pass --bundle FILE.
Tool options (--count, --hours, --bundle, --account, --api):
dotnet run --project tools/HistorySampler -- --help
EOF
}

sampler() {
    dotnet run --project "$MOD_ROOT/$SAMPLER_PROJECT" -v q -- "$@"
}

sample_desktop() {
    local command="$1"
    shift
    if pgrep -f 'TheBazaar.app/Contents/MacOS|The Bazaar.exe' >/dev/null 2>&1; then
        die "The game is running; quit it first, so the mod is not writing the same database."
    fi
    local root
    root="$(game_root)/$DATA_ROOT_NAME"
    [[ -f "$root/$DATABASE" ]] || die "No history database at $root; start the game with the mod once."
    sampler "$command" --data-root "$root" --journal wal "$@"
}

sample_android() {
    local command="$1"
    shift
    require_device
    local remote="$GAME_FILES/$DATA_ROOT_NAME"
    local work="$MOD_ROOT/artifacts/history-sample/$DEVICE_SERIAL"
    rm -rf "$work"
    mkdir -p "$work"
    # The game and the mod run in the App's process; nothing writes the database once it stops.
    device shell am force-stop "$APP_PACKAGE"
    app_cat "$remote/$DATABASE" >"$work/$DATABASE"
    [[ -s "$work/$DATABASE" ]] || die "No history database in the App on $DEVICE_SERIAL; start the game once."
    # A hot rollback journal left by the stop belongs with the database: SQLite rolls it back here.
    if app_sh "test -f $remote/$DATABASE-journal"; then
        app_cat "$remote/$DATABASE-journal" >"$work/$DATABASE-journal"
    fi
    sampler "$command" --data-root "$work" --journal delete --changes "$work/changes.txt" "$@"
    [[ ! -f "$work/$DATABASE-journal" ]] || die "SQLite left a journal in $work; the device was not changed."
    app_put "$work/$DATABASE" "$remote/$DATABASE"
    app_sh "rm -f $remote/$DATABASE-journal $remote/$DATABASE-wal $remote/$DATABASE-shm"
    local action path
    while read -r action path; do
        case "$action" in
            put) app_put "$work/$path" "$remote/$path" ;;
            rm) app_sh "rm -f $remote/$path" ;;
        esac
    done <"$work/changes.txt"
    ok "Updated the App's history on $DEVICE_SERIAL; start the game with: just mod::android-launch"
}

cmd_history_sample() {
    local target="${1:-}"
    [[ "$target" == desktop || "$target" == android ]] || {
        usage
        exit 2
    }
    shift
    local command=import
    if [[ "${1:-}" == import || "${1:-}" == clear ]]; then
        command="$1"
        shift
    fi
    "sample_$target" "$command" "$@"
}

cmd_history_sample "$@"
