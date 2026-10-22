#!/usr/bin/env bash
# Offline test suites. Entry points: `just mod::test`, `mod::test-corpus`.
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

# test-core [-p:Name=Value ...]: the suites that need no Managed/ snapshot, so they also run on
# Linux: the Android host's boundary test, which needs the Interop Snapshot
# (`just mod::fetch android online`) instead, and the two goldens on the net10.0 binding the
# Android host ships: the Run segment (BundleV6Codec) and Run Logging's history database.
cmd_test_core() {
    collect_props "$@"
    dotnet test tests/BazaarPlusPlus.CoreTests.slnx "${TEST_PROPS[@]}" ${PROPS[@]+"${PROPS[@]}"}
    local project
    for project in BundleV6Codec.Net10.Tests RunLoggingPipeline.Net10.Tests; do
        dotnet run --project "tests/$project/$project.csproj" \
            "${TEST_PROPS[@]}" ${PROPS[@]+"${PROPS[@]}"}
    done
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
