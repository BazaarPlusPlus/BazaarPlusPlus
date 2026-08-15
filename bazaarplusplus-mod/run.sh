#!/usr/bin/env bash
set -euo pipefail

CYAN='\033[0;36m'
GREEN='\033[0;32m'
RED='\033[0;31m'
RESET='\033[0m'

# Some VPN/tunnel configurations advertise an unusable IPv6 route. Keep builds on
# the working IPv4 path by default while allowing callers to opt back into IPv6.
export DOTNET_SYSTEM_NET_DISABLEIPV6="${DOTNET_SYSTEM_NET_DISABLEIPV6:-1}"

case "$(uname -s)" in
    Darwin)
        PLATFORM="macOS"
        GAME_ROOT="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar"
        MANAGED="$HOME/Library/Application Support/Steam/steamapps/common/The Bazaar/TheBazaar.app/Contents/Resources/Data/Managed"
        ;;
    MINGW*|MSYS*|CYGWIN*)
        PLATFORM="Windows (Git Bash)"
        GAME_ROOT="/c/Program Files (x86)/Steam/steamapps/common/The Bazaar"
        MANAGED="/c/Program Files (x86)/Steam/steamapps/common/The Bazaar/TheBazaar_Data/Managed"
        ;;
    *)
        echo -e "${RED}Unsupported platform: $(uname -s)${RESET}" >&2
        exit 1
        ;;
esac

echo -e "${CYAN}== Building on ${GREEN}${PLATFORM}${CYAN} ==${RESET}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME_ROOT="${BPP_GAME_ROOT:-$GAME_ROOT}"
MANAGED="${BPP_MANAGED_PATH:-$MANAGED}"
INSTALLER_SOURCE="${BPP_INSTALLER_SOURCE_PATH:-$SCRIPT_DIR/../bazaarplusplus-installer/src-tauri/resources}"
GAME_SQLITE="$GAME_ROOT/BepInEx/plugins/libe_sqlite3.dylib"
TRAMPOLINE_REPAIR_SCRIPT="$SCRIPT_DIR/scripts/repair-macos-trampoline.sh"
PUBLISHED_PROJECTS=(
    src/BazaarPlusPlus.Localization/BazaarPlusPlus.Localization.csproj
    src/BazaarPlusPlus.ModApi/BazaarPlusPlus.ModApi.csproj
    src/BazaarPlusPlus.Storage/BazaarPlusPlus.Storage.csproj
    src/BazaarPlusPlus/BazaarPlusPlus.csproj
    src/BazaarPlusPlus.BazaarAgent/BazaarPlusPlus.BazaarAgent.csproj
    src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj
)

clear_macos_sqlite_quarantine() {
    [[ "$PLATFORM" == "macOS" ]] || return 0

    local installer_source="${1:-$INSTALLER_SOURCE}"
    local target
    for target in "$installer_source/SourceForBuild/macos/BepInEx/plugins/libe_sqlite3.dylib" "$GAME_SQLITE"; do
        [[ -f "$target" ]] || continue
        xattr -d com.apple.quarantine "$target" 2>/dev/null || true
    done
}

print_bazaaragent_mode() {
    local bazaaragent="${1:-false}"
    if [[ "$bazaaragent" == "true" ]]; then
        echo -e "${CYAN}== BazaarAgent: ${GREEN}included${CYAN} ==${RESET}"
    else
        echo -e "${CYAN}== BazaarAgent: excluded ==${RESET}"
    fi
}

repair_macos_trampoline() {
    [[ "$PLATFORM" == "macOS" ]] || return 0

    local installer_source="${1:-$INSTALLER_SOURCE}"
    local trampoline_stub="${BPP_TRAMPOLINE_STUB:-$installer_source/Trampoline/macos/bpp_launcher}"
    BPP_GAME_ROOT="$GAME_ROOT" \
        BPP_TRAMPOLINE_STUB="$trampoline_stub" \
        bash "$TRAMPOLINE_REPAIR_SCRIPT"
}

