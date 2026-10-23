#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Core.GameState;

public readonly record struct GameDataDayTierProbability(CardTier Tier, double Percent);

public sealed class GameDataDayTierTable
{
    private GameDataDayTierTable(
        IReadOnlyList<GameDataDayTierProbability> entries,
        CardTier maximumTier
    )
    {
        Entries = entries;
        MaximumTier = maximumTier;
    }

    public IReadOnlyList<GameDataDayTierProbability> Entries { get; }

    public CardTier MaximumTier { get; }

    public static GameDataDayTierTable? FromWeights(
        float bronze,
        float silver,
        float gold,
        float diamond
    )
    {
        var weights = new[]
        {
            new TierWeight(CardTier.Bronze, bronze),
            new TierWeight(CardTier.Silver, silver),
            new TierWeight(CardTier.Gold, gold),
            new TierWeight(CardTier.Diamond, diamond),
        };

        double total = 0;
        CardTier? maximumTier = null;
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

    private readonly record struct TierWeight(CardTier Tier, float Weight);
}
