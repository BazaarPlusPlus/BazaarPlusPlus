#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.Encounters;

internal readonly struct TierProbability
{
    public TierProbability(ETier tier, double percent)
    {
        Tier = tier;
        Percent = percent;
    }

    public ETier Tier { get; }

    public double Percent { get; }
}

// Normalized view of the positive weights consumed by the game's TierTable.Roll.
// GameData weights are not required to add up to one (Day 12 currently totals 0.8),
// so tooltip percentages must be calculated from their positive-weight sum.
internal sealed class TierDistribution
{
    private TierDistribution(IReadOnlyList<TierProbability> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<TierProbability> Entries { get; }

    public static TierDistribution? FromWeights(
        float bronze,
        float silver,
        float gold,
        float diamond
    )
    {
        var weights = new[]
        {
            new TierWeight(ETier.Bronze, bronze),
            new TierWeight(ETier.Silver, silver),
            new TierWeight(ETier.Gold, gold),
            new TierWeight(ETier.Diamond, diamond),
        };

        double total = 0;
        foreach (var entry in weights)
            if (IsUsable(entry.Weight))
                total += entry.Weight;

        if (total <= 0 || double.IsNaN(total) || double.IsInfinity(total))
            return null;

        var normalized = new List<TierProbability>(weights.Length);
        foreach (var entry in weights)
        {
            if (!IsUsable(entry.Weight))
                continue;
            normalized.Add(new TierProbability(entry.Tier, entry.Weight / total * 100d));
        }

        return normalized.Count == 0 ? null : new TierDistribution(normalized.ToArray());
    }

    private static bool IsUsable(float weight) =>
        weight > 0 && !float.IsNaN(weight) && !float.IsInfinity(weight);

    private readonly struct TierWeight
    {
        public TierWeight(ETier tier, float weight)
        {
            Tier = tier;
            Weight = weight;
        }

        public ETier Tier { get; }

        public float Weight { get; }
    }
}
