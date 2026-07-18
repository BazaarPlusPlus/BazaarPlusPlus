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
        bool showHeroFilter,
        bool showTierFilter,
        bool showSizeFilter,
        bool showTagFilter,
        bool showKeywordFilter,
        bool showDayFilter
    )
    {
        Tab = tab;
        CardType = cardType;
        SourceKind = sourceKind;
        ShowHeroFilter = showHeroFilter;
        ShowTierFilter = showTierFilter;
        ShowSizeFilter = showSizeFilter;
        ShowTagFilter = showTagFilter;
        ShowKeywordFilter = showKeywordFilter;
        ShowDayFilter = showDayFilter;
    }

    public CollectionTabKind Tab { get; }

    public ECardType CardType { get; }

    public CollectionSourceKind? SourceKind { get; }

    public bool ShowHeroFilter { get; }

    public bool ShowTierFilter { get; }

    public bool ShowSizeFilter { get; }

    public bool ShowTagFilter { get; }

    public bool ShowKeywordFilter { get; }

    public bool ShowDayFilter { get; }

    public bool ShowSourceFilter => SourceKind.HasValue;

    public static CollectionTabProfile For(CollectionTabKind tab) =>
        tab switch
        {
            CollectionTabKind.Skills => new CollectionTabProfile(
                CollectionTabKind.Skills,
                ECardType.Skill,
                CollectionSourceKind.Trainer,
                showHeroFilter: true,
                showTierFilter: true,
                showSizeFilter: false,
                showTagFilter: false,
                showKeywordFilter: true,
                showDayFilter: true
            ),
            _ => new CollectionTabProfile(
                CollectionTabKind.Items,
                ECardType.Item,
                CollectionSourceKind.Merchant,
                showHeroFilter: true,
                showTierFilter: true,
                showSizeFilter: true,
                showTagFilter: true,
                showKeywordFilter: true,
                showDayFilter: true
            ),
        };

    public static CollectionTabProfile For(ECardType cardType) =>
        For(cardType == ECardType.Skill ? CollectionTabKind.Skills : CollectionTabKind.Items);
}
