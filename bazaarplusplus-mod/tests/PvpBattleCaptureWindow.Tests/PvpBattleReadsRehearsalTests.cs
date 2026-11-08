using BazaarPlusPlus.Game.PvpBattles;
using Xunit;

namespace BazaarPlusPlus.Tests.PvpBattles;

public sealed class PvpBattleReadsRehearsalTests
{
    // Hand, skills, three texts and five numbers.
    private const int PlayerSteps = 10;

    [Fact]
    public void ThePlayersReadsRunOneAFrameOnceThePlayerIsReadable()
    {
        var h = new Harness();

        h.Tick();
        Assert.Empty(h.Reads);

        h.Readable = true;
        for (var i = 0; i < PlayerSteps; i++)
        {
            h.Tick();
            Assert.Equal(i + 1, h.Reads.Count);
        }
        h.Tick();

        Assert.Equal(
            [
                "player Hand",
                "player Skills",
                "player Hero",
                "player Name",
                "player AccountId",
                "player Level",
                "player Prestige",
                "player Income",
                "player Gold",
                "player Victories",
            ],
            h.Reads
        );
    }

    [Fact]
    public void AFrameThatSteppedOrCarriesABattleRunsNoRead()
    {
        var h = new Harness { Readable = true };

        h.Rehearsal.Tick();
        h.Rehearsal.Tick();
        h.Rehearsal.Offer(FakeMessage.Sim());
        Assert.Equal(["player Hand"], h.Reads);

        h.Frame++;
        h.Rehearsal.Defer();
        h.Rehearsal.Tick();
        h.Rehearsal.Offer(FakeMessage.Sim());
        Assert.Equal(["player Hand"], h.Reads);
    }

    [Fact]
    public void TheMessageReadsRunInsideTheOfferOfAGameSimOnly()
    {
        var h = new Harness { Readable = true };
        h.FinishPlayer();

        h.Offer(FakeMessage.Combat());
        h.Offer(FakeMessage.Other());
        Assert.Empty(h.Reads);

        for (var i = 0; i < 4; i++)
            h.Offer(FakeMessage.Sim());

        Assert.Equal(
            [
                "opening EncounterId",
                "opening OpponentName",
                "opening OpponentHero",
                "opening OpponentRank",
                "opening OpponentAccountId",
                "opening Day",
                "opening Hour",
                "opening OpponentRating",
                "opening OpponentLevel",
                "opening OpponentPrestige",
                "opening OpponentVictories",
                "opponent Skills",
                "serialize",
                "opponent Hand",
            ],
            h.Reads
        );
        Assert.Empty(h.Scripted.LateReads);
        Assert.True(h.Rehearsal.IsDone);
    }

    [Fact]
    public void NoReadRunsWhileThePlayerIsNotReadable()
    {
        var h = new Harness();

        h.Offer(FakeMessage.Sim());
        h.Tick();

        Assert.Empty(h.Reads);
    }

    [Fact]
    public void AHandWithoutACardTriesAgainUntilOneHoldsACard()
    {
        var h = new Harness(attempts: 2) { Readable = true };
        var empty = 3;
        h.Scripted.PlayerBoards[PvpSnapshotSection.Hand] = () =>
            empty-- > 0 ? [] : [ScriptedBattleReads.Card("hand-1")];

        for (var i = 0; i < 5; i++)
            h.Tick();

        Assert.Equal(
            ["player Hand", "player Hand", "player Hand", "player Hand", "player Skills"],
            h.Reads
        );
    }

    [Fact]
    public void AReadThatThrowsTriesAgainOnALaterFrame()
    {
        var h = new Harness { Readable = true };
        var throws = 1;
        h.Scripted.PlayerBoards[PvpSnapshotSection.Hand] = () =>
            throws-- > 0
                ? throw new InvalidOperationException("run still loading")
                : [ScriptedBattleReads.Card("hand-1")];

        h.Tick();
        h.Tick();
        h.Tick();

        Assert.Equal(["player Hand", "player Hand", "player Skills"], h.Reads);
    }

    [Fact]
    public void AStepThatAlwaysThrowsGivesUpAndTheRehearsalFinishes()
    {
        var h = new Harness(attempts: 2) { Readable = true };
        h.Scripted.PlayerBoards[PvpSnapshotSection.Hand] = () =>
            throw new InvalidOperationException("broken");
        h.Serialize = _ => throw new InvalidOperationException("broken");

        for (var i = 0; i < PlayerSteps + 1; i++)
            h.Tick();
        for (var i = 0; i < 4; i++)
            h.Offer(FakeMessage.Sim());
        Assert.False(h.Rehearsal.IsDone);
        h.Offer(FakeMessage.Sim());

        Assert.True(h.Rehearsal.IsDone);
        Assert.Equal(2, h.Reads.Count(read => read == "serialize"));
    }

    private sealed class Harness
    {
        public Harness(int attempts = PvpBattleReadsRehearsal<FakeMessage>.DefaultAttempts)
        {
            Rehearsal = new PvpBattleReadsRehearsal<FakeMessage>(
                new RecordingReads(Scripted, Reads),
                () => Readable,
                () => Frame,
                message =>
                {
                    Reads.Add("serialize");
                    Serialize(message);
                },
                attempts
            );
        }

        public ScriptedBattleReads Scripted { get; } = new();

        public List<string> Reads { get; } = [];

        public PvpBattleReadsRehearsal<FakeMessage> Rehearsal { get; }

        public bool Readable { get; set; }

        public int Frame { get; set; }

        public Action<FakeMessage> Serialize { get; set; } = _ => { };

        public void Tick()
        {
            Frame++;
            Rehearsal.Tick();
        }

        // The message expires once the call that delivered it returns, as an IL2CPP proxy does.
        public void Offer(FakeMessage message)
        {
            Frame++;
            Rehearsal.Offer(message);
            message.Expired = true;
        }

        public void FinishPlayer()
        {
            for (var i = 0; i < PlayerSteps; i++)
                Tick();
            Reads.Clear();
        }
    }

    // Records each read but the classification, which the rehearsal runs on every offer.
    private sealed class RecordingReads(ScriptedBattleReads inner, List<string> reads)
        : IPvpBattleReads<FakeMessage>
    {
        public PvpBattleSignal Classify(FakeMessage message) => inner.Classify(message);

        public string? OpeningText(FakeMessage opening, PvpOpeningText field)
        {
            reads.Add("opening " + field);
            return inner.OpeningText(opening, field);
        }

        public int? OpeningNumber(FakeMessage opening, PvpOpeningNumber field)
        {
            reads.Add("opening " + field);
            return inner.OpeningNumber(opening, field);
        }

        public IReadOnlyList<PvpBattleCardSnapshot> OpponentBoard(
            FakeMessage opening,
            PvpSnapshotSection section
        )
        {
            reads.Add("opponent " + section);
            return inner.OpponentBoard(opening, section);
        }

        public string? PlayerText(PvpPlayerText field)
        {
            reads.Add("player " + field);
            return inner.PlayerText(field);
        }

        public int? PlayerNumber(PvpPlayerNumber field)
        {
            reads.Add("player " + field);
            return inner.PlayerNumber(field);
        }

        public IReadOnlyList<PvpBattleCardSnapshot> PlayerBoard(PvpSnapshotSection section)
        {
            reads.Add("player " + section);
            return inner.PlayerBoard(section);
        }

        public PvpReplayPart KeepReplayPart(
            PvpReplayStage stage,
            FakeMessage message,
            string battleId
        ) => inner.KeepReplayPart(stage, message, battleId);
    }
}
