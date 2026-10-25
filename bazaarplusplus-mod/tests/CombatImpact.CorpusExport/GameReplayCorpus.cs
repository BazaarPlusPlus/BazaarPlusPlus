#nullable enable
using System.Security.Cryptography;
using BazaarGameShared;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.ModApi.Bundle;
using MessagePack;

/// <summary>One battle as the game sent it: the spawn state and the finished combat.</summary>
internal sealed record GameReplay(string BattleId, GameSim Spawn, CombatSim Combat);

/// <summary>
/// The game replays in a corpus directory: a <c>run-bundles/</c> cache of sealed run bundles,
/// raw V5 <c>*.bundle</c> files, or a replay payload store, sampled to
/// <c>BPP_BUNDLE_CORPUS_LIMIT</c> bundles.
/// </summary>
internal sealed record GameReplayCorpus(
    string SourceKind,
    int SourceCount,
    List<GameReplay> Replays,
    List<string> InvalidPayloads,
    IReadOnlyList<CorpusSourceArtifact> SourceArtifacts
)
{
    private const string BundleSampleLimitEnvironmentVariable = "BPP_BUNDLE_CORPUS_LIMIT";
    private const int DefaultBundleSampleLimit = 100;

    internal static GameReplayCorpus Load(string corpusPath)
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

    private static string[] SampleBundlePaths(string rootPath, string searchPattern) =>
        Directory
            .EnumerateFiles(rootPath, searchPattern, SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Take(BundleSampleLimit())
            .ToArray();

    private static int BundleSampleLimit()
    {
        var configured = Environment.GetEnvironmentVariable(BundleSampleLimitEnvironmentVariable);
        return int.TryParse(configured, out var limit) && limit > 0
            ? limit
            : DefaultBundleSampleLimit;
    }

    private static GameReplayCorpus LoadReplayPayloadCorpus(string corpusPath)
    {
        var store = new CombatReplayPayloadStore(corpusPath);
        var battleIds = store.ListBattleIds().Order(StringComparer.Ordinal).ToArray();
        if (battleIds.Length == 0)
            throw new InvalidOperationException($"Replay corpus is empty: {corpusPath}");

        var replays = new List<GameReplay>();
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

        return new GameReplayCorpus(
            "replay_payloads",
            battleIds.Length,
            replays,
            invalidPayloads,
            battleIds.Select(battleId => new CorpusSourceArtifact(battleId, null)).ToArray()
        );
    }

    private static GameReplayCorpus LoadBundleCorpus(
        string sourceKind,
        IReadOnlyList<string> bundlePaths
    )
    {
        var replays = new List<GameReplay>();
        var invalidPayloads = new List<string>();
        var sourceArtifacts = new List<CorpusSourceArtifact>();
        var seenBattleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bundlePath in bundlePaths)
        {
            var bundleName = Path.GetFileName(bundlePath);
            byte[] bundleBytes;
            try
            {
                bundleBytes = ReadBytes(bundlePath);
                sourceArtifacts.Add(
                    new CorpusSourceArtifact(
                        bundleName,
                        Convert.ToHexString(SHA256.HashData(bundleBytes)).ToLowerInvariant()
                    )
                );
            }
            catch (Exception exception)
            {
                invalidPayloads.Add($"{bundleName}:{exception.GetType().Name}");
                continue;
            }

            var opened = RunBundleV6Contract.Open(bundleBytes);
            if (opened.Succeeded && opened.Value != null)
            {
                foreach (var battle in opened.Value.Payload.Battles)
                {
                    AddBundleReplay(
                        replays,
                        invalidPayloads,
                        seenBattleIds,
                        battle.BattleId,
                        battle.Replay?.SpawnMessageBytes,
                        battle.Replay?.CombatMessageBytes
                    );
                }
                continue;
            }

            if (
                !MessagePackGzipCodec.TryDeserialize<ContractlessRunArtifact>(
                    bundleBytes,
                    out var contractless,
                    out var contractlessError,
                    out _
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
                AddBundleReplay(
                    replays,
                    invalidPayloads,
                    seenBattleIds,
                    battle.BattleId,
                    battle.ReplayPayload?.SpawnMessageBytes,
                    battle.ReplayPayload?.CombatMessageBytes
                );
            }
        }

        if (replays.Count == 0)
            throw new InvalidOperationException(
                "Sampled run bundles contained no replay payloads."
            );
        return new GameReplayCorpus(
            sourceKind,
            bundlePaths.Count,
            replays,
            invalidPayloads,
            sourceArtifacts.OrderBy(artifact => artifact.Name, StringComparer.Ordinal).ToArray()
        );
    }

    private static byte[] ReadBytes(string path)
    {
        using var file = File.OpenRead(path);
        using var buffer = new MemoryStream();
        file.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void AddBundleReplay(
        ICollection<GameReplay> replays,
        ICollection<string> invalidPayloads,
        ISet<string> seenBattleIds,
        string battleId,
        byte[]? spawnBytes,
        byte[]? combatBytes
    )
    {
        if (
            string.IsNullOrWhiteSpace(battleId)
            || spawnBytes is not { Length: > 0 }
            || combatBytes is not { Length: > 0 }
            || !seenBattleIds.Add(battleId)
        )
        {
            return;
        }
        AddReplay(replays, invalidPayloads, battleId, spawnBytes, combatBytes);
    }

    private static void AddReplay(
        ICollection<GameReplay> replays,
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
            replays.Add(new GameReplay(battleId, spawnMessage.Data, combatMessage.Data));
        }
        catch (Exception exception)
        {
            invalidPayloads.Add($"{battleId}:{exception.GetType().Name}");
        }
    }
}
