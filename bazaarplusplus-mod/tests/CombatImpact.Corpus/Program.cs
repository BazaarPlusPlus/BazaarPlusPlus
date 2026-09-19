#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Bundle;
using MessagePack;

const string bundleSampleLimitEnvironmentVariable = "BPP_BUNDLE_CORPUS_LIMIT";
const string evidencePathEnvironmentVariable = "BPP_COMBAT_IMPACT_EVIDENCE_PATH";
const string gameDataPathEnvironmentVariable = "BPP_GAMEDATA_DB";
const int defaultBundleSampleLimit = 100;

var benchmarkEnabled = args.Contains("--benchmark", StringComparer.Ordinal);
var positionalArguments = args.Where(argument =>
        !string.Equals(argument, "--benchmark", StringComparison.Ordinal)
    )
    .ToArray();
if (
    positionalArguments.Length is < 1 or > 2
    || args.Any(argument =>
        argument.StartsWith("--", StringComparison.Ordinal) && argument != "--benchmark"
    )
)
{
    throw new ArgumentException(
        "Usage: CombatImpact.Corpus <replay-corpus-path> [report-path] [--benchmark]"
    );
}

var corpusPath = Path.GetFullPath(positionalArguments[0]);

if (!Directory.Exists(corpusPath))
    throw new DirectoryNotFoundException($"Replay corpus does not exist: {corpusPath}");

var reportPath =
    positionalArguments.Length == 2
        ? Path.GetFullPath(positionalArguments[1])
        : Path.GetFullPath(Path.Combine("artifacts", "combat-impact-corpus", "report.json"));
var loadResult = LoadCorpus(corpusPath);
var replays = loadResult.Replays;
var invalidPayloads = loadResult.InvalidPayloads;
var gameDataPath = ResolveGameDataPath();
var projectionResult = CombatImpactCorpusProjection.Analyze(
    replays,
    CombatImpactCorpusCatalog.Load(gameDataPath),
    benchmarkEnabled
);
var cardAttributeAttribution = projectionResult.Attribution;

