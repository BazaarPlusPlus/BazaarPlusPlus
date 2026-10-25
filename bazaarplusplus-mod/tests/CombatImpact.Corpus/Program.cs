#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.TestSupport;

// The Core-lane Combat Impact acceptance runner (ADR-0019). It projects a Combat Impact Corpus,
// the BPP-owned fixture CombatImpact.CorpusExport writes from game payloads, through Core alone.
//
// No arguments: projects the committed corpus and compares its evidence with the committed
// evidence golden; BPP_UPDATE_GOLDENS=1 rewrites the golden.
// <corpus.json.gz> [report-path] [--benchmark]: projects an exported external corpus.

const string pinnedCorpus = "tests/CombatImpact.Corpus/corpus/pinned-corpus.json.gz";
const string pinnedEvidence = "tests/CombatImpact.Corpus/evidence/pinned-corpus.json";
const string evidencePathEnvironmentVariable = "BPP_COMBAT_IMPACT_EVIDENCE_PATH";

var benchmarkEnabled = args.Contains("--benchmark", StringComparer.Ordinal);
var positionalArguments = args.Where(argument =>
        !string.Equals(argument, "--benchmark", StringComparison.Ordinal)
    )
    .ToArray();
if (
    positionalArguments.Length > 2
    || args.Any(argument =>
        argument.StartsWith("--", StringComparison.Ordinal) && argument != "--benchmark"
    )
)
{
    throw new ArgumentException(
        "Usage: CombatImpact.Corpus [<corpus.json.gz> [report-path] [--benchmark]]"
    );
}

var pinned = positionalArguments.Length == 0;
var corpusPath = pinned
    ? Path.Combine(TestInputs.RepoRoot, pinnedCorpus)
    : Path.GetFullPath(positionalArguments[0]);
if (!File.Exists(corpusPath))
    throw new FileNotFoundException($"Combat Impact Corpus does not exist: {corpusPath}");

var reportPath =
    positionalArguments.Length == 2
        ? Path.GetFullPath(positionalArguments[1])
        : Path.Combine(TestInputs.RepoRoot, "artifacts", "combat-impact-corpus", "report.json");
var corpus = CombatImpactCorpusCodec.Read(corpusPath);
var replays = corpus.Battles;
var invalidPayloads = corpus.InvalidPayloads;
var projectionResult = CombatImpactCorpusProjection.Analyze(replays, benchmarkEnabled);
var cardAttributeAttribution = projectionResult.Attribution;

var sample = replays
    .Select(replay => new BoundarySample(
        replay.BattleId,
        replay.Timeline.Frames.Count,
        replay.Spawn.SpawnedCards.Count,
        CountPeriodicStats(replay.Timeline),
        CountPeriodicAdjustments(replay.Timeline)
    ))
    .FirstOrDefault(candidate =>
        candidate.SpawnedCards > 0
        && (candidate.PeriodicStats > 0 || candidate.PeriodicAdjustments > 0)
    );
if (sample == null)
{
    throw new InvalidOperationException(
        $"Corpus contained {replays.Count} replays but no non-vacuous periodic-effect sample. "
            + $"Invalid payloads: {string.Join(",", invalidPayloads)}"
    );
}

// Every newline is "\n", never Environment.NewLine: the report hash and the evidence bytes must
// be identical on macOS, Linux and Windows.
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    NewLine = "\n",
};
var report = CorpusInventory.Build(replays, invalidPayloads, cardAttributeAttribution);
var reportJson = JsonSerializer.Serialize(report, jsonOptions);
var repeatedJson = JsonSerializer.Serialize(
    CorpusInventory.Build(replays, invalidPayloads, cardAttributeAttribution),
    jsonOptions
);
if (!string.Equals(reportJson, repeatedJson, StringComparison.Ordinal))
    throw new InvalidOperationException("P1 inventory is not deterministic across repeated runs.");

Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, reportJson + "\n");
var configuredEvidencePath = Environment.GetEnvironmentVariable(evidencePathEnvironmentVariable);
var evidencePath =
    !string.IsNullOrWhiteSpace(configuredEvidencePath) ? Path.GetFullPath(configuredEvidencePath)
    : pinned ? Path.Combine(Path.GetDirectoryName(reportPath)!, "evidence.json")
    : Path.Combine(
        Path.GetDirectoryName(reportPath)!,
        Path.GetFileNameWithoutExtension(reportPath) + ".evidence.json"
    );
