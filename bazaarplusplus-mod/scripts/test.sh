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

# Keep each invocation's TRX and crash evidence, including when dotnet exits unsuccessfully.
# xUnit has no timeout of its own, so a hung test would hold the job until CI kills it with no
# evidence. The hang guard dumps and stops the test host when no test starts or ends for 5
# minutes: above ScenarioRunner.Tests' own 2-minute limit per capsule, which kills the capsule
# and names it first, and below every CI job limit. The dump type is the crash guard's, which
# the macOS, Windows and Linux lanes already use.
test_diagnostics() {
    mkdir -p "$MOD_ROOT/artifacts/tests"
    local results
    results="$(mktemp -d "$MOD_ROOT/artifacts/tests/$1.XXXXXX")"
    TEST_DIAGNOSTICS=(--logger trx --results-directory "$results" --blame-crash --blame-crash-dump-type mini
        --blame-hang-timeout 5min --blame-hang-dump-type mini)
    step "Test evidence: $results"
}

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
    test_diagnostics desktop
    dotnet test tests/BazaarPlusPlus.Tests.slnx --filter 'TestKind!=Performance' \
        "${TEST_DIAGNOSTICS[@]}" "${TEST_PROPS[@]}" ${PROPS[@]+"${PROPS[@]}"}
}

# Run wall-clock assertions separately from parallel functional suites, through the capsule host.
cmd_test_performance() {
    resolve_test_inputs "$@"
    test_diagnostics performance
    dotnet test tests/ScenarioRunner.Tests/ScenarioRunner.Tests.csproj --filter 'TestKind=Performance' \
        "${TEST_DIAGNOSTICS[@]}" "${TEST_PROPS[@]}" ${PROPS[@]+"${PROPS[@]}"}
}

# test-core [-p:Name=Value ...]: the suites that need no Managed/ snapshot, so they also run on
# Linux: the xUnit suites in BazaarPlusPlus.CoreTests.slnx (among them the Android host's
# boundary test, which needs the Interop Snapshot from `just mod::fetch android publisher`
# instead), then the console capsules BppTestCoreProjects names (Directory.Build.props), which
# run against Core's net10.0 output: among them the two goldens on the binding the Android host
# ships, the Run segment (BundleV6Codec) and Run Logging's history database, and the Combat
# Impact Corpus runner. Architecture.Tests' TestProjectRegistryTests holds every capsule there
# to BppScenarioRunnerProjects, which `mod::test` runs.
# BppSnapshotPlatform=none names no lock entry, so a project that reaches a Mono target fails
# here on every host the way it fails on the Linux lane, not only where no snapshot is fetched.
cmd_test_core() {
    collect_props "$@"
    local core_props=(-p:BppSnapshotPlatform=none "${TEST_PROPS[@]}")
    # Strip all whitespace: a list wrapped across lines would otherwise stop `read` at the first
    # newline and silently run fewer capsules.
    local list projects=()
    list="$(dotnet msbuild Directory.Build.props -nologo -getProperty:BppTestCoreProjects \
        "${core_props[@]}" ${PROPS[@]+"${PROPS[@]}"} | tr -d '[:space:]')"
    [[ -n "$list" ]] || die "BppTestCoreProjects in Directory.Build.props is empty."
    IFS=';' read -r -a projects <<<"$list"
    test_diagnostics core
    dotnet test tests/BazaarPlusPlus.CoreTests.slnx "${TEST_DIAGNOSTICS[@]}" \
        "${core_props[@]}" ${PROPS[@]+"${PROPS[@]}"}
    local project
    for project in "${projects[@]}"; do
        [[ -n "$project" ]] || continue
        dotnet run --project "tests/$project/$project.csproj" \
            "${core_props[@]}" ${PROPS[@]+"${PROPS[@]}"}
    done
}

# test-corpus <replay-corpus-path> [report-path] [--benchmark]: opt-in Combat Impact acceptance.
# The host-lane exporter turns the game payloads into a Combat Impact Corpus beside the report
# (<report>.corpus.json.gz), then the Core-lane runner projects it.
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
    local exported="${report_path%.json}.corpus.json.gz" props=()
    while IFS= read -r arg; do props+=("$arg"); done < <(managed_props)
    dotnet run --project tests/CombatImpact.CorpusExport/CombatImpact.CorpusExport.csproj \
        -p:BppDeployToGame=false ${props[@]+"${props[@]}"} \
        -- export "$corpus_path" "$exported"
    dotnet run --project tests/CombatImpact.Corpus/CombatImpact.Corpus.csproj \
        -p:BppDeployToGame=false -p:BppSnapshotPlatform=none \
        -- "$exported" "$report_path" ${benchmark_arg[@]+"${benchmark_arg[@]}"}
}

dispatch "$@"
