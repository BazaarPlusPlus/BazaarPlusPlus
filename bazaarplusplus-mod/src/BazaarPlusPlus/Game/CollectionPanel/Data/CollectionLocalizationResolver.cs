#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// TCardLocalization (decompiled/Cards/TCardLocalization.cs) carries a Title TLocalizableText
// with Key + Text. The decompiled TLocalizableText (decompiled/Core/TLocalizableText.cs) only
// exposes Key + Text and does not branch by language; the live game appears to populate Text
// with the player's current language at load time. We accept the Text field verbatim, falling
// back to the key for diagnostic visibility, then InternalName at the caller.
//
// If a future game update separates language variants, this is the single seam to update.
internal static class CollectionLocalizationResolver
{
    public static string? ResolveTitle(TCardBase template)
    {
        var title = template.Localization?.Title;
        if (title == null)
            return null;
        return PickText(title);
    }

    private static string? PickText(TLocalizableText text)
    {
        if (!string.IsNullOrWhiteSpace(text.Text))
            return text.Text;
        if (!string.IsNullOrWhiteSpace(text.Key))
            return text.Key;
        return null;
    }
}
