#nullable enable

namespace BazaarPlusPlus.Core.GameState;

// The game obfuscates pedestal Behavior, leaving the template ID as the reliable identifier.
public static class ChoiceScreenPedestalResolver
{
    public static ChoicePedestalSnapshot Resolve(IReadOnlyList<Guid>? templateIds)
    {
        if (templateIds == null || templateIds.Count == 0)
            return ChoicePedestalSnapshot.Empty;

        // The game offers one pedestal kind per choice screen, but enchantment types may differ across the offers.
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
