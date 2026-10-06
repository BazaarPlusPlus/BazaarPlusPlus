#!/usr/bin/env bash
# Which Game Assembly Snapshot is in use: decompile, snapshot, publish, fetch, lock
# check, release pin. Entry points: `just mod::decompile`, `mod::snapshot`,
# `mod::publish`, `mod::fetch`, `mod::lock-check`, and release/payload.mjs
# (`managed-path`). The lock itself is build/game-libs.lock.json (ADR 0004).
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

DECOMPILE_ALL=(Assembly-CSharp BazaarGameClient BazaarGameShared BazaarBattleService TheBazaarRuntime FMODUnity)

# decompile <online|staging|ptr> [DllName ...]: online writes decompiled/, the others
# decompiled-v<channel>/. The channel's lock entry (or, while it is empty, the game
# version of the mounted install) decides which assemblies are read, so one channel's
# bits never land in another channel's reference tree.
cmd_decompile() {
    local channel="${1:-}"
    shift || true
    local out_root
    case "$channel" in
        online) out_root=decompiled ;;
        staging | ptr) out_root="decompiled-v$channel" ;;
        *) die "Usage: just mod::decompile <online|staging|ptr> [DllName ...]" ;;
    esac
    local managed
    managed="$(to_unix_path "$(lock_managed_path "$(host_platform)" "$channel" --allow-unlocked)")"
    if ! command -v ilspycmd >/dev/null 2>&1; then
        step "Installing ilspycmd"
        dotnet tool install -g ilspycmd
    fi
    local dlls=("$@") dll
    ((${#dlls[@]} > 0)) || dlls=(Assembly-CSharp)
    [[ "${dlls[0]}" == "all" ]] && dlls=("${DECOMPILE_ALL[@]}")
    for dll in "${dlls[@]}"; do
        step "Decompiling ${GREEN}${dll}${CYAN} to ${out_root}/${dll}"
        DOTNET_ROLL_FORWARD=Major ilspycmd -p -o "$out_root/$dll" "$managed/$dll.dll"
    done
}

# snapshot: archive the mounted install's Managed dir under game-libs/ keyed by
# platform, channel and game version, and record it in the lock. The game version
# comes from globalgamemanagers above Managed; buildid and branch from the appmanifest
# are the source record. Publish it with `just mod::publish`, then commit the lock.
cmd_snapshot() {
    local managed acf branch buildid
    managed="$(to_unix_path "${BPP_MANAGED_PATH:-$(steam_managed_path)}")"
    [[ -f "${managed:-/nonexistent}/Assembly-CSharp.dll" ]] ||
        die "No mounted game install found. Install The Bazaar through Steam, or set BPP_MANAGED_PATH=/absolute/path/to/Managed."
    acf="$(locate_appmanifest "$managed")"
    [[ -n "$acf" ]] || die "Could not find appmanifest_1617400.acf above '$managed'; a snapshot is captured from a Steam install."
    branch="$(installed_steam_branch "$managed")"
    buildid="$(awk -F '"' '/"buildid"/ {print $4; exit}' "$acf")"
    game_libs_cli snapshot --managed "$managed" --buildid "$buildid" --steam-branch "$branch"
}

# publish <macos|windows> <online|staging|ptr>: upload the captured snapshot of that
# lock entry to the private store. Needs the release credentials (`just mod::publish`
# loads them through the release profile).
cmd_publish() {
    [[ $# -eq 2 ]] || die "Usage: just mod::publish <macos|windows> <online|staging|ptr>"
    game_libs_cli publish --platform "$1" --channel "$2"
}

# fetch <macos|windows> <online|staging|ptr>: make the lock entry's Managed dir
# available locally and print it. Pulling from the private store needs the release
# credentials: `just with-config release just mod::fetch <platform> <channel>`.
cmd_fetch() {
    [[ $# -eq 2 ]] || die "Usage: just mod::fetch <macos|windows> <online|staging|ptr>"
    lock_managed_path "$1" "$2"
}

# lock-check: a malformed lock fails; empty or stale entries warn; the Managed dir the
# build resolves must hash to the lock entry that names its game version.
cmd_lock_check() {
    local managed
    managed="$(to_unix_path "$(managed_path)")"
    if [[ -n "$managed" ]]; then
        game_libs_cli lock-check --managed "$managed"
    else
        game_libs_cli lock-check
    fi
}

# managed-path [-p:ManagedPath=...]: prints the Managed dir a production Payload
# compiles against, and nothing else on stdout. Production Payloads ship to online
# users, so this is the host platform's online lock entry unless overridden.
cmd_managed_path() {
    collect_props "$@"
    local pinned
    pinned="$(prop_value ManagedPath "$@")"
    [[ -n "$pinned" ]] || pinned="$(lock_managed_path "$(host_platform)" online)"
    if command -v cygpath >/dev/null 2>&1; then
        cygpath -am "$pinned"
    else
        (cd "$pinned" && pwd -P)
    fi
}

dispatch "$@"