build() {
    local bazaaragent="${1:-false}"
    local fast="${2:-false}"
    shift 2 || true
    local msbuild_args=("$@")
    local args=()

    if [[ "$fast" == "true" ]]; then
        # Inner-loop accelerator: skips NuGet restore. Run a normal build after
        # editing any csproj or creating a fresh worktree.
        args+=(--no-restore)
    fi

    print_bazaaragent_mode "$bazaaragent"
    repair_macos_trampoline
    # The host is its own plugin project that references the main plugin + the pure core,
    # so building it builds and deploys all three. A default build builds only the main
    # plugin, whose build actively scrubs both host dlls from the plugins folder.
    if [[ "$bazaaragent" == "true" ]]; then
        dotnet build src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj \
            ${args[@]+"${args[@]}"} ${msbuild_args[@]+"${msbuild_args[@]}"} \
            -p:BppDeployToGame=true -p:GamePath="$GAME_ROOT" \
            -p:RequireBazaarAgentDashboard=true
        # The main plugin deliberately scrubs optional host DLLs in its own Debug target.
        # Re-run only the host copy target after the dependency graph has fully settled.
        dotnet msbuild src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj \
            -t:CopyHostToBepInExPlugins -p:BppDeployToGame=true -p:GamePath="$GAME_ROOT"
    else
        dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj \
            ${args[@]+"${args[@]}"} ${msbuild_args[@]+"${msbuild_args[@]}"} \
            -p:BppDeployToGame=true -p:GamePath="$GAME_ROOT"
    fi
}

# Production publishing copies the DLL into the installer resources that ship to
# ONLINE users, but online/PTR share one install directory — a publish run while the
# PTR branch is installed would ship a PTR-assembly build. Pin production builds to
# an online Managed snapshot (game-libs/online-*/Managed, newest) when one exists;
# otherwise require the installed branch to actually be public.
resolve_release_managed() {
    local pinned="${BPP_RELEASE_MANAGED:-}"
    if [[ -n "$pinned" ]]; then
        echo "$pinned"
        return
    fi
    local snaps=("$SCRIPT_DIR"/game-libs/online-*/Managed)
    local last=""
    local snap
    for snap in "${snaps[@]}"; do
        [[ -d "$snap" ]] && last="$snap"
    done
    echo "$last"
}

resolve_installer_source() {
    local resolved="$INSTALLER_SOURCE"
    local arg
    for arg in "$@"; do
        case "$arg" in
            -p:BPPInstallerSourcePath=*|--property:BPPInstallerSourcePath=*)
                resolved="${arg#*=}"
                ;;
        esac
    done
    echo "$resolved"
}

resolve_release_platform() {
    case "$PLATFORM" in
        macOS) echo "macos" ;;
        "Windows (Git Bash)") echo "windows" ;;
        *)
            echo -e "${RED}Unsupported native release platform: $PLATFORM${RESET}" >&2
            return 1
            ;;
    esac
}

ensure_native_release_inputs() {
    local installer_source="$1"
    local release_platform="$2"

    local installer_root
    installer_root="$(cd "$installer_source/../.." && pwd)"
    local coordinator="$installer_root/scripts/release/native-recorder-input.mjs"
    if [[ ! -f "$coordinator" ]]; then
        echo -e "${RED}Native input coordinator not found at '$coordinator'.${RESET}" >&2
        return 1
    fi
    if ! command -v node &>/dev/null; then
        echo -e "${RED}Node.js is required to verify and promote native release inputs.${RESET}" >&2
        return 1
    fi

    echo -e "${CYAN}== Verifying ${GREEN}${release_platform}${CYAN} native release inputs ==${RESET}"
    node "$coordinator" ensure --platform "$release_platform" --source-root "$SCRIPT_DIR"
}

