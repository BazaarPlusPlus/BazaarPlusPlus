#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.EncounterPreview;

internal static class EncounterPreviewGameText
{
    internal static string? Localize(EncounterPreviewLocalizedText text) =>
        TheBazaar.Tooltips.TooltipExtensions.GetLocalizedText(
            new TLocalizableText
            {
                Key = text.Key ?? string.Empty,
                Text = text.FallbackText ?? string.Empty,
            }
        );

    internal static IReadOnlyDictionary<string, EncounterPreviewAbilityValue> CaptureAbilityValues(
        TCardBase template
    )
    {
        var result = new Dictionary<string, EncounterPreviewAbilityValue>(StringComparer.Ordinal);
        var abilities = template.Abilities;
        if (abilities != null)
        {
            foreach (var abilityId in abilities.Keys)
            {
                AddAbilityValue(abilityId);
                AddAbilityValue($"{abilityId}.mod");
            }
        }
        var auras = template.Auras;
        if (auras != null)
        {
            foreach (var auraId in auras.Keys)
            {
                AddAuraValue(auraId);
                AddAuraValue($"{auraId}.mod");
            }
        }
        return result;

        void AddAbilityValue(string placeholder)
        {
            if (CardAbilityValueReader.TryRead(template, placeholder, out var value))
                result[placeholder] = new EncounterPreviewAbilityValue(value.ValueText, value.Unit);
        }

        void AddAuraValue(string placeholder)
        {
            if (CardAbilityValueReader.TryReadAura(template, placeholder, out var value))
            {
                // Aura keys are namespaced to avoid collisions with unprefixed ability IDs.
                result[$"aura.{placeholder}"] = new EncounterPreviewAbilityValue(
                    value.ValueText,
                    value.Unit
                );
            }
        }
    }
}