var evidenceJson =
    JsonSerializer.Serialize(BuildEvidenceSnapshot(report, corpus, reportJson), jsonOptions) + "\n";
Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
File.WriteAllText(evidencePath, evidenceJson);
if (projectionResult.Benchmark is { } benchmark)
{
    var benchmarkPath = Path.Combine(
        Path.GetDirectoryName(reportPath)!,
        Path.GetFileNameWithoutExtension(reportPath) + ".benchmark.json"
    );
    File.WriteAllText(benchmarkPath, JsonSerializer.Serialize(benchmark, jsonOptions) + "\n");
    Console.WriteLine(
        $"BENCHMARK projection_only path={benchmarkPath} battles={benchmark.Battles} "
            + $"frames={benchmark.Frames} events={benchmark.Events} "
            + $"executions={benchmark.Executions} evidence={benchmark.Evidence} "
            + $"critical_work_units={benchmark.CriticalWorkUnits} "
            + $"elapsed_total_ms={benchmark.ElapsedTotalMilliseconds:F3} "
            + $"elapsed_p50_ms={benchmark.ElapsedP50Milliseconds:F3} "
            + $"elapsed_p95_ms={benchmark.ElapsedP95Milliseconds:F3} "
            + $"elapsed_max_ms={benchmark.ElapsedMaxMilliseconds:F3} "
            + $"allocated_total_bytes={benchmark.AllocatedTotalBytes} "
            + $"allocated_p50_bytes={benchmark.AllocatedP50Bytes} "
            + $"allocated_p95_bytes={benchmark.AllocatedP95Bytes} "
            + $"allocated_max_bytes={benchmark.AllocatedMaxBytes}"
    );
}
CorpusAcceptance.Validate(report);

Console.WriteLine(
    $"P0 crossed battle={sample.BattleId} source={corpus.SourceKind} "
        + $"source_count={corpus.SourceCount} corpus={replays.Count} frames={sample.Frames} "
        + $"spawned={sample.SpawnedCards} periodic_stats={sample.PeriodicStats} "
        + $"periodic_adjustments={sample.PeriodicAdjustments} invalid={invalidPayloads.Count}"
);
Console.WriteLine($"P1 evidence snapshot={evidencePath}");
Console.WriteLine(
    $"P1 inventory report={reportPath} battles={report.Battles} frames={report.Frames} "
        + $"periodic_frames={report.PeriodicFrames.Count} adjustments={report.PeriodicAdjustmentCount} "
        + $"resolved_actions={report.Sources.ResolvedActions} "
        + $"unresolved_actions={report.Sources.UnresolvedActions} "
        + $"missing_stat_identities={report.Sources.MissingStatSourceIdentities.Count}"
);
Console.WriteLine(
    $"P2 card_attribute groups={report.CardAttributeAttribution.DiagnosticGroups} "
        + $"claimants={report.CardAttributeAttribution.DiagnosedClaimants} "
        + $"implicit_player_effects={report.CardAttributeAttribution.ImplicitPlayerEffects} "
        + $"concurrent_exact={report.CardAttributeAttribution.ConcurrentExactGroups} "
        + $"a2_solved={report.CardAttributeAttribution.SingleUnknownSolvedGroups} "
        + $"residual={report.CardAttributeAttribution.ResidualGroups} "
        + $"single_unknown={report.CardAttributeAttribution.SingleUnknownResidualGroups} "
        + $"a2_remaining={report.CardAttributeAttribution.RemainingSingleUnknownSolvableGroups} "
        + $"single_unknown_range={report.CardAttributeAttribution.SingleUnknownRangeGroups} "
        + $"b_candidate={report.CardAttributeAttribution.EventOrderReplayCandidateGroups} "
        + $"b_multiply={report.CardAttributeAttribution.EventOrderReplayMultiplyGroups} "
        + $"conservation_failures={report.CardAttributeAttribution.ConservationFailures}"
);
if (pinned)
    CorpusAcceptance.CompareWithGolden(pinnedEvidence, evidenceJson);