prepare_installer_resource_archives() {
    local installer_source="$1"
    local release_platform="$2"
    local installer_root
    installer_root="$(cd "$installer_source/../.." && pwd)"
    echo -e "${CYAN}== Preparing ${GREEN}${release_platform}${CYAN} installer resource archive ==${RESET}"
    npm --prefix "$installer_root" run prepare:resources -- --platform "$release_platform"
}

fetch_remote_data() {
    local canonical_directory="$SCRIPT_DIR/src/BazaarPlusPlus/obj/remote-data"
    local arg
    for arg in "$@"; do
        case "$arg" in
            -p:RemoteEmbeddedDataDirectory=*|--property:RemoteEmbeddedDataDirectory=*)
                canonical_directory="${arg#*=}"
                ;;
        esac
    done

    local canonical_parent
    canonical_parent="$(dirname "$canonical_directory")"
    mkdir -p "$canonical_parent"
    local staging_directory
    staging_directory="$(mktemp -d "$canonical_parent/.remote-data-stage.XXXXXX")"
    local status=0

    if dotnet msbuild src/BazaarPlusPlus/BazaarPlusPlus.csproj \
        -t:FetchRemoteEmbeddedData \
        "$@" \
        -p:RemoteEmbeddedDataDirectory="$staging_directory" \
        -p:ForceRemoteEmbeddedDataRefresh=true; then
        if run_seed_gates \
            "$@" \
            -p:RemoteEmbeddedDataDirectory="$staging_directory" \
            -p:RemoteEmbeddedDataPrepared=true; then
            if dotnet run \
                --project build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetcher.csproj \
                --no-launch-profile -- \
                promote "$staging_directory" "$canonical_directory" \
                voice-lines.json builds.json; then
                :
            else
                status=$?
            fi
        else
            status=$?
        fi
    else
        status=$?
    fi

    rm -rf -- "$staging_directory"
    return "$status"
}

run_seed_gates() {
    echo -e "${CYAN}== Validating ${GREEN}voice subtitle embedded seed${CYAN} ==${RESET}"
    dotnet test tests/RuntimeIntegration.Tests/RuntimeIntegration.Tests.csproj \
        -c Release \
        --filter "TestKind=EmbeddedSeed" \
        "$@" || return $?
    echo -e "${CYAN}== Validating ${GREEN}live build recommendation embedded seed${CYAN} ==${RESET}"
    dotnet test tests/ScenarioRunner.Tests/ScenarioRunner.Tests.csproj \
        -c Release \
        --filter "TestKind=EmbeddedSeed" \
        -p:BppSeedGateOnly=true \
        "$@" || return $?
}

publish() {
    local bazaaragent="${1:-false}"
    shift || true
    local passthrough_args=("$@")
    local installer_source
    installer_source=$(resolve_installer_source ${passthrough_args[@]+"${passthrough_args[@]}"})
    if [[ ! -d "$installer_source" ]]; then
        echo -e "${RED}Installer resources not found at '$installer_source'.${RESET}" >&2
        echo -e "${RED}Pass -p:BPPInstallerSourcePath=/absolute/path/to/resources.${RESET}" >&2
        exit 1
    fi

    local release_platform
    release_platform=$(resolve_release_platform)

    local common_args=(
        ${passthrough_args[@]+"${passthrough_args[@]}"}
        "-p:BPPInstallerSourcePath=$installer_source"
        "-p:BppReleasePlatform=$release_platform"
    )

    local release_managed=""
    release_managed=$(resolve_release_managed)
    if [[ -n "$release_managed" ]]; then
        echo -e "${CYAN}== Release pinned to ${GREEN}${release_managed}${CYAN} ==${RESET}"
        common_args+=("-p:ManagedPath=$release_managed")
    else
        require_steam_branch public
    fi

    print_bazaaragent_mode "$bazaaragent"
    ensure_native_release_inputs "$installer_source" "$release_platform"
    clear_macos_sqlite_quarantine "$installer_source"
    repair_macos_trampoline "$installer_source"

    fetch_remote_data "${common_args[@]}"

    local build_args=(
        -t:BuildAll
        "${common_args[@]}"
        -p:BuildProductionPackage=true
        -p:RemoteEmbeddedDataPrepared=true
    )
    if [[ "$bazaaragent" == "true" ]]; then
        dotnet build src/BazaarPlusPlus.BazaarAgentHost/BazaarPlusPlus.BazaarAgentHost.csproj "${build_args[@]}" \
            -p:RequireBazaarAgentDashboard=true
    else
        dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj "${build_args[@]}"
    fi
    prepare_installer_resource_archives "$installer_source" "$release_platform"
    clear_macos_sqlite_quarantine "$installer_source"
}

