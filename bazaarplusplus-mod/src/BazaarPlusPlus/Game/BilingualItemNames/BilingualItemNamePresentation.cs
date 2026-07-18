#nullable enable
namespace BazaarPlusPlus.Game.BilingualItemNames;

internal static class BilingualItemNamePresentation
{
    private const string SubtitleSize = "42%";
    private const string SubtitleOffset = "-7px";
    private const string EnglishSubtitleLeadingSpace = "<space=2px>";

    internal static string? TryBuild(
        string? primaryTitle,
        string? secondaryTitle,
        bool enabled,
        bool isSupportedCard,
        bool alignEnglishSubtitle
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

        var leadingSpace = alignEnglishSubtitle ? EnglishSubtitleLeadingSpace : string.Empty;
        return $"{primaryTitle}\n<size={SubtitleSize}><voffset={SubtitleOffset}>{leadingSpace}<noparse>{secondaryTitle.Trim()}</noparse></voffset></size>";
    }
}