static object BuildEvidenceSnapshot(
    CorpusInventoryReport report,
    CombatImpactCorpusDocument corpus,
    string reportJson
)
{
    var reportBytes = Encoding.UTF8.GetBytes(reportJson + "\n");
    return new
    {
        EvidenceSchemaVersion = 2,
        CorpusSchemaVersion = report.SchemaVersion,
        report.Attribution.ModelVersion,
        corpus.SourceKind,
        corpus.SourceCount,
        corpus.SourceArtifacts,
        // The game inputs the exporter interpreted the payloads with, copied from the corpus:
        // this lane has no game catalog or game assemblies of its own to describe.
        corpus.VerificationInputs,
        BattleIds = report
            .BattlesSummary.Select(battle => battle.BattleId)
            .Order(StringComparer.Ordinal)
            .ToArray(),
        FullReportSha256 = Convert.ToHexString(SHA256.HashData(reportBytes)).ToLowerInvariant(),
        report.Battles,
        report.Frames,
        PeriodicFrames = report.PeriodicFrames.Count,
        report.PeriodicAdjustmentCount,
        report.Adjustments,
        report.Sources,
        Rules = new
        {
            report.Rules.BurnShieldBridge,
            report.Rules.BurnDecay,
            report.Rules.PoisonTick,
            report.Rules.RegenTick,
            report.Rules.HealthLedger,
            report.Rules.ShieldLedger,
            report.Rules.RealizedTotals,
        },
        report.TerminalImpact,
        Attribution = new
        {
            report.Attribution.Results,
            report.Attribution.ExactResults,
            report.Attribution.ProportionalResults,
            report.Attribution.ResultsByKindAndProof,
            report.Attribution.HealthByKind,
            report.Attribution.ShieldByKind,
            report.Attribution.HealthByKindAndProof,
            report.Attribution.ShieldByKindAndProof,
            report.Attribution.AllocationDecisions,
            report.Attribution.AllocationDecisionsByKindAndProof,
            report.Attribution.AllocatedHealthByKindAndProof,
            report.Attribution.AllocatedShieldByKindAndProof,
            report.Attribution.MeasuredHealthByCombatantAndKind,
            report.Attribution.MeasuredShieldByCombatantAndKind,
            report.Attribution.AllocatedHealthByCombatantAndKind,
            report.Attribution.AllocatedShieldByCombatantAndKind,
            report.Attribution.ResidualHealthByCombatantAndKind,
            report.Attribution.ResidualShieldByCombatantAndKind,
            report.Attribution.UnattributedHealthByKind,
            report.Attribution.UnattributedShieldByKind,
            report.Attribution.UnattributedHealthByOrigin,
            report.Attribution.UnattributedShieldByOrigin,
            report.Attribution.UnattributedHealthByContext,
            report.Attribution.UnattributedRegenByCandidateSurfaces,
            report.Attribution.RegenRealizedBySpawnBaseline,
            report.Attribution.RegenAttributedBySpawnBaseline,
            report.Attribution.RegenAttributedBySourceSurfaces,
            report.Attribution.RegenAttributedSpawnBaselineSources,
            report.Attribution.RegenUnknownBySpawnBaseline,
            report.Attribution.RegenGapMeasurementMismatchFrames,
            report.Attribution.RegenGapMeasurementSignedDifference,
            report.Attribution.RegenGapMeasurementAbsoluteDifference,
            report.Attribution.RegenStableSpawnBaselineRealized,
            report.Attribution.RegenStableSpawnBaselineAttributed,
            report.Attribution.RegenStableSpawnBaselineUnknown,
        },
        report.CardAttributeAttribution,
    };
}

static int CountPeriodicStats(CombatTimeline combat) =>
    combat.CardStats.Values.Count(stats =>
        stats.ContainsKey(CombatCardStat.BurnAdded)
        || stats.ContainsKey(CombatCardStat.PoisonAdded)
        || stats.ContainsKey(CombatCardStat.RegenAdded)
    );

static int CountPeriodicAdjustments(CombatTimeline combat) =>
    combat.Frames.Sum(frame =>
        CountUpdatePeriodicAdjustments(frame.PlayerUpdates)
        + CountUpdatePeriodicAdjustments(frame.OpponentUpdates)
    );

static int CountUpdatePeriodicAdjustments(CombatPlayerUpdate? update) =>
    update?.HealthAdjustments.Count(adjustment =>
        adjustment.DamageType
            is CombatDamageType.Burn
                or CombatDamageType.Poison
                or CombatDamageType.Regen
    ) ?? 0;

internal sealed record BoundarySample(
    string BattleId,
    int Frames,
    int SpawnedCards,
    int PeriodicStats,
    int PeriodicAdjustments
);
