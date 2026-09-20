#nullable enable
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.GameInterop.Encounter;

/// <summary>The kind of pedestal offered on the choice screen plus, for enchant
/// pedestals, the enchant type name(s) it would apply. The choice screen can list
/// several pedestals at once, so the names are the union across every offered enchant
/// pedestal. Names are strings so the Core snapshot that stores them stays free of the
/// game's <c>EEnchantmentType</c>.</summary>
internal readonly struct ChoiceScreenPedestalResult
{
    public ChoiceScreenPedestalKind Kind { get; init; }
    public IReadOnlyList<string> EnchantmentTypeNames { get; init; }

    public static ChoiceScreenPedestalResult None { get; } =
        new()
        {
            Kind = ChoiceScreenPedestalKind.None,
            EnchantmentTypeNames = Array.Empty<string>(),
        };
}

/// <summary>Classifies the choice screen's offered template ids through
/// <see cref="PedestalEnchantCatalog"/>. The pedestal's own <c>Behavior</c> is
/// obfuscated on the client.</summary>
internal static class ChoiceScreenPedestalResolver
{
    internal static ChoiceScreenPedestalResult ResolveDetailedFromTemplateIds(
        IReadOnlyList<Guid>? templateIds
    )
    {
        if (templateIds == null || templateIds.Count == 0)
            return ChoiceScreenPedestalResult.None;

        // A choice screen historically never mixes upgrade and enchant pedestals, so
        // taking the first non-None pedestal's kind is safe; enchant type names are
        // still aggregated across every offered enchant pedestal so the preview can
        // match all of them.
        var kind = ChoiceScreenPedestalKind.None;
        var enchantNames = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var templateId in templateIds)
        {
            if (templateId == Guid.Empty)
                continue;

            var entryKind = PedestalEnchantCatalog.Classify(templateId, out var enchant);
            if (kind == ChoiceScreenPedestalKind.None && entryKind != ChoiceScreenPedestalKind.None)
                kind = entryKind;

            if (enchant.HasValue)
            {
                var name = enchant.Value.ToString();
                if (seenNames.Add(name))
                    enchantNames.Add(name);
            }
        }

        if (kind == ChoiceScreenPedestalKind.None)
            return ChoiceScreenPedestalResult.None;

        return new ChoiceScreenPedestalResult
        {
            Kind = kind,
            EnchantmentTypeNames =
                enchantNames.Count == 0 ? Array.Empty<string>() : enchantNames.ToArray(),
        };
    }
}
