#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Heroes;

/// <summary>
/// Shared hero identity badge resolver: maps a hero name (<c>EHero.ToString()</c>) to a short
/// code, brand background color, and a luminance-picked text color. HistoryPanel and
/// LiveBuildPanel consume one source of truth instead of hand-rolling the mapping.
/// </summary>
internal static class HeroVisual
{
    internal readonly struct HeroBadgeStyle
    {
        public HeroBadgeStyle(string shortCode, Color background, Color text)
        {
            ShortCode = shortCode;
            Background = background;
            Text = text;
        }

        public string ShortCode { get; }

        public Color Background { get; }

        public Color Text { get; }
    }

    internal static HeroBadgeStyle Resolve(string? heroName)
    {
        if (string.IsNullOrWhiteSpace(heroName))
            return new HeroBadgeStyle("UNK", InGameTheme.HeroNone, Color.white);

        if (TheDragonsHeroIdentity.IsAlias(heroName))
            return Build("DRA", InGameTheme.HeroDragons);

        return heroName.Trim() switch
        {
            "Vanessa" => Build("VAN", InGameTheme.HeroVanessa),
            "Pygmalien" => Build("PYG", InGameTheme.HeroPygmalien),
            "Dooley" => Build("DOO", InGameTheme.HeroDooley),
            "Mak" => Build("MAK", InGameTheme.HeroMak),
            "Jules" => Build("JUL", InGameTheme.HeroJules),
            "Karnok" => Build("KAR", InGameTheme.HeroKarnok),
            "Stelle" => Build("STE", InGameTheme.HeroStelle),
            _ => Build(
                heroName.Length <= 3
                    ? heroName.ToUpperInvariant()
                    : heroName[..3].ToUpperInvariant(),
                InGameTheme.HeroUnlisted
            ),
        };
    }

    // Dooley sits on a knife-edge: luminance 0.622 (just over the 0.62 cutoff) -> dark text. Keep
    // the channel weights and the > 0.62f threshold byte-for-byte or his badge text color flips.
    private static HeroBadgeStyle Build(string shortCode, Color background)
    {
        var luminance = (0.299f * background.r) + (0.587f * background.g) + (0.114f * background.b);
        var text = luminance > 0.62f ? InGameTheme.HeroInkDark : Color.white;
        return new HeroBadgeStyle(shortCode, background, text);
    }
}
