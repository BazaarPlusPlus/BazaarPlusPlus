#nullable enable

using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Game.PvpBattles;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatReplay.Bootstrap;

/// <summary>
/// Restores the small part of the native PVP opening presentation that saved replay bootstrap
/// intentionally bypasses. Recorded spawn events take precedence, with captured hand snapshots
/// supplying socket effects that were absent from the message; no state is inferred from the hero.
/// </summary>
internal static class ReplayOpeningStateRestorer
{
    private const string OpeningStateRestoreContext = "opening_state_restore";
    private const string SocketEffectRestoreContext = "socket_effect_restore";

    private static readonly Dictionary<string, Card> TrackedSocketCards = new(
        StringComparer.Ordinal
    );
    private static readonly Dictionary<string, SocketEffectController> TrackedSocketControllers =
        new(StringComparer.Ordinal);

    private static string? _trackedBattleId;

    internal static IReadOnlyList<IGameSimEvent> SelectOpeningEvents(
        IEnumerable<IGameSimEvent>? events
    )
    {
        if (events == null)
            return Array.Empty<IGameSimEvent>();

        return events
            .Where(gameEvent =>
                gameEvent is GameSimEventSocketsUnlocked
                || gameEvent
                    is GameSimEventPlayerInitialized
                    {
                        CombatantId: ECombatantId.Player or ECombatantId.Opponent,
                    }
            )
            .ToList();
    }

    internal static IReadOnlyList<GameSimEventCardSpawned> SelectSocketEffectSpawnEvents(
        IEnumerable<IGameSimEvent>? events
    )
    {
        if (events == null)
            return Array.Empty<GameSimEventCardSpawned>();

        var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        return events
            .OfType<GameSimEventCardSpawned>()
            .Where(spawnEvent =>
                spawnEvent.Type == ECardType.SocketEffect
                && spawnEvent.CombatantId is ECombatantId.Player or ECombatantId.Opponent
                && instanceIds.Add(spawnEvent.InstanceId)
            )
            .ToList();
    }

