#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionPanelHeroPreference
{
    private const string AnonymousAccountScope = "anonymous";
    private const string PrefsKeyPrefix = "BPP.CollectionPanel.SelectedHero";

    public static string BuildPrefsKey(string? accountScope)
    {
        var scope = string.IsNullOrWhiteSpace(accountScope)
            ? AnonymousAccountScope
            : Uri.EscapeDataString(accountScope);
        return $"{PrefsKeyPrefix}.{scope}";
    }

    public static string Serialize(EHero hero) => hero.ToString();

    public static bool TryParse(string? raw, out EHero hero)
    {
        hero = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (!Enum.TryParse(raw.Trim(), ignoreCase: false, out EHero parsed))
            return false;

        if (!IsSupportedHero(parsed))
            return false;

        hero = parsed;
        return true;
    }

    public static bool IsSupportedHero(EHero hero)
    {
        return hero
            is EHero.Common
                or EHero.Vanessa
                or EHero.Dooley
                or EHero.Pygmalien
                or EHero.Karnok
                or EHero.Mak
                or EHero.Stelle
                or EHero.Jules;
    }
}
