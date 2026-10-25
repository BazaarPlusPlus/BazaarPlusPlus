using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BazaarPlusPlus.Tests.PvpBattles;

public sealed class PvpBattleCaptureWindowTests
{
    [Fact]
    public void OpeningCombatAndClosingSimCaptureOneBattle()
    {
        using var h = new WindowHarness();

        var capture = h.Battle();

        var manifest = capture.Manifest;
        Assert.Matches("^[0-9a-f]{32}$", manifest.BattleId);
        Assert.Equal("run-1", manifest.RunId);
        Assert.Equal(WindowHarness.Now, manifest.RecordedAtUtc);
        Assert.Equal("PVPCombat", manifest.CombatKind);
        Assert.Equal(5, manifest.Day);
        Assert.Equal(2, manifest.Hour);
        Assert.Equal("enc-pvp", manifest.EncounterId);
        var p = manifest.Participants;
        Assert.Equal(
            ("PlayerOne", "acct-player", "Vanessa", "Diamond", 2100, 10, 2, 7, 12, 6),
            (
                p.PlayerName,
                p.PlayerAccountId,
                p.PlayerHero,
                p.PlayerRank,
                p.PlayerRating,
                p.PlayerLevel,
                p.PlayerPrestige,
                p.PlayerIncome,
                p.PlayerGold,
                p.PlayerVictories
            )
        );
        Assert.Equal(
            ("Rival", "Dooley", "Gold", "acct-rival", 1500, 9, 3, 4),
            (
                p.OpponentName,
                p.OpponentHero,
                p.OpponentRank,
                p.OpponentAccountId,
                p.OpponentRating,
                p.OpponentLevel,
                p.OpponentPrestige,
                p.OpponentVictories
            )
        );
        Assert.Equal(
            new[] { "hand-1", "hand-2" },
            manifest.Snapshots.PlayerHand.Items.Select(card => card.InstanceId)
        );
        Assert.Equal(
            new[] { "opp-1", "opp-2" },
            manifest.Snapshots.OpponentHand.Items.Select(card => card.InstanceId)
        );
        Assert.Equal(PvpBattleCaptureStatus.Captured, manifest.Snapshots.OpponentSkills.Status);
        Assert.DoesNotContain(h.Log, line => line.StartsWith("Warning", StringComparison.Ordinal));
        Assert.Null(h.Feed(FakeMessage.Sim()));
    }

    [Fact]
    public void AnOpeningBeforeTheCombatSimReopensTheWindow()
    {
        using var h = new WindowHarness();
        var first = FakeMessage.Opening();
        first.Texts[PvpOpeningText.OpponentName] = () => "First";
        var second = FakeMessage.Opening();
        second.Texts[PvpOpeningText.OpponentName] = () => "Second";

        Assert.Null(h.Feed(first));
        var capture = h.Battle(opening: second);

        Assert.Equal("Second", capture.Manifest.Participants.OpponentName);
        var spawns = h.Reads.PartsAsked.Where(part => part.Stage == PvpReplayStage.Spawn).ToList();
        Assert.Equal(2, spawns.Count);
        Assert.NotEqual(spawns[0].BattleId, spawns[1].BattleId);
        Assert.Equal(spawns[1].BattleId, capture.Manifest.BattleId);
    }

    [Fact]
    public void AnOpeningAfterTheCombatSimReopensTheWindow()
    {
        using var h = new WindowHarness();
        Assert.Null(h.Feed(FakeMessage.Opening()));
        Assert.Null(h.Feed(FakeMessage.Combat(winner: "Opponent", loser: "Player")));
        var second = FakeMessage.Opening();
        second.Texts[PvpOpeningText.OpponentName] = () => "Second";

        var capture = h.Battle(opening: second);

        Assert.Equal("Second", capture.Manifest.Participants.OpponentName);
        Assert.Equal("win", capture.Manifest.Outcome.Result);
        Assert.Equal(h.Reads.PartsAsked[^1].BattleId, capture.Manifest.BattleId);
    }

