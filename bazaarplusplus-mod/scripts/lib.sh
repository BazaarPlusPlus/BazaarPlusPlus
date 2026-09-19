# shellcheck shell=bash
# Shared helpers for the mod command scripts. Sourced, never executed.
# Keep this Bash 3.2 compatible: macOS /bin/bash runs it in MacosTrampolineRepair.Tests.

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

# build/ManagedPath.props owns install discovery; ask it rather than restating paths.
discovered_prop() {
    dotnet msbuild build/ManagedPath.props -nologo "-getProperty:$1" | tr -d '\r'
}

# Game assemblies: explicit -p:ManagedPath, then BPP_MANAGED_PATH, then discovery.
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

# Steam beta branches ("public_test_realm" = PTR) replace the single install in
# place, so the Managed dir silently changes identity on branch switch. The
# appmanifest is located by walking up from the Managed dir the DLLs are read from.
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

# require_steam_branch <expected> <managed-dir>
require_steam_branch() {
    [[ "${BPP_SKIP_BRANCH_CHECK:-}" == "1" ]] && return 0
    local branch
    branch="$(installed_steam_branch "$2")"
    if [[ "$branch" == "unknown" ]]; then
        die "Could not find appmanifest_1617400.acf above '$2' to verify the Steam branch." \
            $'\n'"Using a bare copied Managed dir? Set BPP_SKIP_BRANCH_CHECK=1 to override."
    fi
    if [[ "$branch" != "$1" ]]; then
        die "Installed Steam branch is '$branch', expected '$1'." \
            $'\n'"Switch The Bazaar's beta branch in Steam first, or set BPP_SKIP_BRANCH_CHECK=1 to override."
    fi
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
