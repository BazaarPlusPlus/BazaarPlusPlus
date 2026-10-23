#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public readonly struct CollectionTabProfile
{
    private CollectionTabProfile(
        CollectionTabKind tab,
        CardType cardType,
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

    public CardType CardType { get; }

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
                CardType.Skill,
                CollectionSourceKind.Trainer,
                showTierFilter: true,
                showSizeFilter: false,
                showTagFilter: true,
                showKeywordFilter: true
            ),
            _ => new CollectionTabProfile(
                CollectionTabKind.Items,
                CardType.Item,
                CollectionSourceKind.Merchant,
                showTierFilter: true,
                showSizeFilter: true,
                showTagFilter: true,
                showKeywordFilter: true
            ),
        };

    public static CollectionTabProfile For(CardType cardType) =>
        For(cardType == CardType.Skill ? CollectionTabKind.Skills : CollectionTabKind.Items);
}