    [Fact]
    public void AnotherSimBeforeTheCombatSimAbandonsTheWindow()
    {
        using var h = new WindowHarness();

        Assert.Null(h.Feed(FakeMessage.Opening()));
        Assert.Null(h.Feed(FakeMessage.Sim()));
        Assert.Null(h.Feed(FakeMessage.Combat()));
        Assert.Null(h.Feed(FakeMessage.Sim()));
    }

    [Fact]
    public void ASecondCombatSimAbandonsTheWindow()
    {
        using var h = new WindowHarness();

        Assert.Null(h.Feed(FakeMessage.Opening()));
        Assert.Null(h.Feed(FakeMessage.Combat()));
        Assert.Null(h.Feed(FakeMessage.Combat()));
        Assert.Null(h.Feed(FakeMessage.Sim()));
    }

    [Fact]
    public void ACombatSimWithNoWindowOpenIsIgnored()
    {
        using var h = new WindowHarness();

        Assert.Null(h.Feed(FakeMessage.Combat()));
        Assert.Null(h.Feed(FakeMessage.Sim()));
        Assert.NotNull(h.Battle());
    }

    [Fact]
    public void OtherMessagesAndAThrowingClassifyLeaveTheWindowAsItWas()
    {
        using var h = new WindowHarness();
        var unreadable = FakeMessage.Other();
        h.Reads.Classifier = message =>
            ReferenceEquals(message, unreadable)
                ? throw new InvalidOperationException("proxy gone")
                : message.Signal;

        Assert.Null(h.Feed(FakeMessage.Opening()));
        Assert.Null(h.Feed(FakeMessage.Other()));
        Assert.Null(h.Feed(unreadable));
        Assert.Null(h.Feed(FakeMessage.Combat()));
        Assert.Null(h.Feed(FakeMessage.Other()));
        var capture = h.Feed(FakeMessage.Sim());

        Assert.NotNull(capture);
        var warning = Assert.Single(h.LogLines("pvp_battles.window.classify_failed"));
        Assert.StartsWith("Warning", warning);
    }

    [Fact]
    public void WithPvpOnlyAMonsterCombatOpensNothing()
    {
        using var h = new WindowHarness(PvpOpeningKinds.PvpCombat);

        Assert.Null(h.Feed(FakeMessage.Opening("Combat")));
        Assert.Null(h.Feed(FakeMessage.Combat()));
        Assert.Null(h.Feed(FakeMessage.Sim()));
        Assert.Empty(h.Reads.PartsAsked);
    }

    [Fact]
    public void AMonsterCombatCapturesWithoutAReplay()
    {
        using var h = new WindowHarness();

        var capture = h.Battle(opening: FakeMessage.Opening("Combat"));

        Assert.Equal("Combat", capture.Manifest.CombatKind);
        Assert.Null(capture.Replay);
        Assert.Empty(h.Reads.PartsAsked);
        Assert.Empty(h.LogLines("pvp_battles.replay.part_failed"));
    }

    // Each player board read in turn: a number is that many cards, -1 throws. The opening read
    // comes first, then the live retries at the combat sim and at the close.
    [Theory]
    [InlineData(new[] { 2 }, "Captured/OpeningMessage/2/opening=2", 1)]
    [InlineData(new[] { 0, 2 }, "Captured/OpeningMessage/2/opening=0/live", 2)]
    [InlineData(new[] { 0, 0 }, "CapturedEmpty/OpeningMessage/0/opening=0/live", 2)]
    [InlineData(new[] { 0, -1, 1 }, "Captured/OpeningMessage/1/opening=0/live", 3)]
    [InlineData(new[] { -1, 2 }, "Captured/LiveRetry/2/opening=failed/live", 2)]
    [InlineData(new[] { -1, 0 }, "CapturedEmpty/LiveRetry/0/opening=failed/live", 2)]
    [InlineData(new[] { -1, -1, 1 }, "Captured/LiveRetry/1/opening=failed/live", 3)]
    [InlineData(new[] { -1, -1, -1 }, "Missing/Unknown/0/opening=failed", 3)]
    public void APlayerBoardIsReadLiveUntilOneReadSucceeds(
        int[] reads,
        string summary,
        int readCount
    )
    {
        using var h = new WindowHarness();
        var next = 0;
        h.Reads.PlayerBoards[PvpSnapshotSection.Hand] = () =>
        {
            Assert.True(next < reads.Length, "read more often than scripted");
            var cards = reads[next++];
            if (cards < 0)
                throw new InvalidOperationException("board unreadable");
            return Enumerable
                .Range(0, cards)
                .Select(index => ScriptedBattleReads.Card("hand-" + index))
                .ToList();
        };

        var capture = h.Battle();

        Assert.Equal(summary, WindowHarness.Summary(capture, "player_hand"));
        Assert.Equal(readCount, h.Reads.PlayerBoardReads[PvpSnapshotSection.Hand]);
        Assert.Equal(
            "Captured/OpeningMessage/1/opening=1",
            WindowHarness.Summary(capture, "player_skills")
        );
        var set = capture.Manifest.Snapshots.PlayerHand;
        Assert.Equal(summary.Split('/')[0], set.Status.ToString());
        Assert.Equal(summary.Split('/')[1], set.Source.ToString());
    }

