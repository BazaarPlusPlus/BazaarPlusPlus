#nullable enable
namespace BazaarPlusPlus.Game.BilingualItemNames;

internal static class BilingualItemNamePresentation
{
    internal static string? TryBuildSubtitle(
        string? primaryTitle,
        string? secondaryTitle,
        bool enabled,
        bool isSupportedCard
    )
    {
        if (
            !enabled
            || !isSupportedCard
            || string.IsNullOrWhiteSpace(primaryTitle)
            || string.IsNullOrWhiteSpace(secondaryTitle)
            || string.Equals(primaryTitle.Trim(), secondaryTitle.Trim(), StringComparison.Ordinal)
        )
            return null;

        return secondaryTitle.Trim();
    }
}
