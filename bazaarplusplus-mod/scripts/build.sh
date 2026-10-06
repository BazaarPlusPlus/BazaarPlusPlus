#!/usr/bin/env bash
# Compile, deploy, seed refresh, and Payload production. Entry points: `just mod::*`
# and release/payload.mjs (`produce`).
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

MAIN_PROJECT=src/BazaarPlusPlus/BazaarPlusPlus.csproj

# build [--deploy] [--fast] [-p:Name=Value ...]: compile; only --deploy touches the game install.
cmd_build() {
    local deploy=false
    local fast_args=() arg
    local props=()
    for arg in "$@"; do
        case "$arg" in
            # Inner-loop accelerator: skips NuGet restore. Run a normal build after
            # editing any csproj or creating a fresh worktree.
            --fast) fast_args=(--no-restore) ;;
            --deploy) deploy=true ;;
            -p:* | --property:*) props+=("$arg") ;;
            *) die "Expected --deploy, --fast, or -p:Name=Value, got '$arg'" ;;
        esac
    done
    while IFS= read -r arg; do props+=("$arg"); done < <(managed_props ${props[@]+"${props[@]}"})

    local deploy_props=(-p:BppDeployToGame=false)
    if [[ "$deploy" == "true" ]]; then
        local root
        root="$(game_root)"
        [[ -n "$root" ]] || die "The Bazaar install was not found. Set BPP_GAME_ROOT=/path/to/The Bazaar."
        # Deploy is gated on GamePath, which ManagedPath.props only sets for a
        # discovered install, so pass it even when ManagedPath points elsewhere.
        deploy_props=(-p:BppDeployToGame=true "-p:GamePath=$root")
        if [[ "$(host_platform)" == "macos" ]]; then
            repair_macos_trampoline "$root" ${props[@]+"${props[@]}"}
        fi
    fi

    dotnet build "$MAIN_PROJECT" \
        ${fast_args[@]+"${fast_args[@]}"} ${props[@]+"${props[@]}"} "${deploy_props[@]}"
}

# A game update restores Steam's executable, so every deploy re-installs the trampoline first.
repair_macos_trampoline() {
    local root="$1"
    shift
    local installer_source
    installer_source="$(prop_value BPPInstallerSourcePath "$@")"
    installer_source="${installer_source:-${BPP_INSTALLER_SOURCE_PATH:-$MOD_ROOT/../bazaarplusplus-installer/src-tauri/resources}}"
    mkdir -p "$MOD_ROOT/artifacts"
    node "$MOD_ROOT/../bazaarplusplus-installer/scripts/headless.mjs" repair \
        --game "$root" \
        --stub "${BPP_TRAMPOLINE_STUB:-$installer_source/Trampoline/macos/bpp_launcher}"
}

