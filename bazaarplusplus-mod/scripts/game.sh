#!/usr/bin/env bash
# Which game install or archived snapshot is in use: decompile, snapshot, release pin.
# Entry points: `just mod::decompile`, `mod::snapshot`, and release/payload.mjs (`managed-path`).
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

DECOMPILE_ALL=(Assembly-CSharp BazaarGameClient BazaarGameShared BazaarBattleService TheBazaarRuntime FMODUnity)

require_managed() {
    MANAGED="$(to_unix_path "$(managed_path)")"
    [[ -f "$MANAGED/Assembly-CSharp.dll" ]] ||
        die "Game assemblies not found at '${MANAGED:-<not discovered>}'. Set BPP_MANAGED_PATH=/absolute/path/to/Managed."
}

# decompile <online|ptr> [DllName ...]: online writes decompiled/, ptr writes decompiled-vptr/.
# The Steam branch guard keeps PTR bits out of the online reference and vice versa.
cmd_decompile() {
    local channel="${1:-}"
    shift || true
    local branch out_root
    case "$channel" in
        online) branch=public out_root=decompiled ;;
        ptr) branch=public_test_realm out_root=decompiled-vptr ;;
        *) die "Usage: just mod::decompile <online|ptr> [DllName ...]" ;;
    esac
    require_managed
    require_steam_branch "$branch" "$MANAGED"
    if ! command -v ilspycmd >/dev/null 2>&1; then
        step "Installing ilspycmd"
        dotnet tool install -g ilspycmd
    fi
    local dlls=("$@") dll
    ((${#dlls[@]} > 0)) || dlls=(Assembly-CSharp)
    [[ "${dlls[0]}" == "all" ]] && dlls=("${DECOMPILE_ALL[@]}")
    for dll in "${dlls[@]}"; do
        step "Decompiling ${GREEN}${dll}${CYAN} to ${out_root}/${dll}"
        DOTNET_ROLL_FORWARD=Major ilspycmd -p -o "$out_root/$dll" "$MANAGED/$dll.dll"
    done
}

# snapshot: archive the installed Managed dir keyed by branch + buildid. The two
# branches overwrite each other in place, so this is the only way to keep both
# assembly sets available to release pinning and `just mod::matrix`.
cmd_snapshot() {
    require_managed
    local acf branch channel buildid dest
    acf="$(locate_appmanifest "$MANAGED")"
    [[ -n "$acf" ]] || die "Could not find appmanifest_1617400.acf above '$MANAGED'."
    branch="$(installed_steam_branch "$MANAGED")"
    case "$branch" in
        public) channel=online ;;
        public_test_realm) channel=ptr ;;
        *) die "Installed branch '$branch' is neither public nor public_test_realm; refusing to snapshot." ;;
    esac
    buildid="$(awk -F '"' '/"buildid"/ {print $4; exit}' "$acf")"
    dest="game-libs/$channel-$buildid/Managed"
    if [[ -d "$dest" ]]; then
        echo "Snapshot already exists: $MOD_ROOT/$dest"
        return
    fi
    mkdir -p "$dest"
    cp -R "$MANAGED/." "$dest/"
    ok "Archived $channel (buildid $buildid) Managed -> $MOD_ROOT/$dest"
}

# managed-path [-p:ManagedPath=...]: prints the Managed dir a production Payload
# compiles against, and nothing else on stdout. Production Payloads ship to online
# users, but online and PTR share one install, so pin to the newest online snapshot
# (game-libs/online-*/Managed) when one exists; otherwise require the installed
# branch to be public.
cmd_managed_path() {
    collect_props "$@"
    local pinned
    pinned="$(prop_value ManagedPath "$@")"
    if [[ -z "$pinned" ]]; then
        local snap
        for snap in game-libs/online-*/Managed; do
            [[ -d "$snap" ]] && pinned="$MOD_ROOT/$snap"
        done
    fi
    if [[ -z "$pinned" ]]; then
        pinned="$(managed_path)"
        [[ -n "$pinned" ]] || die "Game assemblies not found. Pass -p:ManagedPath=/absolute/path/to/Managed."
        require_steam_branch public "$pinned"
    fi
    if command -v cygpath >/dev/null 2>&1; then
        cygpath -am "$pinned"
    else
        (cd "$pinned" && pwd -P)
    fi
}

dispatch "$@"