    [Fact]
    public void AnOpponentBoardThatThrowsIsMissingAndNeverReadAgain()
    {
        using var h = new WindowHarness();
        var opening = FakeMessage.Opening();
        opening.Boards[PvpSnapshotSection.Skills] = () =>
            throw new InvalidOperationException("events unreadable");

        var capture = h.Battle(opening: opening);

        var skills = capture.Manifest.Snapshots.OpponentSkills;
        Assert.Equal(PvpBattleCaptureStatus.Missing, skills.Status);
        Assert.Equal(PvpBattleCaptureSource.Unknown, skills.Source);
        Assert.Empty(skills.Items);
        Assert.Equal(
            "Missing/Unknown/0/opening=failed",
            WindowHarness.Summary(capture, "opponent_skills")
        );
        Assert.Equal(
            PvpBattleCaptureStatus.Captured,
            capture.Manifest.Snapshots.OpponentHand.Status
        );
        var degraded = Assert.Single(h.LogLines("pvp_battles.snapshot.degraded"));
        Assert.Contains("combatant=opponent", degraded);
        Assert.Contains("section=skills", degraded);
        Assert.Contains("reason_code=opening_message_exception", degraded);
        Assert.Contains(capture.Manifest.BattleId[..8], degraded);
    }

    [Fact]
    public void ABoardThatComesBackNullIsCapturedEmpty()
    {
        using var h = new WindowHarness();
        var opening = FakeMessage.Opening();
        opening.Boards[PvpSnapshotSection.Hand] = () => null!;

        var capture = h.Battle(opening: opening);

        Assert.Equal(
            "CapturedEmpty/OpeningMessage/0/opening=0",
            WindowHarness.Summary(capture, "opponent_hand")
        );
    }

    [Theory]
    [InlineData(PvpPlayerText.Hero)]
    [InlineData(PvpPlayerText.Name)]
    [InlineData(PvpPlayerText.AccountId)]
    public void APlayerTextThatThrowsIsNullAlone(PvpPlayerText field)
    {
        using var h = new WindowHarness();
        h.Reads.PlayerTexts[field] = () => throw new InvalidOperationException("unreadable");

        var capture = h.Battle();

        AssertOnlyNull(capture.Manifest, Key(field));
        AssertOneUnreadable(h, "player", field.ToString(), capture);
    }

    [Theory]
    [InlineData(PvpPlayerNumber.Level)]
    [InlineData(PvpPlayerNumber.Prestige)]
    [InlineData(PvpPlayerNumber.Income)]
    [InlineData(PvpPlayerNumber.Gold)]
    [InlineData(PvpPlayerNumber.Victories)]
    public void APlayerNumberThatThrowsIsNullAlone(PvpPlayerNumber field)
    {
        using var h = new WindowHarness();
        h.Reads.PlayerNumbers[field] = () => throw new InvalidOperationException("unreadable");

        var capture = h.Battle();

        AssertOnlyNull(capture.Manifest, Key(field));
        AssertOneUnreadable(h, "player", field.ToString(), capture);
    }