# matrix [channel ...]: compile against every captured lock entry, fetching each
# snapshot the way a normal build would; with channels, against exactly those entries
# of the host platform, so an empty one fails instead of being skipped (the CI lane
# names staging and ptr). The CompatCheck configuration fires neither the Debug
# deploy nor the Release installer copy.
cmd_matrix() {
    local entries=() entry platform channel managed failed=()
    if (($# > 0)); then
        for channel in "$@"; do
            case "$channel" in
                online | staging | ptr) entries+=("$(host_platform) $channel") ;;
                *) die "Expected a channel (online, staging or ptr), got '$channel'" ;;
            esac
        done
    else
        while IFS= read -r entry; do entries+=("$entry"); done < <(game_libs_cli entries)
        ((${#entries[@]} > 0)) || die "The lock has no captured entries. Run 'just mod::snapshot' on a machine with the game, publish it, and commit build/game-libs.lock.json."
    fi
    for entry in "${entries[@]}"; do
        platform="${entry% *}" channel="${entry#* }"
        step "Matrix build against ${GREEN}${platform} ${channel}${CYAN}"
        if ! managed="$(lock_managed_path "$platform" "$channel")"; then
            failed+=("$platform $channel (snapshot unavailable)")
            continue
        fi
        dotnet build "$MAIN_PROJECT" -c CompatCheck "-p:ManagedPath=$managed" ||
            failed+=("$platform $channel ($managed)")
    done
    if ((${#failed[@]} > 0)); then
        err "Matrix build failed against:"
        printf '  %s\n' "${failed[@]}" >&2
        exit 1
    fi
    ok "Matrix build passed for every captured lock entry."
}

# fetch-data [-p:Name=Value ...]: refresh the remote embedded seeds without building.
cmd_fetch_data() {
    collect_props "$@"
    fetch_remote_data ${PROPS[@]+"${PROPS[@]}"}
}

# Fetches into a staging directory, gates it, then promotes it over the canonical set.
# The EXIT trap removes the staging directory when any step fails.
fetch_remote_data() {
    local canonical_directory
    canonical_directory="$(prop_value RemoteEmbeddedDataDirectory "$@")"
    canonical_directory="${canonical_directory:-$MOD_ROOT/src/BazaarPlusPlus/obj/remote-data}"
    local canonical_parent
    canonical_parent="$(dirname "$canonical_directory")"
    mkdir -p "$canonical_parent"
    STAGING_DIRECTORY="$(mktemp -d "$canonical_parent/.remote-data-stage.XXXXXX")"
    trap 'rm -rf -- "$STAGING_DIRECTORY"' EXIT

    dotnet msbuild "$MAIN_PROJECT" \
        -t:FetchRemoteEmbeddedData \
        "$@" \
        "-p:RemoteEmbeddedDataDirectory=$STAGING_DIRECTORY" \
        -p:ForceRemoteEmbeddedDataRefresh=true
    run_seed_gates \
        "$@" \
        "-p:RemoteEmbeddedDataDirectory=$STAGING_DIRECTORY" \
        -p:RemoteEmbeddedDataPrepared=true
    dotnet run \
        --project build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetcher.csproj \
        --no-launch-profile -- \
        promote "$STAGING_DIRECTORY" "$canonical_directory" \
        voice-lines.json builds.json
    rm -rf -- "$STAGING_DIRECTORY"
}

run_seed_gates() {
    step "Validating ${GREEN}voice subtitle embedded seed${CYAN}"
    dotnet test tests/RuntimeIntegration.Tests/RuntimeIntegration.Tests.csproj \
        -c Release \
        --filter "TestKind=EmbeddedSeed" \
        "$@"
    step "Validating ${GREEN}live build recommendation embedded seed${CYAN}"
    dotnet test tests/ScenarioRunner.Tests/ScenarioRunner.Tests.csproj \
        -c Release \
        --filter "TestKind=EmbeddedSeed" \
        -p:BppSeedGateOnly=true \
        "$@"
}

# produce -p:ManagedPath=... -p:BPPInstallerSourcePath=... ...: internal to
# release/payload.mjs, which resolves and pins ManagedPath (game.sh managed-path).
cmd_produce() {
    : "${BPP_RELEASE_ARTIFACTS:?Run node release.mjs prepare to build an isolated Payload}"
    collect_props "$@"
    local installer_source
    installer_source="$(prop_value BPPInstallerSourcePath "$@")"
    [[ -d "$installer_source" ]] ||
        die "Installer resources not found at '$installer_source'. Pass -p:BPPInstallerSourcePath=/absolute/path/to/resources."
    [[ -n "$(prop_value ManagedPath "$@")" ]] || die "produce requires -p:ManagedPath."

    local common_args=("${PROPS[@]}" "-p:BppReleasePlatform=$(host_platform)")
    fetch_remote_data "${common_args[@]}"
    dotnet build "$MAIN_PROJECT" \
        -t:BuildAll \
        "${common_args[@]}" \
        -p:UseArtifactsOutput=true \
        "-p:ArtifactsPath=$BPP_RELEASE_ARTIFACTS" \
        -p:BuildProductionPackage=true \
        -p:RemoteEmbeddedDataPrepared=true
}

dispatch "$@"