parse_build_options() {
    local bazaaragent=false
    local fast=false
    local msbuild_args=()

    while (($# > 0)); do
        case "$1" in
            --with-bazaaragent) bazaaragent=true ;;
            --fast) fast=true ;;
            -p:*|--property:*) msbuild_args+=("$1") ;;
            *)
                usage
                exit 1
                ;;
        esac
        shift
    done

    build "$bazaaragent" "$fast" ${msbuild_args[@]+"${msbuild_args[@]}"}
}

parse_publish_options() {
    local bazaaragent=false
    local msbuild_args=()

    while (($# > 0)); do
        case "$1" in
            --with-bazaaragent) bazaaragent=true ;;
            -p:*|--property:*) msbuild_args+=("$1") ;;
            *)
                usage
                exit 1
                ;;
        esac
        shift
    done

    publish "$bazaaragent" ${msbuild_args[@]+"${msbuild_args[@]}"}
}

parse_fetch_data_options() {
    local msbuild_args=()

    while (($# > 0)); do
        case "$1" in
            -p:*|--property:*) msbuild_args+=("$1") ;;
            *)
                usage
                exit 1
                ;;
        esac
        shift
    done

    fetch_remote_data ${msbuild_args[@]+"${msbuild_args[@]}"}
}

test_common_args() {
    local remote_data="$SCRIPT_DIR/tests/TestData/remote-data"
    printf '%s\n' \
        "-p:BppDeployToGame=false" \
        "-p:ForceRemoteEmbeddedDataRefresh=false" \
        "-p:RemoteEmbeddedDataDirectory=$remote_data"
}

require_managed_for_tests() {
    if [[ ! -f "$MANAGED/Assembly-CSharp.dll" ]]; then
        echo -e "${RED}Game assemblies not found at '$MANAGED'.${RESET}" >&2
        echo -e "${RED}Pass -p:ManagedPath=/absolute/path/to/Managed.${RESET}" >&2
        return 1
    fi
    echo -e "${CYAN}== Test game assemblies: ${GREEN}${MANAGED}${CYAN} ==${RESET}"
}

resolve_test_managed_arg() {
    local arg
    for arg in "$@"; do
        case "$arg" in
            -p:ManagedPath=*|--property:ManagedPath=*)
                MANAGED="${arg#*=}"
                ;;
        esac
    done
}

test_all() {
    resolve_test_managed_arg "$@"
    require_managed_for_tests || return $?
    local common_args=()
    while IFS= read -r arg; do common_args+=("$arg"); done < <(test_common_args)

    dotnet test tests/BazaarPlusPlus.Tests.slnx \
        "${common_args[@]}" "$@"
}