    [Theory]
    [InlineData(PvpOpeningText.EncounterId, "battle")]
    [InlineData(PvpOpeningText.OpponentName, "opponent")]
    [InlineData(PvpOpeningText.OpponentHero, "opponent")]
    [InlineData(PvpOpeningText.OpponentRank, "opponent")]
    [InlineData(PvpOpeningText.OpponentAccountId, "opponent")]
    public void AnOpeningTextThatThrowsIsNullAlone(PvpOpeningText field, string side)
    {
        using var h = new WindowHarness();
        var opening = FakeMessage.Opening();
        opening.Texts[field] = () => throw new InvalidOperationException("unreadable");

        var capture = h.Battle(opening: opening);

        AssertOnlyNull(capture.Manifest, Key(field));
        AssertOneUnreadable(h, side, field.ToString(), capture);
    }

    [Theory]
    [InlineData(PvpOpeningNumber.Day, "battle")]
    [InlineData(PvpOpeningNumber.Hour, "battle")]
    [InlineData(PvpOpeningNumber.OpponentRating, "opponent")]
    [InlineData(PvpOpeningNumber.OpponentLevel, "opponent")]
    [InlineData(PvpOpeningNumber.OpponentPrestige, "opponent")]
    [InlineData(PvpOpeningNumber.OpponentVictories, "opponent")]
    public void AnOpeningNumberThatThrowsIsNullAlone(PvpOpeningNumber field, string side)
    {
        using var h = new WindowHarness();
        var opening = FakeMessage.Opening();
        opening.Numbers[field] = () => throw new InvalidOperationException("unreadable");

        var capture = h.Battle(opening: opening);

        AssertOnlyNull(capture.Manifest, Key(field));
        AssertOneUnreadable(h, side, field.ToString(), capture);
    }

    [Fact]
    public void WhitespaceTextIsNoValue()
    {
        using var h = new WindowHarness();
        h.Reads.PlayerTexts[PvpPlayerText.Name] = () => "   ";
        var opening = FakeMessage.Opening();
        opening.Texts[PvpOpeningText.OpponentName] = () => "";
        opening.Texts[PvpOpeningText.OpponentHero] = () => " Dooley ";

        var capture = h.Battle(opening: opening);

        Assert.Null(capture.Manifest.Participants.PlayerName);
        Assert.Null(capture.Manifest.Participants.OpponentName);
        Assert.Equal(" Dooley ", capture.Manifest.Participants.OpponentHero);
        Assert.Empty(h.LogLines("pvp_battles.participant.unreadable"));
    }

    [Fact]
    public void ThePlayerNameAndAccountAreReadAtTheClose()
    {
        using var h = new WindowHarness();
        h.Reads.PlayerTexts[PvpPlayerText.Name] = () => "AtOpening";
        h.Reads.PlayerTexts[PvpPlayerText.AccountId] = () => null;
        h.Reads.PlayerTexts[PvpPlayerText.Hero] = () => "Vanessa";

        Assert.Null(h.Feed(FakeMessage.Opening()));
        Assert.Null(h.Feed(FakeMessage.Combat()));
        h.Reads.PlayerTexts[PvpPlayerText.Name] = () => "AtClose";
        h.Reads.PlayerTexts[PvpPlayerText.AccountId] = () => "acct-close";
        h.Reads.PlayerTexts[PvpPlayerText.Hero] = () => "Pygmalien";
        var capture = Assert.IsType<PvpBattleCapture>(h.Feed(FakeMessage.Sim()));

        Assert.Equal("AtClose", capture.Manifest.Participants.PlayerName);
        Assert.Equal("acct-close", capture.Manifest.Participants.PlayerAccountId);
        Assert.Equal("Vanessa", capture.Manifest.Participants.PlayerHero);
    }

    [Fact]
    public void ARankProbeWithNoSnapshotLeavesRankAndRatingNull()
    {
        using var h = new WindowHarness();
        h.Rank.Rank = () => null;

        var capture = h.Battle();

        Assert.Null(capture.Manifest.Participants.PlayerRank);
        Assert.Null(capture.Manifest.Participants.PlayerRating);
        Assert.Empty(h.LogLines("pvp_battles.participant.unreadable"));
    }

