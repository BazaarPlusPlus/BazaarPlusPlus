#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

public static class CollectionGridConstants
{
    public const int ItemColumns = 10;
    public const int SkillColumns = 7;
    public const int SkillRowSpan = 1;
    public const int ItemRowSpan = 2;
    public const int ItemSmallSpan = 1;
    public const int ItemMediumSpan = 2;
    public const int ItemLargeSpan = 3;

    public const float GridGap = 14f;
    public const float GridOuterPadding = 18f;
    public const float MinUnitWidth = 24f;

    // Inset the native card so the slot background remains visible as its frame.
    public const float CellContentInset = 0.06f;

    public const int RowOverscan = 1;

    // Cold item binds trigger Addressables loads. Repositioning a bound card is cheap.
    public const float ColdBindBudgetMs = 3f;

    // These are exponential-lerp time constants, so visible motion settles in about three times the specified duration.
    public const float CardFadeInSeconds = 0.18f;
    public const float CardHoverScale = 1.2f;
    public const float CardHoverScaleInSeconds = 0.08f;
    public const float CardHoverScaleOutSeconds = 0.1f;
    public const float CardHoverShadowAlpha = 0.2f;
    public const float CardHoverShadowInSeconds = 0.06f;
    public const float CardHoverShadowOutSeconds = 0.1f;
    public const float CardHoverShadowPadding = 10f;
    public const float CardHoverShadowOffsetY = -5f;

    // A shorter close fade settles in about 120 ms so dismissing feels immediate while avoiding a hard cut.
    public const float PanelFadeInSeconds = 0.12f;
    public const float PanelFadeOutSeconds = 0.04f;

    public const float ItemCardWidthScale = 0.96f;

    public static int ItemWidthSpan(CardSize size) =>
        size switch
        {
            CardSize.Large => ItemLargeSpan,
            CardSize.Medium => ItemMediumSpan,
            _ => ItemSmallSpan,
        };

    public static int RowSpanFor(CardType type) =>
        type == CardType.Skill ? SkillRowSpan : ItemRowSpan;

    public static int ColumnsFor(CardType type) =>
        type == CardType.Skill ? SkillColumns : ItemColumns;
}
