using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace BazaarPlusPlus.Tests.PvpBattles;

/// <summary>
/// A net message as the scripted port sees it. It expires once the observe call that delivered it
/// returns, as an IL2CPP proxy does; a read of an expired message is recorded and fails the test.
/// </summary>
internal sealed class FakeMessage
{
    private FakeMessage(string name, PvpBattleSignal signal)
    {
        Name = name;
        Signal = signal;
    }

    public string Name { get; }

    public PvpBattleSignal Signal { get; }

    public bool Expired { get; set; }

    public Dictionary<PvpOpeningText, Func<string?>> Texts { get; } =
        new()
        {
            [PvpOpeningText.EncounterId] = () => "enc-pvp",
            [PvpOpeningText.OpponentName] = () => "Rival",
            [PvpOpeningText.OpponentHero] = () => "Dooley",
            [PvpOpeningText.OpponentRank] = () => "Gold",
            [PvpOpeningText.OpponentAccountId] = () => "acct-rival",
        };

    public Dictionary<PvpOpeningNumber, Func<int?>> Numbers { get; } =
        new()
        {
            [PvpOpeningNumber.Day] = () => 5,
            [PvpOpeningNumber.Hour] = () => 2,
            [PvpOpeningNumber.OpponentRating] = () => 1500,
            [PvpOpeningNumber.OpponentLevel] = () => 9,
            [PvpOpeningNumber.OpponentPrestige] = () => 3,
            [PvpOpeningNumber.OpponentVictories] = () => 4,
        };

    public Dictionary<
        PvpSnapshotSection,
        Func<IReadOnlyList<PvpBattleCardSnapshot>>
    > Boards { get; } =
        new()
        {
            [PvpSnapshotSection.Hand] = () =>
                [ScriptedBattleReads.Card("opp-1"), ScriptedBattleReads.Card("opp-2")],
            [PvpSnapshotSection.Skills] = () =>
                [ScriptedBattleReads.Card("opp-skill", CardType.Skill)],
        };

    public static FakeMessage Opening(string state = "PVPCombat") =>
        new("opening:" + state, PvpBattleSignal.GameSim(state));

    public static FakeMessage Sim(string state = "Choice") =>
        new("sim:" + state, PvpBattleSignal.GameSim(state));

    public static FakeMessage Combat(string? winner = "Player", string? loser = "Opponent") =>
        new("combat", PvpBattleSignal.CombatSim(winner, loser));

    public static FakeMessage Other() => new("other", PvpBattleSignal.Other);

    public override string ToString() => Name;
}

/// <summary>
/// The battle-reading port over scripted values: each read returns its value, null, or throws,
/// as its function says. Records the replay parts asked for and any read of an expired message.
/// </summary>
internal sealed class ScriptedBattleReads : IPvpBattleReads<FakeMessage>
{
    public Func<FakeMessage, PvpBattleSignal> Classifier { get; set; } = message => message.Signal;

    public Dictionary<PvpPlayerText, Func<string?>> PlayerTexts { get; } =
        new()
        {
            [PvpPlayerText.Hero] = () => "Vanessa",
            [PvpPlayerText.Name] = () => "PlayerOne",
            [PvpPlayerText.AccountId] = () => "acct-player",
        };

    public Dictionary<PvpPlayerNumber, Func<int?>> PlayerNumbers { get; } =
        new()
        {
            [PvpPlayerNumber.Level] = () => 10,
            [PvpPlayerNumber.Prestige] = () => 2,
            [PvpPlayerNumber.Income] = () => 7,
            [PvpPlayerNumber.Gold] = () => 12,
            [PvpPlayerNumber.Victories] = () => 6,
        };

    public Dictionary<
        PvpSnapshotSection,
        Func<IReadOnlyList<PvpBattleCardSnapshot>>
    > PlayerBoards { get; } =
        new()
        {
            [PvpSnapshotSection.Hand] = () => [Card("hand-1"), Card("hand-2")],
            [PvpSnapshotSection.Skills] = () => [Card("skill-1", CardType.Skill)],
        };

    /// <summary>The part for each stage; by default the stage's message name, serialized.</summary>
    public Func<PvpReplayStage, FakeMessage, string, PvpReplayPart> Part { get; set; } =
        (_, message, _) => PvpReplayPart.Serialized(Bytes(message.Name));

    public List<(PvpReplayStage Stage, string Message, string BattleId)> PartsAsked { get; } = [];

    public Dictionary<PvpSnapshotSection, int> PlayerBoardReads { get; } =
        new() { [PvpSnapshotSection.Hand] = 0, [PvpSnapshotSection.Skills] = 0 };

    public List<string> LateReads { get; } = [];

    public PvpBattleSignal Classify(FakeMessage message)
    {
        Touch(message, "classify");
        return Classifier(message);
    }

