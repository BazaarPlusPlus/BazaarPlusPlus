#nullable enable
using System.Collections;
using System.Globalization;
using BazaarGameShared.Domain.Cards;

namespace BazaarPlusPlus.GameInterop.Cards;

internal readonly record struct CardAbilityValue(string ValueText, string? Unit);

internal static class CardAbilityValueReader
{
    private static readonly HashSet<string> KnownAttributeUnits = new(StringComparer.Ordinal)
    {
        "Heal",
        "Poison",
        "Shield",
        "Burn",
        "Damage",
        "Regen",
        "Freeze",
        "Haste",
        "Slow",
        "Charge",
        "Ammo",
        "Lifesteal",
        "Crit",
        "Income",
        "Gold",
        "Experience",
        "Value",
    };

    internal static bool TryRead(TCardBase template, string abilityId, out CardAbilityValue result)
    {
        result = default;
        var abilities = template.GetType().GetProperty("Abilities")?.GetValue(template);
        if (abilities is not IEnumerable enumerable)
            return false;

        var dot = abilityId.IndexOf('.');
        var baseAbilityId = dot > 0 ? abilityId[..dot] : abilityId;
        var accessor = dot > 0 ? abilityId[(dot + 1)..] : null;
        foreach (var entry in enumerable)
        {
            if (!TryReadEntry(entry, out var key, out var ability))
                continue;
            if (!string.Equals(key?.ToString(), baseAbilityId, StringComparison.Ordinal))
                continue;

            var action = ability?.GetType().GetProperty("Action")?.GetValue(ability);
            var value = action?.GetType().GetProperty("Value")?.GetValue(action);
            if (string.Equals(accessor, "mod", StringComparison.OrdinalIgnoreCase))
            {
                var modifier = value?.GetType().GetProperty("Modifier")?.GetValue(value);
                var modifierValue = modifier?.GetType().GetProperty("Value")?.GetValue(modifier);
                var modifierScalar = modifierValue
                    ?.GetType()
                    .GetProperty("Value")
                    ?.GetValue(modifierValue);
                if (!TryFormatScalar(modifierScalar, out var modifierText))
                    return false;
                result = new CardAbilityValue(modifierText, null);
                return true;
            }

            var scalar = value?.GetType().GetProperty("Value")?.GetValue(value);
            if (TryFormatScalar(scalar, out var scalarText))
            {
                result = new CardAbilityValue(scalarText, ResolveAttributeUnit(action));
                return true;
            }

            var spawnContext = action?.GetType().GetProperty("SpawnContext")?.GetValue(action);
            var limit = spawnContext?.GetType().GetProperty("Limit")?.GetValue(spawnContext);
            var limitScalar = limit?.GetType().GetProperty("Value")?.GetValue(limit);
            if (TryFormatScalar(limitScalar, out var limitText))
            {
                result = new CardAbilityValue(limitText, null);
                return true;
            }

            return TryReadFromCardAttributes(template, action, out result);
        }

        return false;
    }

    private static bool TryReadFromCardAttributes(
        TCardBase template,
        object? action,
        out CardAbilityValue result
    )
    {
        result = default;
        var actionName = action?.GetType().Name;
        if (actionName == null)
            return false;

        const string PlayerPrefix = "TActionPlayer";
        const string CardPrefix = "TActionCard";
        var core =
            actionName.StartsWith(PlayerPrefix, StringComparison.Ordinal)
                ? actionName[PlayerPrefix.Length..]
            : actionName.StartsWith(CardPrefix, StringComparison.Ordinal)
                ? actionName[CardPrefix.Length..]
            : null;
        if (string.IsNullOrEmpty(core))
            return false;

        var attributeKey = core + "Amount";
        var attributes = template.GetType().GetProperty("Attributes")?.GetValue(template);
        if (attributes is not IEnumerable entries)
            return false;
        foreach (var entry in entries)
        {
            if (!TryReadEntry(entry, out var key, out var attributeValue))
                continue;
            if (!string.Equals(key?.ToString(), attributeKey, StringComparison.Ordinal))
                continue;
            if (!TryFormatScalar(attributeValue, out var valueText))
                return false;
            result = new CardAbilityValue(valueText, NormalizeAttributeUnit(attributeKey));
            return true;
        }
        return false;
    }

    private static string? ResolveAttributeUnit(object? action)
    {
        var attribute = action?.GetType().GetProperty("AttributeType")?.GetValue(action);
        return attribute == null ? null : NormalizeAttributeUnit(attribute.ToString());
    }

    private static string? NormalizeAttributeUnit(string? name)
    {
        if (string.IsNullOrEmpty(name) || name!.Contains('_'))
            return null;
        if (name.EndsWith("Amount", StringComparison.Ordinal))
            name = name[..^"Amount".Length];
        if (name.EndsWith("Apply", StringComparison.Ordinal))
            name = name[..^"Apply".Length];
        return KnownAttributeUnits.Contains(name) ? name : null;
    }

    private static bool TryReadEntry(object entry, out object? key, out object? value)
    {
        if (entry is DictionaryEntry dictionaryEntry)
        {
            key = dictionaryEntry.Key;
            value = dictionaryEntry.Value;
            return true;
        }

        var type = entry.GetType();
        key = type.GetProperty("Key")?.GetValue(entry);
        value = type.GetProperty("Value")?.GetValue(entry);
        return key != null;
    }

    private static bool TryFormatScalar(object? scalar, out string valueText)
    {
        valueText = string.Empty;
        switch (scalar)
        {
            case null:
                return false;
            case float value:
                valueText = FormatNumber(value);
                return true;
            case double value:
                valueText = FormatNumber(value);
                return true;
            case decimal value:
                valueText = FormatNumber((double)value);
                return true;
            case int value:
                valueText = value.ToString(CultureInfo.InvariantCulture);
                return true;
            case long value:
                valueText = value.ToString(CultureInfo.InvariantCulture);
                return true;
            default:
                valueText = scalar.ToString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(valueText);
        }
    }

    private static string FormatNumber(double value)
    {
        var rounded = Math.Round(value);
        return Math.Abs(value - rounded) < 0.0001
            ? rounded.ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
