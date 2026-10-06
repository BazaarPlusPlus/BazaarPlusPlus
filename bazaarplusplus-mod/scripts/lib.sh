# shellcheck shell=bash
# Shared helpers for the mod command scripts. Sourced, never executed.
# Keep this Bash 3.2 compatible: macOS ships /bin/bash 3.2.

set -euo pipefail

MOD_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$MOD_ROOT"

# Some VPN/tunnel configurations advertise an unusable IPv6 route. Keep builds on
# the working IPv4 path by default while allowing callers to opt back into IPv6.
export DOTNET_SYSTEM_NET_DISABLEIPV6="${DOTNET_SYSTEM_NET_DISABLEIPV6:-1}"

if [[ -t 2 && -z "${NO_COLOR:-}" ]]; then
    CYAN=$'\033[0;36m' GREEN=$'\033[0;32m' RED=$'\033[0;31m' RESET=$'\033[0m'
else
    CYAN='' GREEN='' RED='' RESET=''
fi

# Progress goes to stderr so command substitutions (managed-path) keep stdout clean.
step() { printf '%s== %s ==%s\n' "$CYAN" "$*" "$RESET" >&2; }
ok() { printf '%s%s%s\n' "$GREEN" "$*" "$RESET" >&2; }
err() { printf '%s%s%s\n' "$RED" "$*" "$RESET" >&2; }
die() {
    err "$*"
    exit 1
}

host_platform() {
    case "$(uname -s)" in
        Darwin) echo macos ;;
        MINGW* | MSYS* | CYGWIN*) echo windows ;;
        *) die "Unsupported platform: $(uname -s)" ;;
    esac
}

# Collects MSBuild property arguments into PROPS; anything else is a usage error.
# Callers expand with ${PROPS[@]+"${PROPS[@]}"} because Bash 3.2 treats an empty
# array as unset under `set -u`.
collect_props() {
    PROPS=()
    local arg
    for arg in "$@"; do
        case "$arg" in
            -p:* | --property:*) PROPS+=("$arg") ;;
            *) die "Expected -p:Name=Value, got '$arg'" ;;
        esac
    done
}

# Prints the last value given for property $1 in the remaining arguments.
prop_value() {
    local name="$1" value="" arg
    shift
    for arg in "$@"; do
        case "$arg" in
            -p:"$name"=* | --property:"$name"=*) value="${arg#*=}" ;;
        esac
    done
    printf '%s' "$value"
}

to_unix_path() {
    if [[ -n "$1" ]] && command -v cygpath >/dev/null 2>&1; then
        cygpath -u "$1"
    else
        printf '%s' "$1"
    fi
}

# build/ManagedPath.props owns install discovery and lock resolution; ask it rather
# than restating paths.
discovered_prop() {
    dotnet msbuild build/ManagedPath.props -nologo "-getProperty:$1" | tr -d '\r'
}

# release/game-libs.mjs owns the Snapshot Lock, the Managed digest and the private store.
game_libs_cli() {
    node "$MOD_ROOT/../release/game-libs.mjs" "$@"
}

# The mounted Steam install's Managed dir, or empty; only snapshot and lock
# resolution read it.
steam_managed_path() {
    discovered_prop BppSteamManagedPath
}

# lock_managed_path <platform> <channel> [--allow-unlocked]: the Managed dir that
# satisfies the lock entry, printed on stdout: a verified game-libs/ snapshot, the
# mounted Steam install when its game version and sha256 match, or a fetch from the
# private store. The mounted install only counts for the host platform.
lock_managed_path() {
    local platform="$1" channel="$2"
    shift 2
    local steam=()
    if [[ "$platform" == "$(host_platform)" ]]; then
        steam=(--steam-managed "$(steam_managed_path)")
    fi
    game_libs_cli fetch --platform "$platform" --channel "$channel" \
        ${steam[@]+"${steam[@]}"} "$@"
}

# Game assemblies: explicit -p:ManagedPath, then BPP_MANAGED_PATH, then the lock
# entry build/ManagedPath.props resolves (fetched snapshot or matching Steam install).
managed_path() {
    local explicit
    explicit="$(prop_value ManagedPath "$@")"
    if [[ -n "$explicit" ]]; then
        printf '%s' "$explicit"
    elif [[ -n "${BPP_MANAGED_PATH:-}" ]]; then
        printf '%s' "$BPP_MANAGED_PATH"
    else
        discovered_prop ManagedPath
    fi
}

# Forwards BPP_MANAGED_PATH to MSBuild when the caller did not pass ManagedPath.
managed_props() {
    if [[ -n "${BPP_MANAGED_PATH:-}" && -z "$(prop_value ManagedPath "$@")" ]]; then
        printf '%s\n' "-p:ManagedPath=$BPP_MANAGED_PATH"
    fi
}

game_root() {
    if [[ -n "${BPP_GAME_ROOT:-}" ]]; then
        printf '%s' "$BPP_GAME_ROOT"
    else
        discovered_prop GamePath
    fi
}

# Steam beta branches replace the single install in place, so a snapshot records
# the buildid and branch from the appmanifest as its source. The appmanifest is
# located by walking up from the Managed dir the DLLs are read from.
locate_appmanifest() {
    local dir
    dir="$(to_unix_path "$1")"
    local _i
    for _i in 1 2 3 4 5 6 7 8 9 10; do
        dir="$(dirname "$dir")"
        if [[ -f "$dir/appmanifest_1617400.acf" ]]; then
            echo "$dir/appmanifest_1617400.acf"
            return
        fi
        [[ "$dir" == "/" || "$dir" == "." ]] && break
    done
}

installed_steam_branch() {
    local acf key
    acf="$(locate_appmanifest "$1")"
    [[ -n "$acf" ]] || {
        echo unknown
        return
    }
    key="$(awk '/"MountedConfig"/,/^\t\}/' "$acf" | awk -F '"' '/"BetaKey"/ {print $4}')"
    echo "${key:-public}"
}

# Dispatches `script <command> args...` to the cmd_<command> function.
dispatch() {
    local command="${1:-}"
    local fn="cmd_${command//-/_}"
    if [[ -z "$command" ]] || ! declare -F "$fn" >/dev/null; then
        die "Usage: $0 <command> [args...]; see 'just --list mod'."
    fi
    shift
    "$fn" "$@"
}
