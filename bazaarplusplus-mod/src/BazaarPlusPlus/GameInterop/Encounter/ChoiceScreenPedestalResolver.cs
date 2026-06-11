#nullable enable
using System;
using System.Collections.Generic;
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

/// <summary>Classifies the choice screen's offered pedestals. Each SelectionSet entry
/// is a live instance id; the supplied lookup turns it into the stable template id,
/// which <see cref="PedestalEnchantCatalog"/> maps to kind + enchant type. Reading the
/// pedestal's own <c>Behavior</c> is useless on the client (it is obfuscated), hence
/// the catalog.</summary>
internal static class ChoiceScreenPedestalResolver
{
    internal static ChoiceScreenPedestalKind Resolve(
        IReadOnlyList<string>? selectionSet,
        Func<string, Guid?> templateIdLookup
    ) => ResolveDetailed(selectionSet, templateIdLookup).Kind;

    internal static ChoiceScreenPedestalKind ResolveFromTemplateIds(
        IReadOnlyList<Guid>? templateIds
    ) => ResolveDetailedFromTemplateIds(templateIds).Kind;

    internal static ChoiceScreenPedestalResult ResolveDetailed(
        IReadOnlyList<string>? selectionSet,
        Func<string, Guid?> templateIdLookup
    )
    {
        if (selectionSet == null || selectionSet.Count == 0)
            return ChoiceScreenPedestalResult.None;

        if (templateIdLookup == null)
            throw new ArgumentNullException(nameof(templateIdLookup));

        var templateIds = new List<Guid>(selectionSet.Count);
        foreach (var id in selectionSet)
        {
            if (string.IsNullOrEmpty(id))
                continue;

            var templateId = templateIdLookup(id);
            if (templateId is null)
                continue;

            templateIds.Add(templateId.Value);
        }

        return ResolveDetailedFromTemplateIds(templateIds);
    }

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
