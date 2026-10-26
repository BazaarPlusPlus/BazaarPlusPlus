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
        // The name collections are created on the first enchant name, so a selection without an
        // enchant pedestal allocates nothing.
        var kind = ChoiceScreenPedestalKind.None;
        List<string>? enchantNames = null;
        HashSet<string>? seenNames = null;

        for (var i = 0; i < templateIds.Count; i++)
        {
            var templateId = templateIds[i];
            if (templateId == Guid.Empty)
                continue;

            var entryKind = PedestalEnchantCatalog.Classify(templateId, out var enchant);
            if (kind == ChoiceScreenPedestalKind.None && entryKind != ChoiceScreenPedestalKind.None)
                kind = entryKind;

            if (enchant == null)
                continue;
            seenNames ??= new HashSet<string>(StringComparer.Ordinal);
            if (seenNames.Add(enchant))
                (enchantNames ??= new List<string>()).Add(enchant);
        }

        if (kind == ChoiceScreenPedestalKind.None)
            return ChoicePedestalSnapshot.Empty;

        return new ChoicePedestalSnapshot
        {
            Kind = kind,
            EnchantmentTypeNames =
                enchantNames == null ? Array.Empty<string>() : enchantNames.ToArray(),
        };
    }
}
