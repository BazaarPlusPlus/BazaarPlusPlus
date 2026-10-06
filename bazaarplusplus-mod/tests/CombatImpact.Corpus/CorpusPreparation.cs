#nullable enable
using System.IO.Compression;
using BazaarGameShared;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.Infra.Serialization;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using MessagePack;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

/// <summary>
/// <c>prepare &lt;corpus-dir&gt; &lt;replay-store&gt; [battle-id ...]</c>: builds the committed
/// pinned corpus. Run it from the mod root; <c>tests/CombatImpact.Corpus/evidence/README.md</c>
/// owns the procedure. It writes the Ghost concert-hall fixtures of
/// <c>tests/PostCombatImpact.Tests/Fixtures/</c> as a replay payload, copies each named battle
/// out of a local <c>CombatReplays</c> store with the opponent's identity scrubbed, and trims
/// <c>GameData.db</c> to the <c>cards</c> rows the corpus can read.
/// </summary>
internal static class CorpusPreparation
{
    private const string ConcertHallBattleId = "ghost-concert-hall-tempo-20260907";
    private const string FixtureDirectory = "tests/PostCombatImpact.Tests/Fixtures";
    private const string TrimmedGameDataFileName = "GameData.cards.db";

    internal static void Run(IReadOnlyList<string> arguments, string gameDataPath)
    {
        if (arguments.Count < 2)
        {
            throw new ArgumentException(
                "Usage: CombatImpact.Corpus prepare <corpus-dir> <replay-store> [battle-id ...]"
            );
        }

        var corpusPath = Path.GetFullPath(arguments[0]);
        var source = new CombatReplayPayloadStore(Path.GetFullPath(arguments[1]));
        var target = new CombatReplayPayloadStore(corpusPath);
        if (Directory.Exists(corpusPath))
            Directory.Delete(corpusPath, recursive: true);
        Directory.CreateDirectory(corpusPath);

        var replays = new List<(GameSim Spawn, CombatSim Combat)> { SaveConcertHall(target) };
        foreach (var battleId in arguments.Skip(2))
        {
            var loaded = source.LoadDetailed(battleId);
            if (loaded.Status != FileBackedPayloadLoadStatus.Loaded || loaded.Payload == null)
                throw new InvalidOperationException(
                    $"Replay {battleId} did not load: {loaded.Status}."
                );
            replays.Add(SaveScrubbed(target, loaded.Payload));
        }

        var trimmedPath = Path.Combine(corpusPath, TrimmedGameDataFileName);
        var kept = TrimGameData(gameDataPath, trimmedPath, ReferencedTemplates(replays));
        Console.WriteLine(
            $"PREPARE corpus={corpusPath} battles={replays.Count} cards={kept} game_data={trimmedPath}"
        );
    }

    private static (GameSim, CombatSim) SaveConcertHall(CombatReplayPayloadStore target)
    {
        var spawn = MessagePackSerializer.Deserialize<GameSim>(
            Gunzip(Path.Combine(FixtureDirectory, ConcertHallBattleId + ".spawn.mpack.gz"))
        );
        var combat = MessagePackSerializer.Deserialize<CombatSim>(
            Gunzip(Path.Combine(FixtureDirectory, ConcertHallBattleId + ".combat.mpack.gz"))
        );
        Save(target, ConcertHallBattleId, version: 1, spawn, combat);
        return (spawn, combat);
    }

    private static (GameSim, CombatSim) SaveScrubbed(
        CombatReplayPayloadStore target,
        PvpReplayPayload payload
    )
    {
        var spawn = MessagePackSerializer
            .Deserialize<NetMessageGameSim>(payload.SpawnMessageBytes, MessagePackConfig.Options)
            .Data;
        var combat = MessagePackSerializer
            .Deserialize<NetMessageCombatSim>(payload.CombatMessageBytes, MessagePackConfig.Options)
            .Data;
        ScrubOpponent(spawn);
        Save(target, payload.BattleId, payload.Version, spawn, combat);
        return (spawn, combat);
    }

