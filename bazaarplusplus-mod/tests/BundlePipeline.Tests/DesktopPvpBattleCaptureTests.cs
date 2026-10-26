using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.TempoNet.Enums;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using BepInEx.Logging;
using MessagePack;

// The desktop's PvP Battle Capture Window: Core's window over the real DesktopPvpBattleReads and
// Mono net messages. The live player reads need the client's Data statics, which this lane does
// not have; they must degrade field by field, and their values are left to an in-game run.
internal static class DesktopPvpBattleCaptureTests
{
    internal static void Run()
    {
        OpeningFieldsComeFromTheOpeningMessage();
        ANullRunLeavesDayAndHourNullAndStillCaptures();
        TheOpponentBoardsComeFromTheOpeningEvents();
        AnEmptyOpponentNameLeavesTheOtherOpponentFields();
        AMonsterCombatCapturesWithoutAReplayAndStaysCurrentNative();
        ReplayPartsAreTheConcreteMessagesSerialized();
    }

    private static void OpeningFieldsComeFromTheOpeningMessage()
    {
        var capture = Capture(Opening(ERunState.PVPCombat));
        var manifest = capture.Manifest;

        Check.That(manifest.CombatKind == "PVPCombat", "CombatKind is the opening state's name.");
        Check.That(manifest.Day == 4 && manifest.Hour == 3, "Day and hour come from the opening.");
        Check.That(manifest.EncounterId == "enc-pvp", "The encounter comes from the opening.");
        Check.That(manifest.RunId == "run-desktop", "The run link is the run context's.");
        Check.That(manifest.Outcome.Result == "win", "A player winner is a win.");
        Check.That(
            manifest.Outcome.WinnerCombatantId == "Player"
                && manifest.Outcome.LoserCombatantId == "Opponent",
            "The combatant ids are the game's names."
        );
        var p = manifest.Participants;
        Check.That(
            p.OpponentName == "Rival"
                && p.OpponentRank == "Gold"
                && p.OpponentRating == 1500
                && p.OpponentLevel == 9
                && p.OpponentPrestige == 3
                && p.OpponentVictories == 4,
            "The opponent's facts come from the opening's PvP opponent."
        );
        Check.That(
            p.OpponentHero != null,
            "The opponent hero is the opening's hero, canonicalized."
        );
        Check.That(
            p.PlayerRank == "Diamond" && p.PlayerRating == 2100,
            "The player's rank comes from the run snapshot probe."
        );
    }

    private static void ANullRunLeavesDayAndHourNullAndStillCaptures()
    {
        var opening = Opening(ERunState.PVPCombat);
        opening.Data.Run = null!;

        var capture = Capture(opening);

        Check.That(
            capture.Manifest.Day == null && capture.Manifest.Hour == null,
            "An opening without a run has no day or hour."
        );
        Check.That(
            capture.Manifest.EncounterId == "enc-pvp" && capture.Replay != null,
            "An opening without a run still captures the battle and its replay."
        );
    }

    private static void TheOpponentBoardsComeFromTheOpeningEvents()
    {
        var opening = Opening(ERunState.PVPCombat);
        opening.Data.Events.Add(Spawned("opp-c", EContainerSocketId.Socket_6));
        opening.Data.Events.Add(Spawned("opp-a", EContainerSocketId.Socket_0));
        opening.Data.Events.Add(Spawned("opp-b", EContainerSocketId.Socket_3));
        opening.Data.Events.Add(
            new GameSimEventCardSpawned(
                "player-card",
                "template-player",
                ECardType.Item,
                ECombatantId.Player,
                EInventorySection.Hand,
                EContainerSocketId.Socket_1
            )
        );
        opening.Data.Events.Add(
            new GameSimEventPlayerSkillEquipped("opp-skill", ECombatantId.Opponent)
        );
        opening.Data.Events.Add(
            new GameSimEventPlayerSkillEquipped("player-skill", ECombatantId.Player)
        );

        var snapshots = Capture(opening).Manifest.Snapshots;

        Check.That(
            snapshots
                .OpponentHand.Items.Select(card => card.InstanceId)
                .SequenceEqual(["opp-a", "opp-b", "opp-c"]),
            "The opponent hand is the opening's opponent hand spawns, in socket order."
        );
        Check.That(
            snapshots.OpponentHand.Status == PvpBattleCaptureStatus.Captured
                && snapshots.OpponentHand.Source == PvpBattleCaptureSource.OpeningMessage,
            "The opponent hand is captured from the opening."
        );
        var skill = snapshots.OpponentSkills.Items.Single();
        Check.That(
            skill.InstanceId == "opp-skill" && skill.Type == CardType.Skill,
            "An opponent skill with no spawn falls back to the skill type."
        );
    }

    private static void AnEmptyOpponentNameLeavesTheOtherOpponentFields()
    {
        var opening = Opening(ERunState.PVPCombat);
        opening.Data.CurrentState!.PvpOpponent!.Name = "  ";

        var p = Capture(opening).Manifest.Participants;

        Check.That(p.OpponentName == null, "A blank opponent name is no name.");
        Check.That(
            p.OpponentRank == "Gold" && p.OpponentRating == 1500,
            "The other opponent fields are kept."
        );
    }

    private static void AMonsterCombatCapturesWithoutAReplayAndStaysCurrentNative()
    {
        var capture = Capture(Opening(ERunState.Combat));

        Check.That(capture.Manifest.CombatKind == "Combat", "A monster combat is Combat.");
        Check.That(capture.Replay == null, "A monster combat keeps no replay parts.");
        Check.That(
            CapturedReplayRouter.Resolve(capture.Manifest) == CapturedReplayRoute.CurrentNative,
            "A monster combat stays on the current-native route."
        );
    }

