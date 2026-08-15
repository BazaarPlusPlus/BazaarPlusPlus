#nullable enable
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal readonly record struct CombatImpactTransformedCardSnapshot(
    SimEventCardTransformation Card,
    CombatSimCardUpdate? Update
);

internal static class CombatImpactTransformedCardSnapshotReader
{
    internal static IReadOnlyList<CombatImpactTransformedCardSnapshot> Read(CombatSim simulation)
    {
        var snapshots = new List<CombatImpactTransformedCardSnapshot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var frame in simulation.Frames)
        {
            foreach (var transformed in frame.Events.OfType<CombatSimEventCardTransformed>())
            {
                if (transformed.TransformedCards == null)
                    continue;
                foreach (var card in transformed.TransformedCards)
                    Add(frame, card, seen, snapshots);
            }

            foreach (var reverted in frame.Events.OfType<CombatSimEventCardTransformReverted>())
            {
                if (reverted.OriginalCard != null)
                    Add(frame, reverted.OriginalCard, seen, snapshots);
            }
        }

        return snapshots;
    }

    private static void Add(
        CombatSimFrame frame,
        SimEventCardTransformation? card,
        ISet<string> seen,
        ICollection<CombatImpactTransformedCardSnapshot> snapshots
    )
    {
        if (
            card == null
            || string.IsNullOrWhiteSpace(card.InstanceId)
            || !seen.Add(card.InstanceId)
        )
            return;

        frame.CardUpdates.TryGetValue(InstanceId.TryParse(card.InstanceId), out var update);
        snapshots.Add(new CombatImpactTransformedCardSnapshot(card, update));
    }
}
