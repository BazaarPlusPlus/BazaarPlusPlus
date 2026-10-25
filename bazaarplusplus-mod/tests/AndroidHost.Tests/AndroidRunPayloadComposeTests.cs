using BazaarPlusPlus.Android;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The Android compose path as the host wires it: Core's net10.0 <see cref="RunPayloadComposer"/>
/// over a history database every store opened with <see cref="AndroidComposition.DatabaseOptions"/>,
/// with the host's own <see cref="AndroidPvpBattleEvidence"/>.
/// </summary>
public sealed class AndroidRunPayloadComposeTests : IDisposable
{
    private const string RunId = "run-android";
    private const string AccountId = "account-android";
    private static readonly DateTimeOffset Started = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "bpp-android-compose-" + Guid.NewGuid().ToString("N")
    );

    public AndroidRunPayloadComposeTests() => Directory.CreateDirectory(_dataRoot);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dataRoot, recursive: true);
    }

    private string Database => PathConstants.RunLogDatabase(_dataRoot);

    private string Replays => PathConstants.CombatReplays(_dataRoot);

    [Fact]
    public void A_completed_run_composes_its_battles_with_interop_enum_values_and_replays()
    {
        CompletedRunWithBattles(("battle-1", 1), ("battle-2", 2));
        var replays = new CombatReplayPayloadStore(Replays);
        replays.Save(ReplayMessages.Payload("battle-1"));
        replays.Save(ReplayMessages.Payload("battle-2"));

        var composition = Composer().Compose(RunId, AccountId);

        var payload = composition.Payload;
        Assert.Empty(composition.ReplayFailures);
        // A Missing set leaves its battle unreplayable under the shared Run Bundle contract.
        Assert.Equal(new[] { "battle-1" }, payload.ReplayableBattleIds);
        Assert.Equal(new[] { "battle-2" }, payload.Degradation.ReplayOmittedBattleIds);
        var final = payload.Battles[1];
        Assert.True(final.Facts.IsFinalBattle);
        Assert.Equal(1, final.Participants.Player.HandItemCount);
        Assert.Equal(1, final.Participants.Player.SkillCount);
        Assert.Null(final.Participants.Opponent.SkillCount);
        var hand = final.Snapshots!.CardSets.Single(set => set.Label == "player_hand");
        Assert.Equal("Captured", hand.Status);
        Assert.Equal("OpeningMessage", hand.Source);
        var item = hand.Items.Single(card => card.InstanceId == "item");
        Assert.Equal((int)ECardType.Item, item.Type);
        Assert.Equal(2, item.Size);
        Assert.Equal(0, item.Section);
        Assert.Equal(3, item.Socket);
        var effect = hand.Items.Single(card => card.InstanceId == "effect");
        Assert.Equal((int)ECardType.SocketEffect, effect.Type);
        var skills = final.Snapshots!.CardSets.Single(set => set.Label == "player_skills");
        Assert.Equal("LiveRetry", skills.Source);
        Assert.Null(skills.Items.Single().Section);
        Assert.Equal(
            "Missing",
            final.Snapshots.CardSets.Single(set => set.Label == "opponent_skills").Status
        );
    }

    [Fact]
    public void A_replay_with_trailing_bytes_is_reported_and_its_battle_is_not_replayable()
    {
        CompletedRunWithBattles(("battle-1", 1));
        var corrupt = ReplayMessages.Payload("battle-1");
        corrupt.SpawnMessageBytes = [.. corrupt.SpawnMessageBytes, 0xC0];
        new CombatReplayPayloadStore(Replays).Save(corrupt);

        var composition = Composer().Compose(RunId, AccountId);

        var failure = Assert.Single(composition.ReplayFailures);
        Assert.Equal("battle-1", failure.BattleId);
        Assert.False(failure.Unreadable);
        Assert.IsType<InvalidDataException>(failure.Exception);
        Assert.Empty(composition.Payload.ReplayableBattleIds);
        Assert.Equal(new[] { "battle-1" }, composition.Payload.Degradation.ReplayOmittedBattleIds);
    }

    [Fact]
    public void Composing_keeps_the_database_in_delete_journal_mode()
    {
        CompletedRunWithBattles(("battle-1", 1));
        new CombatReplayPayloadStore(Replays).Save(ReplayMessages.Payload("battle-1"));

        Composer().Compose(RunId, AccountId);
        SqliteConnection.ClearAllPools();

        Assert.False(File.Exists(Database + "-wal"));
        using var connection = new SqliteConnection($"Data Source={Database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("delete", command.ExecuteScalar() as string);
    }

    [Fact]
    public void The_frozen_account_wins_and_battles_resolve_it_when_none_was_frozen()
    {
        CompletedRunWithBattles(("battle-1", 1));
        var composer = Composer();

        Assert.Equal("frozen", composer.ResolvePlayerAccountId(RunId, "frozen"));
        Assert.Equal(AccountId, composer.ResolvePlayerAccountId(RunId, null));
    }

    private RunPayloadComposer Composer() =>
        new(
            Database,
            Replays,
            AndroidPvpBattleEvidence.Instance,
            AndroidComposition.DatabaseOptions
        );

    private void CompletedRunWithBattles(params (string BattleId, int Day)[] battles)
    {
        var runs = new RunLogStore(new DataRoot(_dataRoot), AndroidComposition.DatabaseOptions);
        runs.CreateRun(
            new RunLogCreateRequest
            {
                RunId = RunId,
                StartedAtUtc = Started,
                Hero = "Vanessa",
                GameMode = "Ranked",
                ModVersion = "6.0.0",
            }
        );
        var store = new PvpBattleSqliteStore(Database, AndroidComposition.DatabaseOptions);
        foreach (var (battleId, day) in battles)
            store.Save(Battle(battleId, day));
        runs.CompleteRun(
            RunId,
            new RunLogCompletion { EndedAtUtc = Started.AddMinutes(30), Status = "completed" }
        );
    }

    // The documents an Android capture is stored as: enum member names, as the codec writes them.
    private static PvpBattleRecord Battle(string battleId, int day) =>
        new()
        {
            BattleId = battleId,
            RunId = RunId,
            RecordedAtUtc = Started.AddMinutes(day * 5),
            CombatKind = "PVPCombat",
            Day = day,
            Hour = 6,
            Participants = new PvpBattleParticipants
            {
                PlayerAccountId = AccountId,
                OpponentAccountId = "account-opponent",
            },
            Outcome = new PvpBattleOutcome
            {
                Result = "win",
                WinnerCombatantId = "Player",
                LoserCombatantId = "Opponent",
            },
            Snapshots = new PvpBattleSnapshotDocuments
            {
                PlayerHand = Set(
                    PvpBattleCaptureStatus.Captured,
                    PvpBattleCaptureSource.OpeningMessage,
                    Card("item", "Item", "Medium", "Hand", "Socket_3"),
                    Card("effect", "SocketEffect", "Small", "Hand", "Socket_0")
                ),
                PlayerSkills = Set(
                    PvpBattleCaptureStatus.Captured,
                    PvpBattleCaptureSource.LiveRetry,
                    Card("skill", "Skill", "Medium", null, null)
                ),
                OpponentHand = Set(
                    PvpBattleCaptureStatus.CapturedEmpty,
                    PvpBattleCaptureSource.OpeningMessage
                ),
                // The last battle's opponent skills were never captured.
                OpponentSkills =
                    day == 2
                        ? Set(PvpBattleCaptureStatus.Missing, PvpBattleCaptureSource.Unknown)
                        : Set(
                            PvpBattleCaptureStatus.CapturedEmpty,
                            PvpBattleCaptureSource.OpeningMessage
                        ),
            },
        };

    private static string Set(
        PvpBattleCaptureStatus status,
        PvpBattleCaptureSource source,
        params PvpBattleCardDocument[] items
    ) =>
        PvpBattleCardSetDocument.Serialize(
            new PvpBattleCardSetDocument
            {
                Status = status,
                Source = source,
                Items = items,
            }
        );

    private static PvpBattleCardDocument Card(
        string instanceId,
        string type,
        string size,
        string? section,
        string? socket
    ) =>
        new()
        {
            InstanceId = instanceId,
            TemplateId = Guid.Empty.ToString(),
            Type = type,
            Size = size,
            Section = section,
            Socket = socket,
            Tier = "Bronze",
            Enchant = string.Empty,
        };

    private sealed class DataRoot(string path) : IPathProvider
    {
        public string? DataRootDirectoryPath => path;
    }
}
