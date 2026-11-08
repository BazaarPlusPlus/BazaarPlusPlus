using BazaarPlusPlus.HistorySampler;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HistorySampler.Tests;

public sealed class SampleStoreTests : IDisposable
{
    private const string LocalAccount = "account-local";
    private const string Uploader = "account-uploader";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "bpp-history-sampler-" + Guid.NewGuid().ToString("N")
    );

    public SampleStoreTests()
    {
        Directory.CreateDirectory(_root);
        using var connection = Connect();
        RunLogSchema.EnsureInitialized(connection);
        Execute(
            connection,
            $"""
            INSERT INTO runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode, player_account_id)
            VALUES ('local-run', 1, 1, 'completed', 'Vanessa', 'ranked', '{LocalAccount}');
            INSERT INTO runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode, upload_policy, mod_version)
            VALUES ('imported-v5', 2, 2, 'completed', 'Mak', 'ranked', 'history_only', '5.0.0');
            """
        );
    }

    [Fact]
    public void Import_writes_the_uploaders_run_as_the_local_players_history_only_run()
    {
        var result = new SampleResult();
        var store = new SampleStore(_root, SqliteOpenOptions.Default);

        Assert.True(store.Import(Open(BuildBundle()), store.ResolveLocalPlayer(null), result));

        var runId = Assert.Single(result.RunIds);
        using var connection = Connect();
        Assert.Equal(
            $"completed|Pygmalien|history_only|sample:6.2.5|{LocalAccount}|7|5|2",
            Query(
                connection,
                $"SELECT status || '|' || hero || '|' || upload_policy || '|' || mod_version || '|' || player_account_id || '|' || final_day || '|' || victories || '|' || losses FROM runs WHERE run_id = '{runId}';"
            )
        );
        // Both battles join the run as the local player's; only the one with a replay is ready.
        Assert.Equal(
            $"{runId}-00|ready|{LocalAccount}|Sample|Rival|1,{runId}-01|missing|{LocalAccount}|Sample|Second|0",
            Query(
                connection,
                $"""
                SELECT group_concat(battle_id || '|' || local_payload_state || '|' || player_account_id || '|' || player_name || '|' || opponent_name || '|' || (s.battle_id IS NOT NULL AND s.player_hand_json LIKE '%Cannon%'), ',')
                FROM (SELECT * FROM battles WHERE run_id = '{runId}' ORDER BY battle_id) b
                LEFT JOIN battle_snapshots s USING (battle_id);
                """
            )
        );
        Assert.True(
            File.Exists(Path.Combine(_root, "CombatReplays", $"{runId}-00.payload.mpack.gz"))
        );
        Assert.Equal(
            ["put CombatReplays/" + runId + "-00.payload.mpack.gz"],
            result.Files.Select(f =>
                $"{(f.Written ? "put" : "rm")} {f.RelativePath.Replace('\\', '/')}"
            )
        );
        // A sampled run is never sealed, protected or resumed, so nothing uploads it.
        Assert.Equal(
            "0",
            Query(
                connection,
                $"SELECT COUNT(*) FROM runs AS r WHERE r.run_id = '{runId}' AND ({BundleQueueStore.ProtectedRunCondition("r")});"
            )
        );
        Assert.False(
            store.Import(Open(BuildBundle()), store.ResolveLocalPlayer(null), new SampleResult())
        );
    }

    [Fact]
    public void Clear_removes_only_sampled_runs_with_their_battles_favorites_and_files()
    {
        var store = new SampleStore(_root, SqliteOpenOptions.Default);
        var imported = new SampleResult();
        store.Import(Open(BuildBundle()), store.ResolveLocalPlayer(null), imported);
        var runId = imported.RunIds.Single();
        using (var connection = Connect())
            Execute(
                connection,
                $"""
                INSERT INTO favorite_battles (battle_id, source, favorited_at_ms, favorite_payload_state, recorded_at_ms, combat_kind)
                SELECT battle_id, 'LOCAL', 3, 'ready', recorded_at_ms, combat_kind FROM battles WHERE battle_id = '{runId}-00';
                """
            );
        Directory.CreateDirectory(Path.Combine(_root, "FavoriteBattles"));
        File.WriteAllText(
            Path.Combine(_root, "FavoriteBattles", $"{runId}-00.favorite.mpack.gz"),
            "x"
        );

        var cleared = store.Clear();

        Assert.Equal([runId], cleared.RunIds);
        Assert.Equal(2, cleared.Battles);
        using var after = Connect();
        Assert.Equal(
            "imported-v5,local-run",
            Query(
                after,
                "SELECT group_concat(run_id, ',') FROM (SELECT run_id FROM runs ORDER BY run_id);"
            )
        );
        Assert.Equal("0", Query(after, "SELECT COUNT(*) FROM battles;"));
        Assert.Equal("0", Query(after, "SELECT COUNT(*) FROM battle_snapshots;"));
        Assert.Equal("0", Query(after, "SELECT COUNT(*) FROM favorite_battles;"));
        Assert.False(
            File.Exists(Path.Combine(_root, "CombatReplays", $"{runId}-00.payload.mpack.gz"))
        );
        Assert.False(
            File.Exists(Path.Combine(_root, "FavoriteBattles", $"{runId}-00.favorite.mpack.gz"))
        );
    }

    [Fact]
    public void An_account_comes_from_the_newest_local_run_unless_one_is_named()
    {
        var store = new SampleStore(_root, SqliteOpenOptions.Default);

        Assert.Equal(new LocalPlayer(LocalAccount, "Sample"), store.ResolveLocalPlayer(null));
        Assert.Equal("other", store.ResolveLocalPlayer("other").AccountId);
    }

    private static OpenedRunBundleV6 Open(byte[] bytes) =>
        RunBundleV6Contract.Open(bytes).Value
        ?? throw new InvalidOperationException("Bundle does not open.");

    // One run with a replayable battle whose four card sets were captured, and a battle with no
    // card sets or replay.
    private static byte[] BuildBundle()
    {
        const string replayable = "battle-replayable";
        var payload = new RunPayloadV6
        {
            RunId = "uploader-run",
            PlayerAccountId = Uploader,
            Run = new RunFactsV6
            {
                Hero = "Pygmalien",
                GameMode = "ranked",
                Status = "completed",
                StartedAtMs = 1_790_380_000_000,
                EndedAtMs = 1_790_381_000_000,
                Day = 7,
                Victories = 5,
                Losses = 2,
                ModVersion = "6.2.5",
            },
            ReplayableBattleIds = [replayable],
            Battles =
            [
                Battle(replayable, "Rival", withBoards: true),
                Battle("battle-bare", "Second", withBoards: false),
            ],
        };
        return BundleV6Codec
            .Build(
                new BundleBuildInputV6
                {
                    BundleId = "01J00000000000000000000777",
                    RunId = payload.RunId,
                    PlayerAccountId = payload.PlayerAccountId,
                    CreatedAtMs = 1_790_381_000_000,
                    RunPayload = RunPayloadV6Codec.Encode(payload),
                    Battles = payload
                        .Battles.Select(battle => new BundleBattleProjectionV6
                        {
                            BattleId = battle.BattleId,
                            RecordedAtMs = battle.Facts.RecordedAtMs,
                            CombatKind = "pvp",
                            Result = "win",
                            WinnerCombatantId = "Player",
                            LoserCombatantId = "Opponent",
                            Player = new BundleCombatantProjectionV6
                            {
                                AccountId = Uploader,
                                DisplayName = "Uploader",
                            },
                            Opponent = new BundleCombatantProjectionV6
                            {
                                AccountId = "account-opponent",
                                DisplayName =
                                    battle.Participants.Opponent.DisplayName ?? string.Empty,
                            },
                        })
                        .ToList(),
                }
            )
            .Bytes;
    }

    private static RunBattleV6 Battle(string battleId, string opponent, bool withBoards) =>
        new()
        {
            BattleId = battleId,
            Facts = new BattleFactsV6
            {
                RecordedAtMs = 1_790_380_500_000,
                Day = 3,
                CombatKind = "PVPCombat",
                Result = "win",
                WinnerCombatantId = "Player",
                LoserCombatantId = "Opponent",
            },
            Participants = new BattleParticipantsV6
            {
                Player = new BattleParticipantV6
                {
                    AccountId = Uploader,
                    DisplayName = "Uploader",
                    HeroName = "Pygmalien",
                },
                Opponent = new BattleParticipantV6
                {
                    AccountId = "account-opponent",
                    DisplayName = opponent,
                    HeroName = "Mak",
                },
            },
            Snapshots = withBoards
                ? new BattleCardSnapshotsV6
                {
                    CardSets =
                    [
                        .. new[]
                        {
                            "player_hand",
                            "player_skills",
                            "opponent_hand",
                            "opponent_skills",
                        }.Select(label => new BattleCardSetV6
                        {
                            Label = label,
                            Status = "Captured",
                            Items =
                                label == "player_hand"
                                    ?
                                    [
                                        new BattleCardV6
                                        {
                                            InstanceId = "i1",
                                            TemplateId = "t1",
                                            Type = 0,
                                            Size = 2,
                                            Name = "Cannon",
                                        },
                                    ]
                                    : [],
                        }),
                    ],
                }
                : null,
            Replay = withBoards
                ? new BattleReplayV6
                {
                    Version = 1,
                    SpawnMessageBytes = [1, 2, 3],
                    CombatMessageBytes = [4, 5, 6],
                    DespawnMessageBytes = [7, 8, 9],
                }
                : null,
        };

    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(
            $"Data Source={Path.Combine(_root, "bazaarplusplus.db")};Pooling=False"
        );
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? Query(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(
            command.ExecuteScalar(),
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
    }
}
