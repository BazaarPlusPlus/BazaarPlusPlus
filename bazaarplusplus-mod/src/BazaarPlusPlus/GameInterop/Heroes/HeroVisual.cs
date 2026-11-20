#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Heroes;

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

    // Dooley's luminance is 0.622, just above the 0.62 cutoff. Small changes can flip his text color.
    private static HeroBadgeStyle Build(string shortCode, Color background)
    {
        var luminance = (0.299f * background.r) + (0.587f * background.g) + (0.114f * background.b);
        var text = luminance > 0.62f ? InGameTheme.HeroInkDark : Color.white;
        return new HeroBadgeStyle(shortCode, background, text);
    }
}