    internal static IReadOnlyList<PvpBattleCardSnapshot> SelectSocketEffectSnapshots(
        PvpBattleManifest? manifest
    )
    {
        if (manifest?.Snapshots == null)
            return Array.Empty<PvpBattleCardSnapshot>();

        return EnumerateHandSnapshots(manifest)
            .Where(snapshot =>
                snapshot != null
                && snapshot.Type == ECardType.SocketEffect
                && !string.IsNullOrWhiteSpace(snapshot.InstanceId)
            )
            .GroupBy(snapshot => snapshot.InstanceId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    internal static async Task RestoreBeforeReplayAsync(
        GameSimHandler gameSimHandler,
        NetMessageGameSim spawnMessage,
        PvpBattleManifest manifest,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        if (gameSimHandler == null)
            throw new ArgumentNullException(nameof(gameSimHandler));
        if (spawnMessage == null)
            throw new ArgumentNullException(nameof(spawnMessage));
        if (outcome == null)
            throw new ArgumentNullException(nameof(outcome));

        PrepareTracking(outcome.BattleId);

        var spawnEvents = spawnMessage.Data?.Events;
        var openingEvents = SelectOpeningEvents(spawnEvents);
        if (openingEvents.Count > 0)
        {
            try
            {
                await gameSimHandler.ProcessEvents(openingEvents.ToList());
            }
            catch (Exception ex)
            {
                ReportDegradation(
                    outcome,
                    OpeningStateRestoreContext,
                    "selective opening events could not be processed",
                    ex
                );
            }
        }

        var socketSpawnEvents = SelectSocketEffectSpawnEvents(spawnEvents);
        var snapshotSocketCards = ResolveSnapshotSocketCards(SelectSocketEffectSnapshots(manifest));
        if (socketSpawnEvents.Count == 0 && snapshotSocketCards.Count == 0)
            return;

        try
        {
            await RestoreSocketEffectsAsync(
                gameSimHandler,
                spawnMessage,
                socketSpawnEvents,
                snapshotSocketCards,
                outcome
            );
        }
        catch (Exception ex)
        {
            ReportDegradation(
                outcome,
                SocketEffectRestoreContext,
                "recorded socket effects could not be restored",
                ex
            );
        }
    }

    internal static void FinalizeAfterWarmup(IReplayPlaybackOutcomeSink outcome)
    {
        if (outcome == null)
            throw new ArgumentNullException(nameof(outcome));

        List<Exception>? failures = null;
        foreach (var entry in TrackedSocketControllers.ToList())
        {
            var controller = ResolveTrackedController(entry.Key, entry.Value);
            if (controller == null)
                continue;

            try
            {
                controller.gameObject.SetActive(true);
                controller.ShowCard(show: true);
                controller.MoveToSocket();
                controller.CheckInitialSocketState();
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }
        }

        if (failures is { Count: > 0 })
        {
            ReportDegradation(
                outcome,
                SocketEffectRestoreContext,
                "restored socket effects could not be finalized after warmup",
                failures.Count == 1 ? failures[0] : new AggregateException(failures)
            );
        }
    }

    internal static void Cleanup()
    {
        try
        {
            var cards = TrackedSocketCards.Values.Distinct().ToList();
            TrackedSocketCards.Clear();
            TrackedSocketControllers.Clear();
            _trackedBattleId = null;

            RemoveControllersWithNativePath(cards);
            RemoveTrackedModels(cards);
        }
        catch
        {
            // Replay cleanup also runs while scenes and static game lookups are being destroyed.
        }
    }

    private static async Task RestoreSocketEffectsAsync(
        GameSimHandler gameSimHandler,
        NetMessageGameSim spawnMessage,
        IReadOnlyList<GameSimEventCardSpawned> socketSpawnEvents,
        IReadOnlyList<Card> snapshotSocketCards,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        List<Exception>? failures = null;
        var eventsWithoutControllers = new List<GameSimEventCardSpawned>();
        foreach (var spawnEvent in socketSpawnEvents)
        {
            Card? existingCard;
            try
            {
                existingCard = PreferRawSocketIdentity(spawnEvent);
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
                existingCard = TryGetCard(spawnEvent.InstanceId);
            }

            var existingController = TryGetSocketController(existingCard);
            if (existingCard != null && existingController != null)
                continue;

            eventsWithoutControllers.Add(spawnEvent);
        }

        var cards =
            eventsWithoutControllers.Count == 0
                ? new List<Card>()
                : gameSimHandler.ProcessCardSpawnedEvents(
                    eventsWithoutControllers,
                    spawnMessage.Data.Cards,
                    triggerEvents: false
                );
        var candidateIds = cards
            .Select(card => card.InstanceId.Value)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var snapshotCard in snapshotSocketCards)
        {
            if (candidateIds.Add(snapshotCard.InstanceId.Value))
            {
                try
                {
                    snapshotCard.TryAddToContainer(force: true);
                    cards.Add(snapshotCard);
                }
                catch (Exception ex)
                {
                    (failures ??= new List<Exception>()).Add(ex);
                }
            }
        }

        if (cards.Count == 0)
        {
            ReportSocketFailures(outcome, failures);
            return;
        }

        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager == null)
            throw new InvalidOperationException("BoardManager is unavailable.");

        foreach (var card in cards)
        {
            if (card == null || card.Type != ECardType.SocketEffect)
                continue;

            var instanceId = card.InstanceId.Value;
            var existingController = TryGetSocketController(card);
            if (existingController != null)
                continue;

            TrackedSocketCards[instanceId] = card;
            try
            {
                var controller = await boardManager.SpawnCardInstantly(card);
                if (controller is not SocketEffectController socketController)
                {
                    throw new InvalidOperationException(
                        $"Socket effect {instanceId} did not create a SocketEffectController."
                    );
                }

                socketController.gameObject.SetActive(true);
                socketController.ShowCard(show: false);
                TrackedSocketControllers[instanceId] = socketController;
            }
            catch (Exception ex)
            {
                (failures ??= new List<Exception>()).Add(ex);
            }
        }

        ReportSocketFailures(outcome, failures);
    }

    private static void PrepareTracking(string battleId)
    {
        if (
            _trackedBattleId != null
            && !string.Equals(_trackedBattleId, battleId, StringComparison.Ordinal)
        )
        {
            Cleanup();
        }

        _trackedBattleId = battleId;
    }

    private static IEnumerable<PvpBattleCardSnapshot> EnumerateHandSnapshots(
        PvpBattleManifest manifest
    )
    {
        if (manifest.Snapshots.PlayerHand?.Items != null)
        {
            foreach (var snapshot in manifest.Snapshots.PlayerHand.Items)
                yield return snapshot;
        }

        if (manifest.Snapshots.OpponentHand?.Items != null)
        {
            foreach (var snapshot in manifest.Snapshots.OpponentHand.Items)
                yield return snapshot;
        }
    }