    private static void ReplayPartsAreTheConcreteMessagesSerialized()
    {
        var opening = Opening(ERunState.PVPCombat);
        var combat = Combat();
        var closing = Closing();
        var expectedSpawn = MessagePackSerializer.Serialize(opening, MessagePackConfig.Options);
        var expectedCombat = MessagePackSerializer.Serialize(combat, MessagePackConfig.Options);
        var expectedDespawn = MessagePackSerializer.Serialize(closing, MessagePackConfig.Options);
        var logLines = new List<string>();
        using var logSource = new ManualLogSource("desktop-pvp-capture");
        logSource.LogEvent += (_, args) => logLines.Add(args.Data?.ToString() ?? string.Empty);
        BppLog.Install(new BepInExLogSink(logSource));

        var capture = Capture(opening, combat, closing);

        BppLog.Flush();
        // Each deferred part logs its serialization once, at the close, in part order. Temporary
        // #514 probe: this sink and assertion go with the part_serialized event.
        var serialized = logLines
            .Where(line =>
                line.Contains(
                    "event=combat_replay.capture.part_serialized",
                    StringComparison.Ordinal
                )
            )
            .ToList();
        Check.That(
            serialized.Count == 3
                && serialized[0].Contains("part=spawn", StringComparison.Ordinal)
                && serialized[0].Contains($"bytes={expectedSpawn.Length}", StringComparison.Ordinal)
                && serialized[1].Contains("part=combat", StringComparison.Ordinal)
                && serialized[1]
                    .Contains($"bytes={expectedCombat.Length}", StringComparison.Ordinal)
                && serialized[2].Contains("part=despawn", StringComparison.Ordinal)
                && serialized[2]
                    .Contains($"bytes={expectedDespawn.Length}", StringComparison.Ordinal)
                && serialized.All(line => line.Contains("elapsed_ms=", StringComparison.Ordinal)),
            "Each replay part logs its bytes and elapsed time once, in part order:\n"
                + string.Join("\n", logLines)
        );

        var replay = capture.Replay;
        Check.That(replay != null, "A PvP combat keeps its replay.");
        Check.That(
            replay!.BattleId == capture.Manifest.BattleId && replay.Version == 1,
            "The replay is the battle's, version 1."
        );
        Check.That(
            replay.SpawnMessageBytes.SequenceEqual(expectedSpawn)
                && replay.CombatMessageBytes.SequenceEqual(expectedCombat)
                && replay.DespawnMessageBytes.SequenceEqual(expectedDespawn),
            "Each part is its message serialized as its concrete type."
        );
        var spawn = MessagePackSerializer.Deserialize<NetMessageGameSim>(
            replay.SpawnMessageBytes,
            MessagePackConfig.Options
        );
        var decodedCombat = MessagePackSerializer.Deserialize<NetMessageCombatSim>(
            replay.CombatMessageBytes,
            MessagePackConfig.Options
        );
        // CombatReplayLoader reads each part back as its concrete type, as here.
        Check.That(
            spawn.MessageId == "spawn" && decodedCombat.MessageId == "combat",
            "Each part decodes back as its concrete type."
        );
    }

    private static PvpBattleCapture Capture(
        NetMessageGameSim opening,
        NetMessageCombatSim? combat = null,
        NetMessageGameSim? closing = null
    )
    {
        var window = new PvpBattleCaptureWindow<INetMessage>(
            new DesktopPvpBattleReads(),
            new RunContextStore { CurrentServerRunId = "run-desktop" },
            new FixedRank(),
            () => PipelineInputs.Base,
            PvpOpeningKinds.PvpCombat | PvpOpeningKinds.MonsterCombat
        );
        Check.That(window.Observe(opening) == null, "An opening returns no capture.");
        Check.That(window.Observe(combat ?? Combat()) == null, "A combat sim returns no capture.");
        return window.Observe(closing ?? Closing())
            ?? throw new InvalidOperationException("The closing game sim must close the window.");
    }

    private static NetMessageGameSim Opening(ERunState state) =>
        new(
            new GameSim
            {
                Run = new SimUpdateRun
                {
                    Day = 4,
                    Hour = 3,
                    Victories = 2,
                },
                CurrentState = new SimUpdateRunState
                {
                    StateName = state,
                    CurrentEncounterId = "enc-pvp",
                    PvpOpponent = new SimPvpOpponent(
                        name: "Rival",
                        titlePrefix: null,
                        titleSuffix: null,
                        rank: ERank.Gold,
                        rating: 1500,
                        division: null,
                        victories: 4,
                        prestige: 3,
                        level: 9,
                        hero: EHero.Dooley,
                        playerLoadoutResponse: null,
                        playerCollection: null
                    ),
                },
            }
        )
        {
            MessageId = "spawn",
        };

    private static NetMessageCombatSim Combat() =>
        new(new CombatSim { Winner = ECombatantId.Player, Loser = ECombatantId.Opponent })
        {
            MessageId = "combat",
        };

    private static NetMessageGameSim Closing() =>
        new(new GameSim { CurrentState = new SimUpdateRunState { StateName = ERunState.Choice } })
        {
            MessageId = "despawn",
        };

    private static GameSimEventCardSpawned Spawned(string instanceId, EContainerSocketId socket) =>
        new(
            instanceId,
            "template-" + instanceId,
            ECardType.Item,
            ECombatantId.Opponent,
            EInventorySection.Hand,
            socket
        );

    private sealed class FixedRank : IRunSnapshotProbe
    {
        public bool TryGetRankSnapshot(out RankSnapshot rank)
        {
            rank = new RankSnapshot { Rank = "Diamond", Rating = 2100 };
            return true;
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
}