    [Fact]
    public void ARankProbeThatThrowsLeavesTheOtherPlayerFields()
    {
        using var h = new WindowHarness();
        h.Rank.Rank = () => throw new InvalidOperationException("rank unreadable");

        var capture = h.Battle();

        Assert.Null(capture.Manifest.Participants.PlayerRank);
        Assert.Null(capture.Manifest.Participants.PlayerRating);
        Assert.Equal(10, capture.Manifest.Participants.PlayerLevel);
        AssertOneUnreadable(h, "player", "Rank", capture);
    }

    [Fact]
    public void ARunIdFirstSeenAtTheCombatSimIsKept()
    {
        using var h = new WindowHarness();
        h.Run.CurrentServerRunId = null;

        Assert.Null(h.Feed(FakeMessage.Opening()));
        h.Run.CurrentServerRunId = "run-at-combat";
        Assert.Null(h.Feed(FakeMessage.Combat()));
        h.Run.CurrentServerRunId = null;
        var capture = Assert.IsType<PvpBattleCapture>(h.Feed(FakeMessage.Sim()));

        Assert.Equal("run-at-combat", capture.Manifest.RunId);
    }

    [Fact]
    public void TheRunIdAtTheCloseWins()
    {
        using var h = new WindowHarness();

        Assert.Null(h.Feed(FakeMessage.Opening()));
        Assert.Null(h.Feed(FakeMessage.Combat()));
        h.Run.CurrentServerRunId = "run-at-close";
        var capture = Assert.IsType<PvpBattleCapture>(h.Feed(FakeMessage.Sim()));

        Assert.Equal("run-at-close", capture.Manifest.RunId);
    }

    [Fact]
    public void ARunIdGoneAtTheCloseKeepsTheOneHeld()
    {
        using var h = new WindowHarness();

        Assert.Null(h.Feed(FakeMessage.Opening()));
        Assert.Null(h.Feed(FakeMessage.Combat()));
        h.Run.CurrentServerRunId = null;
        var capture = Assert.IsType<PvpBattleCapture>(h.Feed(FakeMessage.Sim()));

        Assert.Equal("run-1", capture.Manifest.RunId);
    }

    [Theory]
    [InlineData("Player", "Opponent", "win")]
    [InlineData("Opponent", "Player", "loss")]
    [InlineData("None", "None", null)]
    [InlineData(null, null, null)]
    public void TheOutcomeIsThePlayersSideOfTheCombatSim(
        string? winner,
        string? loser,
        string? result
    )
    {
        using var h = new WindowHarness();

        var capture = h.Battle(combat: FakeMessage.Combat(winner, loser));

        Assert.Equal(result, capture.Manifest.Outcome.Result);
        Assert.Equal(winner, capture.Manifest.Outcome.WinnerCombatantId);
        Assert.Equal(loser, capture.Manifest.Outcome.LoserCombatantId);
    }

    [Fact]
    public void ReplayPartsAreKeptInStageOrderUnderTheBattleId()
    {
        using var h = new WindowHarness();

        var capture = h.Battle();

        var replay = Assert.IsType<PvpReplayPayload>(capture.Replay);
        var battleId = capture.Manifest.BattleId;
        Assert.Equal(
            new[]
            {
                (PvpReplayStage.Spawn, "opening:PVPCombat", battleId),
                (PvpReplayStage.Combat, "combat", battleId),
                (PvpReplayStage.Despawn, "sim:Choice", battleId),
            },
            h.Reads.PartsAsked
        );
        Assert.Equal(battleId, replay.BattleId);
        Assert.Equal(1, replay.Version);
        Assert.Equal(ScriptedBattleReads.Bytes("opening:PVPCombat"), replay.SpawnMessageBytes);
        Assert.Equal(ScriptedBattleReads.Bytes("combat"), replay.CombatMessageBytes);
        Assert.Equal(ScriptedBattleReads.Bytes("sim:Choice"), replay.DespawnMessageBytes);
    }