    private static IReadOnlyList<Card> ResolveSnapshotSocketCards(
        IEnumerable<PvpBattleCardSnapshot> snapshots
    )
    {
        var cards = new List<Card>();
        foreach (var snapshot in snapshots)
        {
            var card = TryGetCard(snapshot.InstanceId);
            if (
                card != null
                && card.Type == ECardType.SocketEffect
                && TryGetSocketController(card) == null
            )
                cards.Add(card);
        }

        return cards;
    }

    private static Card? PreferRawSocketIdentity(GameSimEventCardSpawned spawnEvent)
    {
        var existingCard = TryGetCard(spawnEvent.InstanceId);
        if (existingCard == null)
            return null;

        if (
            existingCard.Type == spawnEvent.Type
            && Guid.TryParse(spawnEvent.TemplateId, out var rawTemplateId)
            && rawTemplateId != Guid.Empty
            && existingCard.TemplateId == rawTemplateId
        )
        {
            return existingCard;
        }

        if (
            !Guid.TryParse(spawnEvent.TemplateId, out rawTemplateId)
            || rawTemplateId == Guid.Empty
            || TryGetSocketController(existingCard) != null
        )
        {
            return existingCard;
        }

        var rawCard = DTOUtils.CreateCard(
            spawnEvent.InstanceId,
            spawnEvent.TemplateId,
            spawnEvent.Type
        );
        if (existingCard.GetType() == rawCard.GetType())
        {
            existingCard.TemplateId = rawCard.TemplateId;
            existingCard.Template = rawCard.Template;
            existingCard.Type = rawCard.Type;
            return existingCard;
        }

        existingCard.TryRemoveFromContainer();
        Data.Entities[rawCard.InstanceId] = rawCard;
        return rawCard;
    }

    private static Card? TryGetCard(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return null;

        try
        {
            return Data.GetCard(instanceId);
        }
        catch
        {
            return null;
        }
    }

    private static SocketEffectController? TryGetSocketController(Card? card)
    {
        if (card == null)
            return null;

        try
        {
            return Data.CardAndSkillLookup?.GetCardController(card) as SocketEffectController;
        }
        catch
        {
            return null;
        }
    }

    private static SocketEffectController? ResolveTrackedController(
        string instanceId,
        SocketEffectController trackedController
    )
    {
        var card = TrackedSocketCards.GetValueOrDefault(instanceId);
        var currentController = TryGetSocketController(card);
        if (currentController != null)
            return currentController;

        try
        {
            return trackedController != null ? trackedController : null;
        }
        catch
        {
            return null;
        }
    }

    private static void RemoveControllersWithNativePath(IReadOnlyList<Card> cards)
    {
        BoardManager? boardManager;
        try
        {
            boardManager = Singleton<BoardManager>.Instance;
        }
        catch
        {
            return;
        }

        if (boardManager == null)
            return;

        foreach (var card in cards)
        {
            try
            {
                boardManager.RemoveCardsInstantly(new List<Card> { card });
            }
            catch
            {
                // Continue cleaning the remaining cards if a lookup died during scene teardown.
            }
        }
    }

    private static void RemoveTrackedModels(IEnumerable<Card> cards)
    {
        foreach (var card in cards)
        {
            try
            {
                card.TryRemoveFromContainer();
            }
            catch
            {
                // The owning run or container may already have been reset.
            }

            try
            {
                Data.Entities?.Remove(card.InstanceId);
            }
            catch
            {
                // Data may already have been torn down with the replay scene.
            }
        }
    }

    private static void ReportDegradation(
        IReplayPlaybackOutcomeSink outcome,
        string context,
        string message,
        Exception exception
    )
    {
        outcome.ReportDegradation(
            ReplayPlaybackReasonCode.PresentationWarmupFailed,
            new InvalidOperationException($"{context}: {message}.", exception)
        );
    }

    private static void ReportSocketFailures(
        IReplayPlaybackOutcomeSink outcome,
        List<Exception>? failures
    )
    {
        if (failures is not { Count: > 0 })
            return;

        ReportDegradation(
            outcome,
            SocketEffectRestoreContext,
            "one or more recorded socket effects could not be restored",
            failures.Count == 1 ? failures[0] : new AggregateException(failures)
        );
    }
}
