#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal readonly struct CollectionTabProfile
{
    private CollectionTabProfile(
        CollectionTabKind tab,
        ECardType cardType,
        CollectionSourceKind? sourceKind,
        bool showTierFilter,
        bool showSizeFilter,
        bool showTagFilter,
        bool showKeywordFilter
    )
    {
        Tab = tab;
        CardType = cardType;
        SourceKind = sourceKind;
        ShowTierFilter = showTierFilter;
        ShowSizeFilter = showSizeFilter;
        ShowTagFilter = showTagFilter;
        ShowKeywordFilter = showKeywordFilter;
    }

    public CollectionTabKind Tab { get; }

    public ECardType CardType { get; }

    public CollectionSourceKind? SourceKind { get; }

    public bool ShowTierFilter { get; }

    public bool ShowSizeFilter { get; }

    public bool ShowTagFilter { get; }

    public bool ShowKeywordFilter { get; }

    public bool ShowSourceFilter => SourceKind.HasValue;

    public static CollectionTabProfile For(CollectionTabKind tab) =>
        tab switch
        {
            CollectionTabKind.Skills => new CollectionTabProfile(
                CollectionTabKind.Skills,
                ECardType.Skill,
                CollectionSourceKind.Trainer,
                showTierFilter: true,
                showSizeFilter: false,
                showTagFilter: true,
                showKeywordFilter: true
            ),
            _ => new CollectionTabProfile(
                CollectionTabKind.Items,
                ECardType.Item,
                CollectionSourceKind.Merchant,
                showTierFilter: true,
                showSizeFilter: true,
                showTagFilter: true,
                showKeywordFilter: true
            ),
        };

    public static CollectionTabProfile For(ECardType cardType) =>
        For(cardType == ECardType.Skill ? CollectionTabKind.Skills : CollectionTabKind.Items);
}
