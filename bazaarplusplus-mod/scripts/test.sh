#!/usr/bin/env bash
# Offline test suites. Entry points: `just mod::test`, `mod::test-compat`, `mod::test-corpus`.
# shellcheck source=scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

# Tests never deploy or fetch; they embed the committed seed fixtures.
TEST_PROPS=(
    -p:BppDeployToGame=false
    -p:ForceRemoteEmbeddedDataRefresh=false
    "-p:RemoteEmbeddedDataDirectory=$MOD_ROOT/tests/TestData/remote-data"
)

# Collects -p arguments into PROPS plus BPP_MANAGED_PATH, and sets MANAGED to the
# game assembly directory the build will resolve.
resolve_test_inputs() {
    collect_props "$@"
    local arg
    while IFS= read -r arg; do PROPS+=("$arg"); done < <(managed_props "$@")
    MANAGED="$(to_unix_path "$(managed_path "$@")")"
}

# test [-p:Name=Value ...]: the default xUnit suite.
cmd_test() {
    resolve_test_inputs "$@"
    [[ -f "$MANAGED/Assembly-CSharp.dll" ]] ||
        die "Game assemblies not found at '${MANAGED:-<not discovered>}'. Pass -p:ManagedPath=/absolute/path/to/Managed."
    step "Test game assemblies: ${GREEN}${MANAGED}${CYAN}"
    dotnet test tests/BazaarPlusPlus.Tests.slnx "${TEST_PROPS[@]}" ${PROPS[@]+"${PROPS[@]}"}
}

# test-compat [-p:Name=Value ...]: mandatory Release/game JSON check, plus available source checks.
cmd_test_compat() {
    resolve_test_inputs "$@"
    [[ -f "$MANAGED/Assembly-CSharp.dll" && -f "$MANAGED/Newtonsoft.Json.dll" ]] ||
        die "Release member compatibility requires Assembly-CSharp.dll and Newtonsoft.Json.dll at '${MANAGED:-<not discovered>}'. Pass -p:ManagedPath=/absolute/path/to/Managed."

    local main_project=src/BazaarPlusPlus/BazaarPlusPlus.csproj
    local release_props=(
        ${PROPS[@]+"${PROPS[@]}"} "${TEST_PROPS[@]}"
        -p:Configuration=Release -p:BuildProductionPackage=false
    )
    step "Release member compatibility against ${MANAGED}"
    dotnet build "$main_project" "${release_props[@]}"
    local target_dir
    target_dir="$(dotnet msbuild "$main_project" -nologo "${release_props[@]}" -getProperty:TargetDir)"
    target_dir="$(to_unix_path "${target_dir//$'\r'/}")"
    dotnet run --project build/GameAssemblyCompatibility/GameAssemblyCompatibility.csproj \
        --configuration Release -- \
        --payload "$MANAGED/Newtonsoft.Json.dll" "$MOD_ROOT/../release/payload.json" "$target_dir"

    local decompiled_root="${BPP_DECOMPILED_SOURCE_ROOT:-$MOD_ROOT}"
    local runnable=1 checked=1 failures=()
    local requirement label project ready

    step "Compatibility preflight"
    while IFS='|' read -r requirement label project; do
        [[ -n "$requirement" && "$requirement" != \#* ]] || continue
        ready=false
        case "$requirement" in
            managed)
                [[ -f "$MANAGED/Assembly-CSharp.dll" ]] && ready=true
                ;;
            decompiled-online)
                [[ -f "$MOD_ROOT/decompiled/TheBazaarRuntime/AssetLoader.cs" ]] && ready=true
                ;;
            decompiled-any)
                [[ -d "$decompiled_root/decompiled/TheBazaarRuntime" ||
                    -d "$decompiled_root/decompiled-vptr/TheBazaarRuntime" ]] && ready=true
                ;;
            *)
                die "Unknown compatibility requirement '$requirement'."
                ;;
        esac

        if [[ "$ready" != "true" ]]; then
            printf '  SKIPPED %-34s requirement=%s\n' "$label" "$requirement"
            continue
        fi

        printf '  CHECKED %-34s project=%s\n' "$label" "$project"
        ((runnable += 1))
        if dotnet run --project "$project" "${TEST_PROPS[@]}" ${PROPS[@]+"${PROPS[@]}"}; then
            ((checked += 1))
        else
            failures+=("$label")
        fi
    done <tests/CompatibilityTests.manifest

    if ((${#failures[@]} > 0)); then
        printf '  FAILED %s\n' "${failures[@]}" >&2
        exit 1
    fi
    ok "Compatibility checks passed: ${checked}/${runnable}."
}

# test-corpus <replay-corpus-path> [report-path] [--benchmark]: opt-in Combat Impact acceptance.
cmd_test_corpus() {
    local usage="Usage: just mod::test-corpus <replay-corpus-path> [report-path] [--benchmark]"
    local corpus_path="" report_path="" benchmark_arg=() arg
    for arg in "$@"; do
        case "$arg" in
            --benchmark) benchmark_arg=(--benchmark) ;;
            *)
                if [[ -z "$corpus_path" ]]; then
                    corpus_path="$arg"
                elif [[ -z "$report_path" ]]; then
                    report_path="$arg"
                else
                    err "$usage"
                    exit 2
                fi
                ;;
        esac
    done
    if [[ -z "$corpus_path" ]]; then
        err "$usage"
        exit 2
    fi
    if [[ ! -d "$corpus_path" ]]; then
        err "Replay corpus directory not found: '$corpus_path'."
        exit 2
    fi
    report_path="${report_path:-$MOD_ROOT/artifacts/combat-impact-corpus/report.json}"
    local props=()
    while IFS= read -r arg; do props+=("$arg"); done < <(managed_props)
    dotnet run --project tests/CombatImpact.Corpus/CombatImpact.Corpus.csproj \
        -p:BppDeployToGame=false ${props[@]+"${props[@]}"} \
        -- "$corpus_path" "$report_path" ${benchmark_arg[@]+"${benchmark_arg[@]}"}
}

dispatch "$@"