    [Fact]
    public void AnEmptySpawnPartSkipsTheLaterPartsAndDropsTheReplay()
    {
        using var h = new WindowHarness();
        h.Reads.Part = (stage, message, _) =>
            PvpReplayPart.Serialized(
                stage == PvpReplayStage.Spawn ? [] : ScriptedBattleReads.Bytes(message.Name)
            );

        var capture = h.Battle();

        Assert.Null(capture.Replay);
        Assert.Equal(new[] { PvpReplayStage.Spawn }, h.Reads.PartsAsked.Select(part => part.Stage));
        var failed = Assert.Single(h.LogLines("pvp_battles.replay.part_failed"));
        Assert.Contains("stage=spawn", failed);
        Assert.Equal(PvpBattleCaptureStatus.Captured, capture.Manifest.Snapshots.PlayerHand.Status);
    }

    [Fact]
    public void AThrowingPartRequestDropsTheReplay()
    {
        using var h = new WindowHarness();
        h.Reads.Part = (stage, message, _) =>
            stage == PvpReplayStage.Combat
                ? throw new InvalidOperationException("cast failed")
                : PvpReplayPart.Serialized(ScriptedBattleReads.Bytes(message.Name));

        var capture = h.Battle();

        Assert.Null(capture.Replay);
        Assert.Equal(
            new[] { PvpReplayStage.Spawn, PvpReplayStage.Combat },
            h.Reads.PartsAsked.Select(part => part.Stage)
        );
        Assert.Contains(
            "stage=combat",
            Assert.Single(h.LogLines("pvp_battles.replay.part_failed"))
        );
    }

    [Fact]
    public void ADeferredPartThatThrowsAtTheCloseDropsTheReplay()
    {
        using var h = new WindowHarness();
        var resolved = new List<PvpReplayStage>();
        h.Reads.Part = (stage, message, _) =>
        {
            var name = message.Name;
            return PvpReplayPart.Deferred(() =>
            {
                resolved.Add(stage);
                return stage == PvpReplayStage.Despawn
                    ? throw new InvalidOperationException("serializer failed")
                    : ScriptedBattleReads.Bytes(name);
            });
        };

        Assert.Null(h.Feed(FakeMessage.Opening()));
        Assert.Null(h.Feed(FakeMessage.Combat()));
        Assert.Empty(resolved);
        var capture = Assert.IsType<PvpBattleCapture>(h.Feed(FakeMessage.Sim()));

        Assert.Null(capture.Replay);
        Assert.Equal(
            new[] { PvpReplayStage.Spawn, PvpReplayStage.Combat, PvpReplayStage.Despawn },
            resolved
        );
        Assert.Contains(
            "stage=despawn",
            Assert.Single(h.LogLines("pvp_battles.replay.part_failed"))
        );
    }

    [Fact]
    public void ADeferredPartIsResolvedOnlyOnce()
    {
        using var h = new WindowHarness();
        var resolutions = 0;
        h.Reads.Part = (_, message, _) =>
        {
            var name = message.Name;
            return PvpReplayPart.Deferred(() =>
            {
                resolutions++;
                return ScriptedBattleReads.Bytes(name);
            });
        };

        var capture = h.Battle();

        Assert.NotNull(capture.Replay);
        Assert.Equal(3, resolutions);
    }

    [Fact]
    public void AFailedCloseResetsTheWindowAndTheNextBattleCapturesCleanly()
    {
        using var h = new WindowHarness();
        var broken = new Exception("clock gone");
        var clockBroken = true;
        var window = new PvpBattleCaptureWindow<FakeMessage>(
            h.Reads,
            h.Run,
            h.Rank,
            () => clockBroken ? throw broken : WindowHarness.Now,
            PvpOpeningKinds.PvpCombat
        );
        PvpBattleCapture? Feed(FakeMessage message)
        {
            var result = window.Observe(message);
            message.Expired = true;
            return result;
        }

        Assert.Null(Feed(FakeMessage.Opening()));
        Assert.Null(Feed(FakeMessage.Combat()));
        Assert.Null(Feed(FakeMessage.Sim()));
        Assert.Single(h.LogLines("pvp_battles.window.observe_failed"));
        Assert.Null(Feed(FakeMessage.Sim()));
        Assert.Null(Feed(FakeMessage.Combat()));

        clockBroken = false;
        var second = FakeMessage.Opening();
        second.Texts[PvpOpeningText.OpponentName] = () => "Next";
        Assert.Null(Feed(second));
        Assert.Null(Feed(FakeMessage.Combat(winner: "Opponent", loser: "Player")));
        var capture = Assert.IsType<PvpBattleCapture>(Feed(FakeMessage.Sim()));

        Assert.Equal("Next", capture.Manifest.Participants.OpponentName);
        Assert.Equal("loss", capture.Manifest.Outcome.Result);
        Assert.NotNull(capture.Replay);
        Assert.Single(h.LogLines("pvp_battles.window.observe_failed"));
    }