test_compat() {
    resolve_test_managed_arg "$@"
    local common_args=()
    while IFS= read -r arg; do common_args+=("$arg"); done < <(test_common_args)
    local decompiled_root="${BPP_DECOMPILED_SOURCE_ROOT:-$SCRIPT_DIR}"
    local runnable=0
    local checked=0
    local failures=()
    local requirement label project ready

    echo -e "${CYAN}== Compatibility preflight ==${RESET}"
    while IFS='|' read -r requirement label project; do
        [[ -n "$requirement" && "$requirement" != \#* ]] || continue
        ready=false
        case "$requirement" in
            managed)
                [[ -f "$MANAGED/Assembly-CSharp.dll" ]] && ready=true
                ;;
            decompiled-online)
                [[ -f "$SCRIPT_DIR/decompiled/TheBazaarRuntime/AssetLoader.cs" ]] && ready=true
                ;;
            decompiled-any)
                [[ -d "$decompiled_root/decompiled/TheBazaarRuntime" \
                    || -d "$decompiled_root/decompiled-vptr/TheBazaarRuntime" ]] && ready=true
                ;;
            *)
                echo -e "${RED}Unknown compatibility requirement '$requirement'.${RESET}" >&2
                return 1
                ;;
        esac

        if [[ "$ready" != "true" ]]; then
            printf '  SKIPPED %-34s requirement=%s\n' "$label" "$requirement"
            continue
        fi

        printf '  CHECKED %-34s project=%s\n' "$label" "$project"
        ((runnable += 1))
        if dotnet run --project "$project" "${common_args[@]}" "$@"; then
            ((checked += 1))
        else
            failures+=("$label")
        fi
    done < tests/CompatibilityTests.manifest

    if ((runnable == 0)); then
        echo -e "${RED}No compatibility checks were runnable; nothing was verified.${RESET}" >&2
        return 2
    fi
    if ((${#failures[@]} > 0)); then
        printf '%s\n' "${failures[@]}" | sed 's/^/  FAILED /' >&2
        return 1
    fi
    echo -e "${GREEN}Compatibility checks passed: ${checked}/${runnable}.${RESET}"
}

test_corpus() {
    if (($# < 1 || $# > 2)); then
        echo -e "${RED}Usage: ./run.sh test-corpus <replay-corpus-path> [report-path]${RESET}" >&2
        return 2
    fi
    local corpus_path="$1"
    if [[ ! -d "$corpus_path" ]]; then
        echo -e "${RED}Replay corpus directory not found: '$corpus_path'.${RESET}" >&2
        return 2
    fi
    local report_path="${2:-$SCRIPT_DIR/artifacts/combat-impact-corpus/report.json}"
    dotnet run --project tests/CombatImpact.Corpus/CombatImpact.Corpus.csproj \
        -p:BppDeployToGame=false -p:ManagedPath="$MANAGED" \
        -- "$corpus_path" "$report_path"
}

parse_test_options() {
    local command="$1"
    shift
    local msbuild_args=()
    while (($# > 0)); do
        case "$1" in
            -p:*|--property:*) msbuild_args+=("$1") ;;
            *) usage; return 2 ;;
        esac
        shift
    done
    "$command" ${msbuild_args[@]+"${msbuild_args[@]}"}
}

restore_locks() {
    local project
    for project in "${PUBLISHED_PROJECTS[@]}"; do
        echo -e "${CYAN}== Refreshing NuGet lock for ${GREEN}${project}${CYAN} ==${RESET}"
        dotnet restore "$project" --force-evaluate
    done
}

restore_locked() {
    local project
    for project in "${PUBLISHED_PROJECTS[@]}"; do
        echo -e "${CYAN}== Validating NuGet lock for ${GREEN}${project}${CYAN} ==${RESET}"
        dotnet restore "$project" --locked-mode
    done
}

restore_dotnet_tools() {
    dotnet tool restore
}

format() {
    restore_dotnet_tools
    dotnet csharpier format .
}

format_check() {
    restore_dotnet_tools
    dotnet csharpier check .
}

check_ilspy() {
    if ! command -v ilspycmd &>/dev/null; then
        echo "ilspycmd not found. Installing..."
        dotnet tool install -g ilspycmd
    fi
}

# Steam beta branches ("public_test_realm" = PTR) replace the single install
# in place, so the Managed dir silently changes identity on branch switch.
# Guard so PTR bits never overwrite ./decompiled (online reference) and vice versa.
# The appmanifest is located by walking up from MANAGED — the directory the DLLs
# are actually read from — so an overridden BPP_MANAGED_PATH is guarded too.
locate_appmanifest() {
    local dir="$MANAGED"
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
    local acf
    acf=$(locate_appmanifest)
    [[ -n "$acf" ]] || { echo "unknown"; return; }
    local key
    key=$(awk '/"MountedConfig"/,/^\t\}/' "$acf" | awk -F '"' '/"BetaKey"/ {print $4}')
    echo "${key:-public}"
}

require_steam_branch() {
    local expected="$1"
    [[ "${BPP_SKIP_BRANCH_CHECK:-}" == "1" ]] && return 0
    local branch
    branch=$(installed_steam_branch)
    if [[ "$branch" == "unknown" ]]; then
        echo -e "${RED}Could not find appmanifest_1617400.acf above the Managed path to verify the Steam branch.${RESET}" >&2
        echo -e "${RED}Decompiling a bare copied Managed dir? Set BPP_SKIP_BRANCH_CHECK=1 to override.${RESET}" >&2
        exit 1
    fi
    if [[ "$branch" != "$expected" ]]; then
        echo -e "${RED}Installed Steam branch is '$branch', expected '$expected'.${RESET}" >&2
        echo -e "${RED}Switch The Bazaar's beta branch in Steam first, or set BPP_SKIP_BRANCH_CHECK=1 to override.${RESET}" >&2
        exit 1
    fi
}

decompile() {
    check_ilspy
    local dll="${2:-Assembly-CSharp}"
    local out_root="${BPP_DECOMPILE_OUT:-./decompiled}"
    local out="$out_root/$dll"
    echo "Decompiling $dll to $out..."
    DOTNET_ROLL_FORWARD=Major ilspycmd -p -o "$out" "$MANAGED/$dll.dll"
    echo "Done: $out"
}

decompile_all() {
    for dll in Assembly-CSharp BazaarGameClient BazaarGameShared BazaarBattleService TheBazaarRuntime FMODUnity; do
        decompile _ "$dll"
    done
}

# Archive the currently installed Managed dir keyed by branch + buildid. Because the
# two branches overwrite each other in place, this is the only way to keep both
# assembly sets available (Release pinning + build-matrix consume these snapshots).
snapshot_managed() {
    local acf branch buildid channel dest
    acf=$(locate_appmanifest)
    if [[ -z "$acf" ]]; then
        echo -e "${RED}Could not find appmanifest_1617400.acf above the Managed path.${RESET}" >&2
        exit 1
    fi
    branch=$(installed_steam_branch)
    channel="online"
    [[ "$branch" == "public_test_realm" ]] && channel="ptr"
    if [[ "$branch" != "public" && "$branch" != "public_test_realm" ]]; then
        echo -e "${RED}Installed branch '$branch' is neither public nor public_test_realm; refusing to snapshot.${RESET}" >&2
        exit 1
    fi
    buildid=$(awk -F '"' '/"buildid"/ {print $4; exit}' "$acf")
    dest="$SCRIPT_DIR/game-libs/$channel-$buildid/Managed"
    if [[ -d "$dest" ]]; then
        echo "Snapshot already exists: $dest"
        return
    fi
    mkdir -p "$dest"
    cp -R "$MANAGED/." "$dest/"
    echo -e "${GREEN}Archived $channel (buildid $buildid) Managed -> $dest${RESET}"
}

# Compile the single source tree against every archived Managed snapshot. Uses the
# CompatCheck configuration so neither the Debug plugins-copy nor the Release
# installer-copy post-build steps fire.
build_matrix() {
    local snaps=("$SCRIPT_DIR"/game-libs/*/Managed)
    local found=0 failed=()
    local snap
    for snap in "${snaps[@]}"; do
        [[ -d "$snap" ]] || continue
        found=1
        echo -e "${CYAN}== Matrix build against ${GREEN}${snap}${CYAN} ==${RESET}"
        if ! dotnet build src/BazaarPlusPlus/BazaarPlusPlus.csproj -c CompatCheck -p:ManagedPath="$snap"; then
            failed+=("$snap")
        fi
    done
    if ((found == 0)); then
        echo -e "${RED}No snapshots under game-libs/. Run './run.sh snapshot-managed' on each branch first.${RESET}" >&2
        exit 1
    fi
    if ((${#failed[@]} > 0)); then
        echo -e "${RED}Matrix build failed against:${RESET}" >&2
        printf '  %s\n' "${failed[@]}" >&2
        exit 1
    fi
    echo -e "${GREEN}Matrix build passed for all snapshots.${RESET}"
}

usage() {
    cat <<EOF
Usage:
  $0 build [--with-bazaaragent] [--fast] [-p:Name=Value ...]
      Debug build; copies into BepInEx/plugins/ when the game is found.
  $0 publish [--with-bazaaragent] [-p:Name=Value ...]
      Production build: fetch remote embedded data, run the feature-owned seed
      gates, then Debug + Release with installer packaging.
  $0 fetch-data [-p:Name=Value ...]
      Refresh the remote embedded seeds without building.
  $0 restore-locks
      Rewrite the committed NuGet lock files for the six published assemblies.
      Run after every Directory.Packages.props change; test projects get none.
  $0 restore-locked
      Validate those restores in locked mode without rewriting the lock files.
  $0 test
      Run the explicit default xUnit suite without deploying to the live game or fetching seeds.
  $0 test-compat [-p:ManagedPath=...]
      Run every compatibility check whose local game/decompiled prerequisites are available.
  $0 test-corpus <replay-corpus-path> [report-path]
      Run the opt-in offline Combat Impact corpus acceptance. A corpus is mandatory.
  $0 format
  $0 format-check
      Format, or fail on unformatted files, with the repo-pinned CSharpier.
  $0 decompile [DllName]
  $0 decompile-all
      Decompile game DLLs into decompiled/ (default DLL: Assembly-CSharp).
  $0 decompile-ptr [DllName]
  $0 decompile-all-ptr
      Same for the PTR client, into decompiled-vptr/.
  $0 snapshot-managed
      Archive the installed Managed dir under game-libs/, so a later game
      update can still be built against.
  $0 build-matrix
      Build the source tree against every archived Managed snapshot.

Options:
  --with-bazaaragent  Build and copy the optional BazaarAgent assemblies.
  --fast              With build: skip NuGet restore (rerun without it after csproj edits or in a fresh worktree).
  -p:Name=Value       Forward an MSBuild property to build, publish, or fetch-data.
EOF
}

case "${1:-}" in
    publish)
        shift
        parse_publish_options "$@"
        ;;
    fetch-data)
        shift
        parse_fetch_data_options "$@"
        ;;
    build)
        shift
        parse_build_options "$@"
        ;;
    restore-locks)  restore_locks ;;
    restore-locked) restore_locked ;;
    test)
        shift
        parse_test_options test_all "$@"
        ;;
    test-compat)
        shift
        parse_test_options test_compat "$@"
        ;;
    test-corpus)
        shift
        test_corpus "$@"
        ;;
    format)       format ;;
    format-check) format_check ;;
    decompile)
        require_steam_branch public
        decompile "$@"
        ;;
    decompile-all)
        require_steam_branch public
        decompile_all
        ;;
    decompile-ptr)
        require_steam_branch public_test_realm
        BPP_DECOMPILE_OUT=./decompiled-vptr decompile "$@"
        ;;
    decompile-all-ptr)
        require_steam_branch public_test_realm
        BPP_DECOMPILE_OUT=./decompiled-vptr decompile_all
        ;;
    snapshot-managed) snapshot_managed ;;
    build-matrix) build_matrix ;;
    *)
        usage
        exit 1
        ;;
esac