    public string? OpeningText(FakeMessage opening, PvpOpeningText field)
    {
        Touch(opening, field.ToString());
        return opening.Texts[field]();
    }

    public int? OpeningNumber(FakeMessage opening, PvpOpeningNumber field)
    {
        Touch(opening, field.ToString());
        return opening.Numbers[field]();
    }

    public IReadOnlyList<PvpBattleCardSnapshot> OpponentBoard(
        FakeMessage opening,
        PvpSnapshotSection section
    )
    {
        Touch(opening, "opponent " + section);
        return opening.Boards[section]();
    }

    public string? PlayerText(PvpPlayerText field) => PlayerTexts[field]();

    public int? PlayerNumber(PvpPlayerNumber field) => PlayerNumbers[field]();

    public IReadOnlyList<PvpBattleCardSnapshot> PlayerBoard(PvpSnapshotSection section)
    {
        PlayerBoardReads[section]++;
        return PlayerBoards[section]();
    }

    public PvpReplayPart KeepReplayPart(PvpReplayStage stage, FakeMessage message, string battleId)
    {
        Touch(message, "part " + stage);
        PartsAsked.Add((stage, message.Name, battleId));
        return Part(stage, message, battleId);
    }

    public static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    public static PvpBattleCardSnapshot Card(string instanceId, CardType type = CardType.Item) =>
        new()
        {
            InstanceId = instanceId,
            TemplateId = "template-" + instanceId,
            Type = type,
            Size = CardSize.Medium,
            Section = CardSection.Hand,
            Socket = CardSocket.Socket_0,
            Name = "Card " + instanceId,
            Tier = "Gold",
            Tags = ["Weapon"],
            Attributes = new Dictionary<string, int> { ["DamageAmount"] = 10 },
        };

    private void Touch(FakeMessage message, string read)
    {
        if (message.Expired)
            LateReads.Add($"{read} of {message.Name}");
    }
}

internal sealed class ScriptedRunSnapshot : IRunSnapshotProbe
{
    public Func<RankSnapshot?> Rank { get; set; } =
        () => new RankSnapshot { Rank = "Diamond", Rating = 2100 };

    public bool TryGetRankSnapshot(out RankSnapshot rank)
    {
        var value = Rank();
        rank = value!;
        return value != null;
    }

    public bool TryGetRunBasics(out RunBasicsSnapshot basics)
    {
        basics = null!;
        return false;
    }

    public bool TryGetPlayerStats(out PlayerStatsSnapshot stats)
    {
        stats = null!;
        return false;
    }

    public bool TryGetLeaderboardPosition(out int? position)
    {
        position = null;
        return false;
    }
}

/// <summary>One window over the scripted port, a run context, a rank probe and a fixed clock.</summary>
internal sealed class WindowHarness : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public WindowHarness(
        PvpOpeningKinds openingKinds = PvpOpeningKinds.PvpCombat | PvpOpeningKinds.MonsterCombat
    )
    {
        Run.CurrentServerRunId = "run-1";
        Window = new PvpBattleCaptureWindow<FakeMessage>(Reads, Run, Rank, () => Now, openingKinds);
        BppLog.Install(new ListLogSink(Log));
    }

    public ScriptedBattleReads Reads { get; } = new();

    public RunContextStore Run { get; } = new();

    public ScriptedRunSnapshot Rank { get; } = new();

    public PvpBattleCaptureWindow<FakeMessage> Window { get; }

    public List<string> Log { get; } = [];

    public PvpBattleCapture? Feed(FakeMessage message)
    {
        try
        {
            return Window.Observe(message);
        }
        finally
        {
            message.Expired = true;
        }
    }

    /// <summary>Feeds each message in turn and returns the one capture they close.</summary>
    public PvpBattleCapture Battle(
        FakeMessage? opening = null,
        FakeMessage? combat = null,
        FakeMessage? closing = null
    )
    {
        Assert.Null(Feed(opening ?? FakeMessage.Opening()));
        Assert.Null(Feed(combat ?? FakeMessage.Combat()));
        return Assert.IsType<PvpBattleCapture>(Feed(closing ?? FakeMessage.Sim()));
    }

    public IReadOnlyList<string> LogLines(string eventId) =>
        Log.Where(line => line.Contains(eventId, StringComparison.Ordinal)).ToList();

    public static string Summary(PvpBattleCapture capture, string label) =>
        Assert.Single(capture.BoardSummary, field => field.Name == label).Value?.ToString()
        ?? "null";

    public void Dispose() => Assert.Empty(Reads.LateReads);

    private sealed class ListLogSink(List<string> lines) : IBppLogSink
    {
        public void Write(BppLogSeverity severity, string message) =>
            lines.Add($"{severity} {message}");
    }
}
