#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.CombatSimulation;

/// <summary>
/// Turns one game replay into a corpus battle: the combat through the desktop host's
/// <see cref="DesktopCombatTimeline"/>, the entities through the catalog, and the spawn and
/// transform facts the inventory reads through the same facet maps. A game member Core has no
/// facet for is left out, as the producer leaves it out.
/// </summary>
internal static class CombatImpactCorpusExport
{
    internal static CombatImpactCorpusBattle ToBattle(
        GameReplay replay,
        CombatImpactCorpusCatalog catalog
    )
    {
        var timeline = DesktopCombatTimeline.From(replay.Combat);
        var entities = catalog.BuildEntities(replay.Spawn, replay.Combat, timeline);
        return new CombatImpactCorpusBattle
        {
            BattleId = replay.BattleId,
            Spawn = ToSpawn(replay.Spawn),
            Timeline = timeline,
            TransformIdentities = ReadTransformIdentities(replay.Combat),
            DeadPlayerUpdates = ReadDeadPlayerUpdates(replay.Combat),
            Entities = entities.Entities,
            ImplicitPlayerEffects = entities.ImplicitPlayerEffects,
        };
    }

    private static CombatImpactCorpusSpawn ToSpawn(GameSim spawn)
    {
        var result = new CombatImpactCorpusSpawn
        {
            PlayerAttributes = PlayerAttributes(spawn.Player.Attributes),
            OpponentAttributes = PlayerAttributes(spawn.Opponent.Attributes),
        };
        foreach (var (instanceId, card) in spawn.Cards)
        {
            result.Cards[instanceId] = new CombatImpactCorpusSpawnCard
            {
                Owner = DesktopCombatFacets.ToCombatant(card.Placement?.Owner),
                Attributes = DesktopCombatFacets.CardAttributes(
                    card.Attributes.Select(attribute =>
                        KeyValuePair.Create(attribute.Key, attribute.Value.Value)
                    )
                ),
            };
        }
        foreach (var candidate in spawn.Events)
        {
            switch (candidate)
            {
                case GameSimEventCardSpawned spawned:
                    result.SpawnedCards.Add(
                        Identity(
                            spawned.InstanceId,
                            spawned.TemplateId,
                            spawned.Type,
                            spawned.CombatantId
                        )
                    );
                    break;
                case GameSimEventEffectExecuted executed
                    when DesktopCombatFacets.ActionType.TryToFacet(
                        executed.ActionType,
                        out var action
                    ):
                    result.Effects.Add(
                        new CombatEffectExecuted
                        {
                            ExecutionContextId = executed.ExecutionContextId ?? string.Empty,
                            EffectId = executed.EffectId ?? string.Empty,
                            ActionType = action,
                            Source = executed.Source?.Value,
                            TriggerSource = executed.TriggerSource?.Value,
                            Target = DesktopCombatTimeline.ToTarget(executed.Target),
                        }
                    );
                    break;
            }
        }
        return result;
    }

    private static Dictionary<CombatPlayerAttribute, int> PlayerAttributes(
        IEnumerable<KeyValuePair<EPlayerAttributeType, int>> attributes
    )
    {
        var result = new Dictionary<CombatPlayerAttribute, int>();
        foreach (var (attribute, value) in attributes)
        {
            if (DesktopCombatFacets.PlayerAttribute.TryToFacet(attribute, out var facet))
                result[facet] = value;
        }
        return result;
    }

    private static List<CombatImpactCorpusTransformFrame> ReadTransformIdentities(CombatSim combat)
    {
        var result = new List<CombatImpactCorpusTransformFrame>();
        for (var frameIndex = 0; frameIndex < combat.Frames.Count; frameIndex++)
        {
            var events = combat.Frames[frameIndex].Events;
            var frame = new CombatImpactCorpusTransformFrame { FrameIndex = frameIndex };
            foreach (var transformed in events.OfType<CombatSimEventCardTransformed>())
                frame.Transformed.AddRange(transformed.TransformedCards.Select(Identity));
            foreach (var reverted in events.OfType<CombatSimEventCardTransformReverted>())
                frame.Reverted.Add(Identity(reverted.OriginalCard));
            if (frame.Transformed.Count > 0 || frame.Reverted.Count > 0)
                result.Add(frame);
        }
        return result;
    }

    private static List<CombatImpactCorpusPlayerFrame> ReadDeadPlayerUpdates(CombatSim combat)
    {
        var result = new List<CombatImpactCorpusPlayerFrame>();
        for (var frameIndex = 0; frameIndex < combat.Frames.Count; frameIndex++)
        {
            var frame = combat.Frames[frameIndex];
            if (frame.PlayerUpdates?.IsPlayerDead == true)
                result.Add(new CombatImpactCorpusPlayerFrame(frameIndex, Combatant.Player));
            if (frame.OpponentUpdates?.IsPlayerDead == true)
                result.Add(new CombatImpactCorpusPlayerFrame(frameIndex, Combatant.Opponent));
        }
        return result;
    }

    private static CombatImpactCorpusCardIdentity Identity(SimEventCardTransformation card) =>
        Identity(card.InstanceId, card.TemplateId, card.Type, card.CombatantId);

    private static CombatImpactCorpusCardIdentity Identity(
        string instanceId,
        string? templateId,
        ECardType type,
        ECombatantId combatant
    ) =>
        new(
            instanceId,
            templateId,
            DesktopCardFacets.Type.TryToFacet(type, out var facet) ? facet : null,
            DesktopCombatFacets.ToCombatant(combatant)
        );
}
