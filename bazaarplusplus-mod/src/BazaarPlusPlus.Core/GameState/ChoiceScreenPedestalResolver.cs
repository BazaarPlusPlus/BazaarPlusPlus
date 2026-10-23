#nullable enable

namespace BazaarPlusPlus.Core.GameState;

/// <summary>Classifies the pedestal templates a screen offers through
/// <see cref="PedestalEnchantCatalog"/>: the choice screen's selection set, or the one pedestal
/// encounter the player stands in. The pedestal's own <c>Behavior</c> is obfuscated on the
/// client, so the template id is the only stable handle. Each host reads the ids from its game
/// runtime; this rule is shared.</summary>
public static class ChoiceScreenPedestalResolver
{
    public static ChoicePedestalSnapshot Resolve(IReadOnlyList<Guid>? templateIds)
    {
        if (templateIds == null || templateIds.Count == 0)
            return ChoicePedestalSnapshot.Empty;

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

            if (enchant != null && seenNames.Add(enchant))
                enchantNames.Add(enchant);
        }

        if (kind == ChoiceScreenPedestalKind.None)
            return ChoicePedestalSnapshot.Empty;

        return new ChoicePedestalSnapshot
        {
            Kind = kind,
            EnchantmentTypeNames =
                enchantNames.Count == 0 ? Array.Empty<string>() : enchantNames.ToArray(),
        };
    }
}
