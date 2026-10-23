using BazaarPlusPlus.Game.CollectionPanel.Data;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppBazaarGameShared.Domain.Core;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// A static card template's authored text in the current language, falling back to the authored
/// English: its title (or internal name) and its description, with <c>{ability.*}</c> placeholders
/// dropped as the Collection catalog drops them. Null when static data is not ready, the template
/// is unknown, or the lookup fails. Main thread only.
/// </summary>
internal static class AndroidCardTemplateText
{
    public static string? Title(Guid templateId)
    {
        var template = Template(templateId);
        if (template == null)
            return null;
        return Text(template.Localization?.Title)
            ?? (string.IsNullOrWhiteSpace(template.InternalName) ? null : template.InternalName);
    }

    /// <summary>The description, or else the tooltips one per line, as most items author their text there.</summary>
    public static string? Description(Guid templateId)
    {
        var localization = Template(templateId)?.Localization;
        if (localization == null)
            return null;
        var description = Formatted(Text(localization.Description));
        if (description != null)
            return description;
        var lines = new List<string>();
        try
        {
            var tooltips = localization.Tooltips;
            if (tooltips != null)
                foreach (var tooltip in tooltips)
                    if (Formatted(Text(tooltip?.Content)) is { } line && !lines.Contains(line))
                        lines.Add(line);
        }
        catch (Exception)
        {
            // A partial list is still worth showing.
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string? Formatted(string? text)
    {
        var formatted = CollectionAbilityPlaceholders.Format(text, NoAbilityValue)?.Trim();
        return string.IsNullOrWhiteSpace(formatted) ? null : formatted;
    }

    private static TCardBase? Template(Guid templateId)
    {
        try
        {
            if (!Il2CppTheBazaar.Data.IsManagerCreated())
                return null;
            return Il2CppTheBazaar
                .Data.GetStatic()
                ?.GetCardById(Il2CppSystem.Guid.Parse(templateId.ToString()))
                ?.TryCast<TCardBase>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Text(TLocalizableText? text)
    {
        if (text == null)
            return null;
        try
        {
            var localized = Il2CppTheBazaar.Tooltips.TooltipExtensions.GetLocalizedText(text);
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }
        catch (Exception)
        {
            // The localization service may not be up; the authored English stands in.
        }
        return string.IsNullOrWhiteSpace(text.Text) ? null : text.Text;
    }

    private static bool NoAbilityValue(string abilityId, out string valueText, out string? unit)
    {
        valueText = string.Empty;
        unit = null;
        return false;
    }
}