var sample = replays
    .Select(replay => new BoundarySample(
        replay.BattleId,
        replay.Combat.Frames.Count,
        replay.Spawn.Events.OfType<GameSimEventCardSpawned>().Count(),
        CountPeriodicStats(replay.Combat),
        CountPeriodicAdjustments(replay.Combat)
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

var forbiddenAssemblies = AppDomain
    .CurrentDomain.GetAssemblies()
    .Select(assembly => assembly.GetName().Name ?? string.Empty)
    .Where(name =>
        name.Equals("Assembly-CSharp", StringComparison.Ordinal)
        || name.StartsWith("UnityEngine", StringComparison.Ordinal)
    )
    .Order(StringComparer.Ordinal)
    .ToArray();
if (forbiddenAssemblies.Length > 0)
{
    throw new InvalidOperationException(
        $"Pure replay host loaded forbidden assemblies: {string.Join(",", forbiddenAssemblies)}"
    );
}

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
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
File.WriteAllText(reportPath, reportJson + Environment.NewLine);
var configuredEvidencePath = Environment.GetEnvironmentVariable(evidencePathEnvironmentVariable);
var evidencePath = string.IsNullOrWhiteSpace(configuredEvidencePath)
    ? Path.Combine(
        Path.GetDirectoryName(reportPath)!,
        Path.GetFileNameWithoutExtension(reportPath) + ".evidence.json"
    )
    : Path.GetFullPath(configuredEvidencePath);
var evidence = BuildEvidenceSnapshot(
    report,
    loadResult,
    reportJson,
    [
        DescribeVerificationInput("game_data", gameDataPath),
        DescribeVerificationInput(
            "game_types",
            typeof(CombatSim).Assembly.Location,
            typeof(CombatSim).Assembly.GetName().Version?.ToString()
        ),
        DescribeVerificationInput(
            "json_runtime",
            typeof(Newtonsoft.Json.JsonConvert).Assembly.Location,
            typeof(Newtonsoft.Json.JsonConvert).Assembly.GetName().Version?.ToString()
        ),
    ]
);
Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
File.WriteAllText(
    evidencePath,
    JsonSerializer.Serialize(evidence, jsonOptions) + Environment.NewLine
);
if (projectionResult.Benchmark is { } benchmark)
{
    var benchmarkPath = Path.Combine(
        Path.GetDirectoryName(reportPath)!,
        Path.GetFileNameWithoutExtension(reportPath) + ".benchmark.json"
    );
    File.WriteAllText(
        benchmarkPath,
        JsonSerializer.Serialize(benchmark, jsonOptions) + Environment.NewLine
    );
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
Validate(report);

Console.WriteLine(
    $"P0 crossed battle={sample.BattleId} source={loadResult.SourceKind} "
        + $"source_count={loadResult.SourceCount} corpus={replays.Count} frames={sample.Frames} "
        + $"spawned={sample.SpawnedCards} periodic_stats={sample.PeriodicStats} "
        + $"periodic_adjustments={sample.PeriodicAdjustments} invalid={invalidPayloads.Count}"
);
Console.WriteLine($"P1 evidence snapshot={evidencePath}");

static object BuildEvidenceSnapshot(
    CorpusInventoryReport report,
    CorpusLoadResult loadResult,
    string reportJson,
    IReadOnlyList<VerificationInputArtifact> verificationInputs
)
{
    var reportBytes = Encoding.UTF8.GetBytes(reportJson + Environment.NewLine);
    return new
    {
        EvidenceSchemaVersion = 2,
        CorpusSchemaVersion = report.SchemaVersion,
        report.Attribution.ModelVersion,
        loadResult.SourceKind,
        loadResult.SourceCount,
        SourceArtifacts = loadResult.SourceArtifacts,
        VerificationInputs = verificationInputs,
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

static VerificationInputArtifact DescribeVerificationInput(
    string role,
    string path,
    string? version = null
)
{
    using var stream = File.OpenRead(path);
    return new VerificationInputArtifact(
        role,
        Path.GetFileName(path),
        Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
        version
    );
}
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
        + $"conservation_failures={report.CardAttributeAttribution.ConservationFailures} "
        + $"game_data={gameDataPath}"
);

static int CountPeriodicStats(CombatSim combat) =>
    combat.CardStats.Values.Count(stats =>
        stats.ContainsKey(ECardStats.BurnAdded)
        || stats.ContainsKey(ECardStats.PoisonAdded)
        || stats.ContainsKey(ECardStats.RegenAdded)
    );

static string ResolveGameDataPath()
{
    var configured = Environment.GetEnvironmentVariable(gameDataPathEnvironmentVariable);
    if (!string.IsNullOrWhiteSpace(configured))
        return Path.GetFullPath(configured);

    var candidates = new[]
    {
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            "com.TempoStorm.TheBazaar",
            "prod",
            "cache",
            "GameData.db"
        ),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TempoStorm",
            "TheBazaar",
            "prod",
            "cache",
            "GameData.db"
        ),
    };
    var resolved = candidates.FirstOrDefault(File.Exists);
    return resolved
        ?? throw new FileNotFoundException(
            $"GameData.db was not found; set {gameDataPathEnvironmentVariable}."
        );
}

static int CountPeriodicAdjustments(CombatSim combat) =>
    combat.Frames.Sum(frame =>
        CountUpdatePeriodicAdjustments(frame.PlayerUpdates)
        + CountUpdatePeriodicAdjustments(frame.OpponentUpdates)
    );

static int CountUpdatePeriodicAdjustments(CombatSimPlayerUpdate? update) =>
    update?.HealthAdjustments.Count(adjustment =>
        adjustment.DamageType is EDamageType.Burn or EDamageType.Poison or EDamageType.Regen
    ) ?? 0;

static CorpusLoadResult LoadCorpus(string corpusPath)
{
    var runBundleRoot = Path.Combine(corpusPath, "run-bundles");
    if (Directory.Exists(runBundleRoot))
    {
        var runBundlePaths = SampleBundlePaths(runBundleRoot, "*.mpack.gz");
        if (runBundlePaths.Length == 0)
            throw new InvalidOperationException($"Run-bundle cache is empty: {runBundleRoot}");
        return LoadBundleCorpus("run_bundles", runBundlePaths);
    }

    var rawBundlePaths = SampleBundlePaths(corpusPath, "*.bundle");
    return rawBundlePaths.Length > 0
        ? LoadBundleCorpus("raw_v5_bundles", rawBundlePaths)
        : LoadReplayPayloadCorpus(corpusPath);
}

static string[] SampleBundlePaths(string rootPath, string searchPattern) =>
    Directory
        .EnumerateFiles(rootPath, searchPattern, SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Take(BundleSampleLimit())
        .ToArray();

static int BundleSampleLimit()
{
    var configured = Environment.GetEnvironmentVariable(bundleSampleLimitEnvironmentVariable);
    return int.TryParse(configured, out var limit) && limit > 0 ? limit : defaultBundleSampleLimit;
}

static CorpusLoadResult LoadReplayPayloadCorpus(string corpusPath)
{
    var store = new CombatReplayPayloadStore(corpusPath);
    var battleIds = store.ListBattleIds().Order(StringComparer.Ordinal).ToArray();
    if (battleIds.Length == 0)
        throw new InvalidOperationException($"Replay corpus is empty: {corpusPath}");

    var replays = new List<ReplayObservationInput>();
    var invalidPayloads = new List<string>();
    foreach (var battleId in battleIds)
    {
        var loaded = store.LoadDetailed(battleId);
        if (loaded.Status != FileBackedPayloadLoadStatus.Loaded || loaded.Payload == null)
        {
            invalidPayloads.Add($"{battleId}:{loaded.Status}");
            continue;
        }
        AddReplay(
            replays,
            invalidPayloads,
            battleId,
            loaded.Payload.SpawnMessageBytes,
            loaded.Payload.CombatMessageBytes
        );
    }

    return new CorpusLoadResult(
        "replay_payloads",
        battleIds.Length,
        replays,
        invalidPayloads,
        battleIds.Select(battleId => new CorpusSourceArtifact(battleId, null)).ToArray()
    );
}

static CorpusLoadResult LoadBundleCorpus(string sourceKind, IReadOnlyList<string> bundlePaths)
{
    var replays = new List<ReplayObservationInput>();
    var invalidPayloads = new List<string>();
    var sourceArtifacts = new List<CorpusSourceArtifact>();
    var seenBattleIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var bundlePath in bundlePaths)
    {
        var bundleName = Path.GetFileName(bundlePath);
        byte[] bundleBytes;
        try
        {
            bundleBytes = File.ReadAllBytes(bundlePath);
            sourceArtifacts.Add(
                new CorpusSourceArtifact(
                    Path.GetFileName(bundlePath),
                    Convert.ToHexString(SHA256.HashData(bundleBytes)).ToLowerInvariant()
                )
            );
        }
        catch (Exception exception)
        {
            invalidPayloads.Add($"{bundleName}:{exception.GetType().Name}");
            continue;
        }

        var opened = RunBundleV5Contract.Open(bundleBytes);
        if (opened.Succeeded && opened.Value != null)
        {
            foreach (var battle in opened.Value.Payload.Battles)
                AddBundleReplay(
                    replays,
                    invalidPayloads,
                    seenBattleIds,
                    battle.BattleId,
                    battle.Replay
                );
            continue;
        }

        if (
            !MessagePackGzipCodec.TryDeserialize<ContractlessRunArtifact>(
                bundleBytes,
                out var contractless,
                out var contractlessError
            )
            || contractless == null
        )
        {
            invalidPayloads.Add(
                $"{bundleName}:{opened.FailureKind?.ToString() ?? "OpenFailed"}/"
                    + $"{contractlessError ?? "ContractlessOpenFailed"}"
            );
            continue;
        }
        foreach (var battle in contractless.Battles)
        {
            AddContractlessBundleReplay(
                replays,
                invalidPayloads,
                seenBattleIds,
                battle.BattleId,
                battle.ReplayPayload
            );
        }
    }

    if (replays.Count == 0)
        throw new InvalidOperationException("Sampled run bundles contained no replay payloads.");
    return new CorpusLoadResult(
        sourceKind,
        bundlePaths.Count,
        replays,
        invalidPayloads,
        sourceArtifacts.OrderBy(artifact => artifact.Name, StringComparer.Ordinal).ToArray()
    );
}

static void AddBundleReplay(
    ICollection<ReplayObservationInput> replays,
    ICollection<string> invalidPayloads,
    ISet<string> seenBattleIds,
    string battleId,
    BattleReplayV5? replay
)
{
    if (
        string.IsNullOrWhiteSpace(battleId)
        || replay == null
        || replay.SpawnMessageBytes.Length == 0
        || replay.CombatMessageBytes.Length == 0
        || !seenBattleIds.Add(battleId)
    )
    {
        return;
    }
    AddReplay(
        replays,
        invalidPayloads,
        battleId,
        replay.SpawnMessageBytes,
        replay.CombatMessageBytes
    );
}

static void AddContractlessBundleReplay(
    ICollection<ReplayObservationInput> replays,
    ICollection<string> invalidPayloads,
    ISet<string> seenBattleIds,
    string battleId,
    ContractlessReplayPayload? replay
)
{
    if (
        string.IsNullOrWhiteSpace(battleId)
        || replay == null
        || replay.SpawnMessageBytes.Length == 0
        || replay.CombatMessageBytes.Length == 0
        || !seenBattleIds.Add(battleId)
    )
    {
        return;
    }
    AddReplay(
        replays,
        invalidPayloads,
        battleId,
        replay.SpawnMessageBytes,
        replay.CombatMessageBytes
    );
}

static void AddReplay(
    ICollection<ReplayObservationInput> replays,
    ICollection<string> invalidPayloads,
    string battleId,
    ReadOnlyMemory<byte> spawnBytes,
    ReadOnlyMemory<byte> combatBytes
)
{
    try
    {
        var spawnMessage = MessagePackSerializer.Deserialize<NetMessageGameSim>(
            spawnBytes,
            MessagePackConfig.Options
        );
        var combatMessage = MessagePackSerializer.Deserialize<NetMessageCombatSim>(
            combatBytes,
            MessagePackConfig.Options
        );
        replays.Add(new ReplayObservationInput(battleId, spawnMessage.Data, combatMessage.Data));
    }
    catch (Exception exception)
    {
        invalidPayloads.Add($"{battleId}:{exception.GetType().Name}");
    }
}

static void Validate(CorpusInventoryReport report)
{
    if (report.InvalidPayloads.Count > 0)
        throw new InvalidOperationException(
            $"Corpus contained invalid payloads: {string.Join(",", report.InvalidPayloads)}"
        );
    var cardAttributes = report.CardAttributeAttribution;
    if (cardAttributes.DiagnosticGroups == 0 || cardAttributes.DiagnosedClaimants == 0)
    {
        throw new InvalidOperationException("Card-attribute attribution diagnostics were vacuous.");
    }
    if (
        cardAttributes.ConcurrentExactGroups
            + cardAttributes.SingleUnknownSolvedGroups
            + cardAttributes.ResidualGroups
        == 0
    )
    {
        throw new InvalidOperationException(
            "Card-attribute attribution diagnostics observed no concurrent claimant group."
        );
    }
    if (cardAttributes.ConservationFailures != 0)
    {
        throw new InvalidOperationException(
            $"Card-attribute attribution failed conservation in {cardAttributes.ConservationFailures} groups."
        );
    }
    if (
        cardAttributes.ResidualObservations.Any(observation =>
            observation.Resolution
                == CombatImpactAttributeTransitionResolution.ConcurrentResidual.ToString()
            && observation.FailureReasons.Count == 0
        )
    )
    {
        throw new InvalidOperationException(
            "A residual card-attribute group was emitted without an auditable failure reason."
        );
    }
    if (report.Sources.ResolvedActions == 0 || report.Sources.MissingStatSourceIdentities.Count > 0)
    {
        throw new InvalidOperationException(
            "Periodic source inventory was vacuous or missed an authoritative CardStats identity."
        );
    }
    if (report.Scenarios.PoisonShieldAdjustment != 0)
        throw new InvalidOperationException("Poison unexpectedly consumed Shield in the corpus.");

    foreach (
        var (name, candidate) in new[]
        {
            ("Burn Shield bridge", report.Rules.BurnShieldBridge),
            ("Burn decay", report.Rules.BurnDecay),
            ("Poison tick", report.Rules.PoisonTick),
            ("Regen tick", report.Rules.RegenTick),
        }
    )
    {
        if (candidate.Observed == 0)
        {
            throw new InvalidOperationException(
                $"{name} diagnostic was vacuous: observed={candidate.Observed}."
            );
        }
    }

    if (report.Rules.ShieldLedger.Mismatched != 0)
        throw new InvalidOperationException("Shield pool ledger did not reconcile.");
    if (
        report.TerminalImpact.CombatantsWithMultipleDeathEvents != 0
        || report.TerminalImpact.FramesAfterFirstDeathEvent != 0
        || report.TerminalImpact.PeriodicAdjustmentFramesAfterFirstDeathEvent != 0
    )
    {
        throw new InvalidOperationException(
            "Terminal periodic-impact policy encountered a replay that continued after death."
        );
    }
    if (report.Attribution.Results == 0)
        throw new InvalidOperationException("Attribution model produced no source results.");
    if (
        report.Attribution.Results
        != report.Attribution.ExactResults + report.Attribution.ProportionalResults
    )
    {
        throw new InvalidOperationException(
            "Attribution result counts did not reconcile across proof classes."
        );
    }
    if (
        report.Attribution.HealthByKind.Values.Sum()
            != report.Attribution.ExactHealthAmount + report.Attribution.ProportionalHealthAmount
        || report.Attribution.ShieldByKind.Values.Sum()
            != report.Attribution.ExactShieldAmount + report.Attribution.ProportionalShieldAmount
    )
    {
        throw new InvalidOperationException(
            "Attributed amounts did not reconcile across proof classes."
        );
    }
    if (
        report.Attribution.AllocationDecisions
        != report.Attribution.ExactAllocationDecisions
            + report.Attribution.ProportionalAllocationDecisions
    )
    {
        throw new InvalidOperationException(
            "Allocation decision counts did not reconcile across proof classes."
        );
    }
    if (
        report.Attribution.AllocatedHealthByKindAndProof.Values.Sum()
            != report.Attribution.HealthByKind.Values.Sum()
        || report.Attribution.AllocatedShieldByKindAndProof.Values.Sum()
            != report.Attribution.ShieldByKind.Values.Sum()
    )
    {
        throw new InvalidOperationException(
            "Allocation decision amounts did not reconcile with attributed totals."
        );
    }
    if (
        report.Attribution.RegenPoolAcceptedOnDeathEventFrames
        != report.Attribution.RegenRealizedOnDeathEventFrames
            + report.Attribution.RegenPoolAcceptedAfterNonPositiveHealthOnDeathEventFrames
    )
    {
        throw new InvalidOperationException(
            "Death-frame Regen did not partition into pre-lethal realized and post-lethal excluded amounts."
        );
    }
    var totals = report.Rules.RealizedTotals;
    RequireTerminalMeasurement(
        "Burn Health",
        report.Adjustments.GetValueOrDefault("Burn/Health/Loss")?.AbsoluteAmount ?? 0,
        report.TerminalImpact.PeriodicHealthLossOnDeathEventFrames.GetValueOrDefault("Burn"),
        report.TerminalImpact.AtomicBurnGroupCombatEffectiveHealthLossOnDeathEventFrames,
        totals.BurnHealthDamage
    );
    RequireTerminalMeasurement(
        "Burn Shield",
        report.Adjustments.GetValueOrDefault("Burn/Shield/Loss")?.AbsoluteAmount ?? 0,
        report.TerminalImpact.BurnShieldConsumedOnDeathEventFrames,
        report.TerminalImpact.AtomicBurnGroupCombatEffectiveShieldConsumedOnDeathEventFrames,
        totals.BurnShieldConsumed
    );
    RequireTerminalMeasurement(
        "Poison Health",
        report.Adjustments.GetValueOrDefault("Poison/Health/Loss")?.AbsoluteAmount ?? 0,
        report.TerminalImpact.PeriodicHealthLossOnDeathEventFrames.GetValueOrDefault("Poison"),
        report.TerminalImpact.CombatEffectivePeriodicHealthLossOnDeathEventFrames.GetValueOrDefault(
            "Poison"
        ),
        totals.PoisonHealthDamage
    );
    RequireConservation(
        "Burn Health",
        report.Attribution.HealthByKind.GetValueOrDefault("Burn"),
        report.Attribution.UnattributedHealthByKind.GetValueOrDefault("Burn"),
        totals.BurnHealthDamage
    );
    RequireConservation(
        "Burn Shield",
        report.Attribution.ShieldByKind.GetValueOrDefault("Burn"),
        report.Attribution.UnattributedShieldByKind.GetValueOrDefault("Burn"),
        totals.BurnShieldConsumed
    );
    RequireConservation(
        "Poison Health",
        report.Attribution.HealthByKind.GetValueOrDefault("Poison"),
        report.Attribution.UnattributedHealthByKind.GetValueOrDefault("Poison"),
        totals.PoisonHealthDamage
    );
    RequireConservation(
        "Regen Health",
        report.Attribution.HealthByKind.GetValueOrDefault("Regen"),
        report.Attribution.UnattributedHealthByKind.GetValueOrDefault("Regen"),
        totals.RegenRealized
    );
    if (
        report.Attribution.ShieldByKind.GetValueOrDefault("Poison") != 0
        || report.Attribution.ShieldByKind.GetValueOrDefault("Regen") != 0
    )
    {
        throw new InvalidOperationException(
            "Attribution model booked non-Burn periodic Shield impact."
        );
    }
    var attributedRegen = report.Attribution.HealthByKind.GetValueOrDefault("Regen");
    if (report.Attribution.RegenStableSpawnBaselineAttributed != 0)
    {
        throw new InvalidOperationException(
            "Stable spawn Regen baseline was attributed to an item or skill: "
                + report.Attribution.RegenStableSpawnBaselineAttributed
        );
    }
    var sourceAttributableRegen =
        totals.RegenRealized - report.Attribution.RegenStableSpawnBaselineRealized;
    if (attributedRegen * 100 < sourceAttributableRegen * 94)
    {
        throw new InvalidOperationException(
            "Regen source-attributable realized-impact coverage regressed below 94%: "
                + $"{attributedRegen}/{sourceAttributableRegen}."
        );
    }
}

static void RequireTerminalMeasurement(
    string name,
    long raw,
    long rawOnDeathFrames,
    long effectiveOnDeathFrames,
    long measured
)
{
    var expected = raw - rawOnDeathFrames + effectiveOnDeathFrames;
    if (measured != expected)
    {
        throw new InvalidOperationException(
            $"{name} terminal measurement did not reconcile: "
                + $"raw={raw} death_raw={rawOnDeathFrames} "
                + $"death_effective={effectiveOnDeathFrames} measured={measured}."
        );
    }
}

static void RequireConservation(string name, long attributed, long unknown, long measured)
{
    if (attributed < 0 || unknown < 0 || attributed + unknown != measured)
    {
        throw new InvalidOperationException(
            $"{name} attribution did not conserve its measured ledger: "
                + $"attributed={attributed} unknown={unknown} measured={measured}."
        );
    }
}

internal sealed record BoundarySample(
    string BattleId,
    int Frames,
    int SpawnedCards,
    int PeriodicStats,
    int PeriodicAdjustments
);

internal sealed record CorpusLoadResult(
    string SourceKind,
    int SourceCount,
    List<ReplayObservationInput> Replays,
    List<string> InvalidPayloads,
    IReadOnlyList<CorpusSourceArtifact> SourceArtifacts
);

internal sealed record CorpusSourceArtifact(string Name, string? Sha256);

internal sealed record VerificationInputArtifact(
    string Role,
    string Name,
    string Sha256,
    string? Version
);
