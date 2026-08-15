#nullable enable
using System.Collections;
using System.Globalization;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;

namespace BazaarPlusPlus.GameInterop.Cards;

internal readonly record struct CardAbilityValue(string ValueText, string? Unit);

internal static class CardAbilityValueReader
{
    private const string AbilitiesProperty = "Abilities";
    private const string AurasProperty = "Auras";

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

    internal static bool TryRead(
        TCardBase template,
        string abilityId,
        out CardAbilityValue result
    ) => TryRead(template, AbilitiesProperty, abilityId, out result);

    internal static bool TryReadAura(
        TCardBase template,
        string auraId,
        out CardAbilityValue result
    ) => TryRead(template, AurasProperty, auraId, out result);

    internal static bool TryEvaluate(
        TCardBase template,
        string abilityId,
        ValueContext context,
        out CardAbilityValue result
    ) => TryEvaluate(template, AbilitiesProperty, abilityId, context, out result);

    internal static bool TryEvaluateAura(
        TCardBase template,
        string auraId,
        ValueContext context,
        out CardAbilityValue result
    ) => TryEvaluate(template, AurasProperty, auraId, context, out result);

    // Static card data cannot resolve values backed by the current run (player
    // attributes, owned-card counts, aggregate card attributes). The native game
    // evaluates those ITValue graphs against a ValueContext; use that same graph at
    // presentation time instead of reimplementing each reference-value subtype.
    private static bool TryEvaluate(
        TCardBase template,
        string effectsProperty,
        string effectId,
        ValueContext context,
        out CardAbilityValue result
    )
    {
        result = default;
        if (effectId.IndexOf('.') >= 0)
            return false;

        var effects = template.GetType().GetProperty(effectsProperty)?.GetValue(template);
        if (effects is not IEnumerable enumerable)
            return false;

        foreach (var entry in enumerable)
        {
            if (!TryReadEntry(entry, out var key, out var effect))
                continue;
            if (!string.Equals(key?.ToString(), effectId, StringComparison.Ordinal))
                continue;

            var action = effect?.GetType().GetProperty("Action")?.GetValue(effect);
            if (action?.GetType().GetProperty("Value")?.GetValue(action) is not ITValue value)
                return false;
            if (!TryFormatScalar(value.GetValue(context), out var valueText))
                return false;

            result = new CardAbilityValue(valueText, ResolveAttributeUnit(action));
            return true;
        }

        return false;
    }

    private static bool TryRead(
        TCardBase template,
        string effectsProperty,
        string effectId,
        out CardAbilityValue result
    )
    {
        result = default;
        var effects = template.GetType().GetProperty(effectsProperty)?.GetValue(template);
        if (effects is not IEnumerable enumerable)
            return false;

        var dot = effectId.IndexOf('.');
        var baseEffectId = dot > 0 ? effectId[..dot] : effectId;
        var accessor = dot > 0 ? effectId[(dot + 1)..] : null;
        foreach (var entry in enumerable)
        {
            if (!TryReadEntry(entry, out var key, out var effect))
                continue;
            if (!string.Equals(key?.ToString(), baseEffectId, StringComparison.Ordinal))
                continue;

            var action = effect?.GetType().GetProperty("Action")?.GetValue(effect);
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

            if (TryReadCardAttributeReference(template, value, out var attributeText))
            {
                result = new CardAbilityValue(attributeText, ResolveAttributeUnit(action));
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

    private static bool TryReadCardAttributeReference(
        TCardBase template,
        object? value,
        out string valueText
    )
    {
        valueText = string.Empty;
        ECardAttributeType attributeType;
        TValueModifier? modifier;
        switch (value)
        {
            case TReferenceValueCardAttribute reference:
                attributeType = reference.AttributeType;
                modifier = reference.Modifier;
                break;
            case TReferenceValueCardAttributeUnscaled reference:
                attributeType = reference.AttributeType;
                modifier = reference.Modifier;
                break;
            default:
                return false;
        }

        var attributes = template.GetType().GetProperty("Attributes")?.GetValue(template);
        if (
            attributes is not IReadOnlyDictionary<ECardAttributeType, int> cardAttributes
            || !cardAttributes.TryGetValue(attributeType, out var original)
        )
            return false;

        if (modifier != null && modifier.Value is not TFixedValue)
            return false;

        var modified = modifier?.GetModifiedValue(original, default) ?? original;
        valueText = FormatNumber(modified);
        return true;
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
