using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Domain.Players;
using Il2CppBazaarGameShared.Infra.Messages;
using Il2CppBazaarGameShared.Infra.Messages.GameSimEvents;
using Il2CppTheBazaar;
using GamePlayer = Il2CppBazaarGameClient.Domain.Models.Player;

namespace BazaarPlusPlus.Android.CombatReplay.Bootstrap;

/// <summary>
/// The saved state a replay restores before it plays, the Android forms of the desktop's
/// <c>ReplaySavedStateNormalizer</c>, <c>SnapshotRehydrator</c> and the opening events of
/// <c>ReplayOpeningStateRestorer</c>: the messages' day, hour and levels from the manifest (Core's
/// <see cref="ReplaySavedStateRules"/>), the captured cards and skills onto the game's card
/// models, the opponent's off-hand spawns removed, and the opening socket and player events
/// processed. Card facets map back through <see cref="AndroidCardFacets"/> (ADR-0015). Main
/// thread only. The desktop's socket effect restoration is not ported yet.
/// </summary>
internal static class AndroidReplaySavedState
{
    public static void Normalize(PvpBattleManifest? manifest, CombatSequenceMessages? sequence)
    {
        NormalizeMessage(sequence?.SpawnMessage, manifest);
        NormalizeMessage(sequence?.DespawnMessage, manifest);
    }

