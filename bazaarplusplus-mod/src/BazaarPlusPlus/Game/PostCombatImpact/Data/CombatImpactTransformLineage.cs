#nullable enable
using BazaarGameShared.Infra.Messages.CombatSimEvents;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal sealed class CombatImpactTransformLineage
{
    private readonly IReadOnlyDictionary<string, string> _parents;

    private CombatImpactTransformLineage(IReadOnlyDictionary<string, string> parents)
    {
        _parents = parents;
    }

    internal static CombatImpactTransformLineage Build(CombatSim simulation)
    {
        var parents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var frame in simulation.Frames)
        {
            foreach (var transformed in frame.Events.OfType<CombatSimEventCardTransformed>())
            {
                if (transformed.TransformedCards == null)
                    continue;
                foreach (var card in transformed.TransformedCards)
                {
                    if (card == null)
                        continue;
                    AddParent(parents, card.InstanceId, transformed.OriginalInstanceId);
                }
            }

            foreach (var reverted in frame.Events.OfType<CombatSimEventCardTransformReverted>())
            {
                if (reverted.OriginalCard == null || reverted.TransformedCardInstanceIds == null)
                    continue;
                foreach (var transformedId in reverted.TransformedCardInstanceIds)
                    AddParent(parents, transformedId, reverted.OriginalCard.InstanceId);
            }
        }

        return new CombatImpactTransformLineage(parents);
    }

    internal string ResolveKnown(
        string instanceId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var current = instanceId;
        string? highestKnown = entities.ContainsKey(current) ? current : null;
        var path = new List<string>();
        var pathIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        while (_parents.TryGetValue(current, out var parent))
        {
            if (pathIndexes.TryGetValue(current, out var cycleStart))
            {
                return path.Skip(cycleStart)
                        .Where(entities.ContainsKey)
                        .OrderBy(item => item, StringComparer.Ordinal)
                        .FirstOrDefault()
                    ?? highestKnown
                    ?? instanceId;
            }
            pathIndexes[current] = path.Count;
            path.Add(current);
            current = parent;
            if (entities.ContainsKey(current))
                highestKnown = current;
        }

        return highestKnown ?? instanceId;
    }

    internal string? ResolveOptional(
        string? instanceId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) => instanceId == null ? null : ResolveKnown(instanceId, entities);

    private static void AddParent(IDictionary<string, string> parents, string child, string parent)
    {
        if (
            string.IsNullOrWhiteSpace(child)
            || string.IsNullOrWhiteSpace(parent)
            || string.Equals(child, parent, StringComparison.Ordinal)
        )
            return;

        parents.TryAdd(child, parent);
    }
}