    [Fact]
    public void ACapturedBattleRoundTripsThroughTheCatalog()
    {
        using var h = new WindowHarness();
        var capture = h.Battle();
        var directory = Path.Combine(
            Path.GetTempPath(),
            "bpp-pvp-window-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = new PvpBattleCatalog(
                new PvpBattleSqliteStore(Path.Combine(directory, "history.db"))
            );

            catalog.Save(capture.Manifest);
            var loaded = Assert.IsType<PvpBattleManifest>(
                catalog.TryLoad(capture.Manifest.BattleId)
            );

            // The run link is kept only once the run row exists, so it is not asserted here.
            Assert.Equal("PVPCombat", loaded.CombatKind);
            Assert.Equal((5, 2, "enc-pvp"), (loaded.Day, loaded.Hour, loaded.EncounterId));
            Assert.Equal("Rival", loaded.Participants.OpponentName);
            Assert.Equal("acct-player", loaded.Participants.PlayerAccountId);
            Assert.Equal("win", loaded.Outcome.Result);
            var hand = loaded.Snapshots.PlayerHand;
            Assert.Equal(PvpBattleCaptureStatus.Captured, hand.Status);
            Assert.Equal(PvpBattleCaptureSource.OpeningMessage, hand.Source);
            var card = hand.Items[0];
            Assert.Equal(
                ("hand-1", CardType.Item, CardSize.Medium, CardSocket.Socket_0, 10),
                (
                    card.InstanceId,
                    card.Type,
                    card.Size,
                    card.Socket,
                    card.Attributes["DamageAmount"]
                )
            );
            Assert.Equal(
                PvpBattleCaptureSource.OpeningMessage,
                loaded.Snapshots.OpponentSkills.Source
            );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertOneUnreadable(
        WindowHarness h,
        string side,
        string field,
        PvpBattleCapture capture
    )
    {
        var line = Assert.Single(h.LogLines("pvp_battles.participant.unreadable"));
        Assert.StartsWith("Warning", line);
        Assert.Contains($"side={side}", line);
        Assert.Contains(field, line);
        Assert.Contains(capture.Manifest.BattleId[..8], line);
    }

    private static string Key(Enum field) => field.ToString();

    // Every scalar the window reads, by the field name the port uses for it.
    private static Dictionary<string, object?> Scalars(PvpBattleManifest m)
    {
        var p = m.Participants;
        return new()
        {
            ["EncounterId"] = m.EncounterId,
            ["Day"] = m.Day,
            ["Hour"] = m.Hour,
            ["Hero"] = p.PlayerHero,
            ["Name"] = p.PlayerName,
            ["AccountId"] = p.PlayerAccountId,
            ["Level"] = p.PlayerLevel,
            ["Prestige"] = p.PlayerPrestige,
            ["Income"] = p.PlayerIncome,
            ["Gold"] = p.PlayerGold,
            ["Victories"] = p.PlayerVictories,
            ["OpponentName"] = p.OpponentName,
            ["OpponentHero"] = p.OpponentHero,
            ["OpponentRank"] = p.OpponentRank,
            ["OpponentAccountId"] = p.OpponentAccountId,
            ["OpponentRating"] = p.OpponentRating,
            ["OpponentLevel"] = p.OpponentLevel,
            ["OpponentPrestige"] = p.OpponentPrestige,
            ["OpponentVictories"] = p.OpponentVictories,
        };
    }

    private static void AssertOnlyNull(PvpBattleManifest manifest, string key)
    {
        foreach (var (name, value) in Scalars(manifest))
        {
            if (name == key)
                Assert.Null(value);
            else
                Assert.True(value != null, $"{name} was lost when {key} failed");
        }
    }
}