    public static void RehydratePlayerCards(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var capture = manifest.Snapshots.PlayerHand;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.PlayerSnapshotUnavailable);
            return;
        }
        RehydrateCards(capture.Items, spawnMessage, Data.Run?.Player);
    }

    public static void RehydrateOpponentCards(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var capture = manifest.Snapshots.OpponentHand;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.OpponentSnapshotUnavailable);
            return;
        }
        RehydrateCards(capture.Items, spawnMessage, Data.Run?.Opponent);
    }

    public static void RehydratePlayerSkills(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var capture = manifest.Snapshots.PlayerSkills;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.PlayerSkillsUnavailable);
            return;
        }
        ReplaceSkills(
            Data.Run?.Player,
            RehydrateCards(capture.Items, spawnMessage, Data.Run?.Player)
        );
    }

    public static void RehydrateOpponentSkills(
        PvpBattleManifest manifest,
        NetMessageGameSim spawnMessage,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var capture = manifest.Snapshots.OpponentSkills;
        if (capture.Status == PvpBattleCaptureStatus.Missing)
        {
            outcome.ReportDegradation(ReplayPlaybackReasonCode.OpponentSkillsUnavailable);
            return;
        }
        ReplaceSkills(
            Data.Run?.Opponent,
            RehydrateCards(capture.Items, spawnMessage, Data.Run?.Opponent)
        );
    }

    // The opponent's cards outside the hand are not part of the fight the replay shows.
    public static void SanitizeSpawnEvents(CombatSequenceMessages sequence, string? battleId)
    {
        var events = sequence.SpawnMessage?.Data?.Events;
        if (events == null || events.Count == 0)
            return;
        var removed = 0;
        for (var index = events.Count - 1; index >= 0; index--)
        {
            var spawned = events[index]?.TryCast<GameSimEventCardSpawned>();
            if (
                spawned == null
                || spawned.CombatantId != ECombatantId.Opponent
                || spawned.Type == ECardType.SocketEffect
                || AndroidGameIds.Unboxed(() => spawned.Section) == EInventorySection.Hand
            )
                continue;
            events.RemoveAt(index);
            removed++;
        }
        if (removed > 0)
            BppLog.DebugEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.playback.cleanup_observed"
                ),
                () =>
                    [
                        ("stage", "spawn_sanitization"),
                        ("removed_count", removed),
                        ("battle_id", battleId, BppLogCorrelationPolicy.Short),
                    ]
            );
    }

    /// <summary>
    /// Processes the spawn's opening socket unlocks and player initializations, which the
    /// replay's direct injection skips; a failure is a degradation.
    /// </summary>
    public static async Task RestoreOpeningEventsAsync(
        GameSimHandler gameSimHandler,
        NetMessageGameSim spawnMessage,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        var events = spawnMessage.Data?.Events;
        if (events == null)
            return;
        var opening = new Il2CppSystem.Collections.Generic.List<IGameSimEvent>();
        foreach (var gameEvent in events)
        {
            if (gameEvent == null)
                continue;
            if (gameEvent.TryCast<GameSimEventSocketsUnlocked>() != null)
            {
                opening.Add(gameEvent);
                continue;
            }
            var initialized = gameEvent.TryCast<GameSimEventPlayerInitialized>();
            if (
                initialized != null
                && initialized.CombatantId is ECombatantId.Player or ECombatantId.Opponent
            )
                opening.Add(gameEvent);
        }
        if (opening.Count == 0)
            return;
        try
        {
            await Il2CppTasks.AwaitAsync(
                gameSimHandler.ProcessEvents(
                    opening.Cast<Il2CppSystem.Collections.Generic.IList<IGameSimEvent>>()
                ),
                "GameSimHandler.ProcessEvents"
            );
        }
        catch (Exception exception)
        {
            outcome.ReportDegradation(
                ReplayPlaybackReasonCode.PresentationWarmupFailed,
                new InvalidOperationException(
                    "opening_state_restore: selective opening events could not be processed.",
                    exception
                )
            );
        }
    }

    private static void NormalizeMessage(NetMessageGameSim? message, PvpBattleManifest? manifest)
    {
        var data = message?.Data;
        if (data == null)
            return;
        data.Run ??= new SimUpdateRun();
        data.Run.Day = ReplaySavedStateRules.ResolvePositiveUInt(data.Run.Day, manifest?.Day);
        data.Run.Hour = ReplaySavedStateRules.ResolvePositiveUInt(data.Run.Hour, manifest?.Hour);
        data.Player ??= new SimUpdatePlayer { CombatantId = ECombatantId.Player };
        NormalizeLevel(data.Player, manifest?.Participants?.PlayerLevel);
        data.Opponent ??= new SimUpdatePlayer { CombatantId = ECombatantId.Opponent };
        NormalizeLevel(data.Opponent, manifest?.Participants?.OpponentLevel);
    }

    private static void NormalizeLevel(SimUpdatePlayer player, int? manifestLevel)
    {
        player.Attributes ??= new Il2CppSystem.Collections.Generic.Dictionary<
            EPlayerAttributeType,
            int
        >();
        int? raw = player.Attributes.TryGetValue(EPlayerAttributeType.Level, out var level)
            ? level
            : null;
        player.Attributes[EPlayerAttributeType.Level] = ReplaySavedStateRules.ResolveLevel(
            raw,
            manifestLevel
        );
    }

    private static List<Card> RehydrateCards(
        IEnumerable<PvpBattleCardSnapshot> snapshots,
        NetMessageGameSim spawnMessage,
        GamePlayer? owner
    )
    {
        var cards = new List<Card>();
        var updates = spawnMessage.Data?.Cards;
        var ownerProxy = owner?.TryCast<IPlayer>();
        foreach (var snapshot in snapshots)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.InstanceId))
                continue;
            var card = Data.GetOrCreateCard(
                snapshot.InstanceId,
                snapshot.TemplateId,
                AndroidCardFacets.GameType(snapshot.Type)
            );
            if (card == null)
                continue;
            if (updates != null && updates.TryGetValue(snapshot.InstanceId, out var update))
                DataExtensions.Update(card, update, false);
            ApplySnapshotFallback(card, snapshot);
            card.Size = AndroidCardFacets.GameSize(snapshot.Size);
            card.Owner = ownerProxy;
            card.Section = AndroidGameIds.Optional(AndroidCardFacets.GameSection(snapshot.Section));
            card.LeftSocketId = AndroidGameIds.Optional(
                AndroidCardFacets.GameSocket(snapshot.Socket)
            );
            cards.Add(card);
        }
        return cards;
    }

    private static void ReplaceSkills(GamePlayer? owner, IReadOnlyList<Card> cards)
    {
        var skills = owner?.Skills;
        if (skills == null)
            return;
        skills.Clear();
        foreach (var card in cards)
        {
            var skill = card.TryCast<SkillCard>();
            if (skill != null)
                skills.Add(skill);
        }
    }

    private static void ApplySnapshotFallback(Card card, PvpBattleCardSnapshot snapshot)
    {
        if (snapshot.Attributes is { Count: > 0 } attributes && card.Attributes != null)
        {
            foreach (var entry in attributes)
            {
                if (Enum.TryParse<ECardAttributeType>(entry.Key, false, out var attribute))
                    card.Attributes[attribute] = entry.Value;
            }
        }
        if (snapshot.Tags is { Count: > 0 } tags)
        {
            var parsed = new Il2CppSystem.Collections.Generic.HashSet<ECardTag>();
            foreach (var tag in tags)
            {
                if (Enum.TryParse<ECardTag>(tag, false, out var value))
                    parsed.Add(value);
            }
            card.Tags = parsed;
        }
        if (
            !string.IsNullOrWhiteSpace(snapshot.Tier)
            && Enum.TryParse<ETier>(snapshot.Tier, false, out var tier)
        )
            card.Tier = tier;
        var item = card.TryCast<ItemCard>();
        if (
            item != null
            && !string.IsNullOrWhiteSpace(snapshot.Enchant)
            && Enum.TryParse<EEnchantmentType>(snapshot.Enchant, false, out var enchantment)
        )
            item.Enchantment = new Il2CppSystem.Nullable<EEnchantmentType>(enchantment);
    }
}