    /// <summary>
    /// The repository is public. The only identity a replay carries is the opponent block of the
    /// spawn state (name, titles, rank and rating, account id, cosmetic and collection ids);
    /// the projection reads none of it. The despawn message is never read and is written empty.
    /// </summary>
    private static void ScrubOpponent(GameSim spawn)
    {
        if (spawn.CurrentState?.PvpOpponent is not { } opponent)
            return;
        opponent.Name = "Opponent";
        opponent.TitlePrefix = "";
        opponent.TitleSuffix = "";
        opponent.Rank = null;
        opponent.Rating = 0;
        opponent.Division = 0;
        opponent.Victories = 0;
        opponent.Prestige = 0;
        opponent.Level = 0;
        opponent.PlayerLoadout = new BazaarCollectionLoadout { accountId = "" };
        opponent.PlayerCollection = [];
    }

    // The game's NetMessage constructors assign a random MessageId; fixed ids keep re-running
    // prepare from changing the payload content.
    private static void Save(
        CombatReplayPayloadStore target,
        string battleId,
        int version,
        GameSim spawn,
        CombatSim combat
    ) =>
        target.Save(
            new PvpReplayPayload
            {
                BattleId = battleId,
                Version = version,
                SpawnMessageBytes = MessagePackSerializer.Serialize(
                    new NetMessageGameSim(spawn) { MessageId = "spawn" },
                    MessagePackConfig.Options
                ),
                CombatMessageBytes = MessagePackSerializer.Serialize(
                    new NetMessageCombatSim(combat) { MessageId = "combat" },
                    MessagePackConfig.Options
                ),
                DespawnMessageBytes = MessagePackSerializer.Serialize(
                    new NetMessageGameSim(new GameSim()) { MessageId = "despawn" },
                    MessagePackConfig.Options
                ),
            }
        );

    /// <summary>The template ids <c>CombatImpactCorpusCatalog.BuildEntities</c> looks up.</summary>
    private static HashSet<Guid> ReferencedTemplates(
        IEnumerable<(GameSim Spawn, CombatSim Combat)> replays
    )
    {
        var templates = new HashSet<Guid>();
        foreach (var (spawn, combat) in replays)
        {
            foreach (var spawned in spawn.Events.OfType<GameSimEventCardSpawned>())
            {
                if (Guid.TryParse(spawned.TemplateId, out var id))
                    templates.Add(id);
            }
            foreach (var snapshot in CombatImpactTransformedCardSnapshotReader.Read(combat))
            {
                if (Guid.TryParse(snapshot.Card.TemplateId, out var id))
                    templates.Add(id);
            }
        }
        return templates;
    }

    /// <summary>
    /// Copies the referenced rows plus every PlayerEffect row:
    /// <c>CombatImpactImplicitPlayerEffectReader.AddMissing</c> matches observed effects against
    /// all PlayerEffect templates and resolves only a unique match, so dropping one could turn an
    /// ambiguous signature into a resolved one.
    /// </summary>
    private static int TrimGameData(string sourcePath, string targetPath, ISet<Guid> referenced)
    {
        using var source = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString()
        );
        source.Open();
        using var target = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = targetPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString()
        );
        target.Open();

        using (var create = target.CreateCommand())
        {
            create.CommandText = Scalar(
                source,
                "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'cards'"
            );
            create.ExecuteNonQuery();
        }

        var settings = new BazaarJsonSerializerSettings(validate: false);
        var kept = 0;
        using var transaction = target.BeginTransaction();
        using var insert = target.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO cards (Id, Data) VALUES ($id, $data)";
        var idParameter = insert.Parameters.Add("$id", SqliteType.Text);
        var dataParameter = insert.Parameters.Add("$data", SqliteType.Blob);
        using var read = source.CreateCommand();
        read.CommandText = "SELECT Id, Data FROM cards ORDER BY Id";
        using var reader = read.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var data = (byte[])reader.GetValue(1);
            if (!Guid.TryParse(id, out var templateId))
                continue;
            if (!referenced.Contains(templateId) && !IsPlayerEffect(data, settings))
                continue;
            idParameter.Value = id;
            dataParameter.Value = data;
            insert.ExecuteNonQuery();
            kept++;
        }
        transaction.Commit();

        using var vacuum = target.CreateCommand();
        vacuum.CommandText = "VACUUM";
        vacuum.ExecuteNonQuery();
        return kept;
    }

    private static bool IsPlayerEffect(byte[] data, BazaarJsonSerializerSettings settings) =>
        JsonConvert
            .DeserializeObject<TCardBase>(System.Text.Encoding.UTF8.GetString(data), settings)
            ?.Type == ECardType.PlayerEffect;

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }

    private static byte[] Gunzip(string path)
    {
        using var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        return bytes.ToArray();
    }
}
