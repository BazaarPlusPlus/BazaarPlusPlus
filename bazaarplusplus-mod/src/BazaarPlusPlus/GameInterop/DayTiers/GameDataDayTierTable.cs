#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.DayTiers;

internal readonly record struct GameDataDayTierProbability(ETier Tier, double Percent);

internal sealed class GameDataDayTierTable
{
    private GameDataDayTierTable(
        IReadOnlyList<GameDataDayTierProbability> entries,
        ETier maximumTier
    )
    {
        Entries = entries;
        MaximumTier = maximumTier;
    }

    public IReadOnlyList<GameDataDayTierProbability> Entries { get; }

    public ETier MaximumTier { get; }

    public static GameDataDayTierTable? FromWeights(
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
        ETier? maximumTier = null;
        foreach (var entry in weights)
        {
            if (!IsUsable(entry.Weight))
                continue;
            total += entry.Weight;
            maximumTier = entry.Tier;
        }

        if (!maximumTier.HasValue || total <= 0 || double.IsNaN(total) || double.IsInfinity(total))
            return null;

        var normalized = new List<GameDataDayTierProbability>(weights.Length);
        foreach (var entry in weights)
        {
            if (!IsUsable(entry.Weight))
                continue;
            normalized.Add(new GameDataDayTierProbability(entry.Tier, entry.Weight / total * 100d));
        }

        return new GameDataDayTierTable(normalized.ToArray(), maximumTier.Value);
    }

    private static bool IsUsable(float weight) =>
        weight > 0 && !float.IsNaN(weight) && !float.IsInfinity(weight);

    private readonly record struct TierWeight(ETier Tier, float Weight);
}

internal static class GameDataDayTierOrder
{
    public static int Rank(ETier tier) =>
        tier switch
        {
            ETier.Bronze => 0,
            ETier.Silver => 1,
            ETier.Gold => 2,
            ETier.Diamond => 3,
            ETier.Legendary => 4,
            _ => 99,
        };
}
