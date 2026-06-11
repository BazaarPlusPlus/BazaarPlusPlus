#nullable enable
using System;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal sealed class CollectionPanelSelectionState
{
    public const EHero DefaultHero = EHero.Vanessa;
    public const string DefaultMerchantSourceKey = "merchant:jay-jay:global";

    public static CollectionPanelSelectionState Default { get; } =
        new(DefaultHero, DefaultMerchantSourceKey, CollectionSourceKind.Merchant);

    public CollectionPanelSelectionState(
        EHero? selectedHero,
        string? selectedSourceKey,
        CollectionSourceKind selectedSourceKind
    )
    {
        SelectedHero = selectedHero;
        SelectedSourceKey = NormalizeSourceKey(selectedSourceKey);
        SelectedSourceKind = selectedSourceKind;
    }

    public EHero? SelectedHero { get; }

    public string? SelectedSourceKey { get; }

    public CollectionSourceKind SelectedSourceKind { get; }

    public override bool Equals(object? obj)
    {
        return obj is CollectionPanelSelectionState other
            && SelectedHero == other.SelectedHero
            && SelectedSourceKind == other.SelectedSourceKind
            && string.Equals(SelectedSourceKey, other.SelectedSourceKey, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + SelectedHero.GetHashCode();
            hash = (hash * 31) + SelectedSourceKind.GetHashCode();
            hash =
                (hash * 31)
                + (
                    SelectedSourceKey == null
                        ? 0
                        : StringComparer.Ordinal.GetHashCode(SelectedSourceKey)
                );
            return hash;
        }
    }

    private static string? NormalizeSourceKey(string? sourceKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            return null;
        return sourceKey.Trim();
    }
}
