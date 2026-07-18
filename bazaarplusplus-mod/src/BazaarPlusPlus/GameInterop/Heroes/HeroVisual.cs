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
            return new HeroBadgeStyle("UNK", Colors.HeroUnknownBackground, Colors.White);

        return heroName.Trim() switch
        {
            "Vanessa" => Build("VAN", Colors.HeroVanessaBackground),
            "Pygmalien" => Build("PYG", Colors.HeroPygmalienBackground),
            "Dooley" => Build("DOO", Colors.HeroDooleyBackground),
            "Mak" => Build("MAK", Colors.HeroMakBackground),
            "Jules" => Build("JUL", Colors.HeroJulesBackground),
            "Karnok" => Build("KAR", Colors.HeroKarnokBackground),
            "Stelle" => Build("STE", Colors.HeroStelleBackground),
            _ => Build(
                heroName.Length <= 3
                    ? heroName.ToUpperInvariant()
                    : heroName[..3].ToUpperInvariant(),
                Colors.HeroDefaultBackground
            ),
        };
    }

    // The seven playable heroes. The corpus also keys a "Common" pool (and may carry other
    // non-playable keys); callers filter on this so only real heroes surface.
    internal static bool IsPlayableHero(string? heroName) =>
        heroName?.Trim() switch
        {
            "Vanessa" or "Pygmalien" or "Dooley" or "Mak" or "Jules" or "Karnok" or "Stelle" =>
                true,
            _ => false,
        };

    // Dooley sits on a knife-edge: luminance 0.622 (just over the 0.62 cutoff) -> dark text. Keep
    // the channel weights and the > 0.62f threshold byte-for-byte or his badge text color flips.
    private static HeroBadgeStyle Build(string shortCode, Color background)
    {
        var luminance = (0.299f * background.r) + (0.587f * background.g) + (0.114f * background.b);
        var text = luminance > 0.62f ? Colors.HeroDarkText : Colors.White;
        return new HeroBadgeStyle(shortCode, background, text);
    }
}
