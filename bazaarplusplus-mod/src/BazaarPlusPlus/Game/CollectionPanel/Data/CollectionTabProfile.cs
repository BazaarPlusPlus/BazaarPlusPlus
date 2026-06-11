#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal readonly struct CollectionTabProfile
{
    private CollectionTabProfile(
        ECardType cardType,
        CollectionSourceKind sourceKind,
        bool showSizeFilter,
        bool showTagFilter,
        bool showKeywordFilter
    )
    {
        CardType = cardType;
        SourceKind = sourceKind;
        ShowSizeFilter = showSizeFilter;
        ShowTagFilter = showTagFilter;
        ShowKeywordFilter = showKeywordFilter;
    }

    public ECardType CardType { get; }

    public CollectionSourceKind SourceKind { get; }

    public bool ShowHeroFilter => true;

    public bool ShowTierFilter => true;

    public bool ShowSizeFilter { get; }

    public bool ShowTagFilter { get; }

    public bool ShowKeywordFilter { get; }

    public bool ShowDayFilter => true;

    public static CollectionTabProfile For(ECardType cardType) =>
        cardType == ECardType.Skill
            ? new CollectionTabProfile(
                ECardType.Skill,
                CollectionSourceKind.Trainer,
                showSizeFilter: false,
                showTagFilter: false,
                showKeywordFilter: true
            )
            : new CollectionTabProfile(
                ECardType.Item,
                CollectionSourceKind.Merchant,
                showSizeFilter: true,
                showTagFilter: true,
                showKeywordFilter: true
            );
}
