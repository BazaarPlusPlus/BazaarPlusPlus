#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Infrastructure.UiTokens;

internal readonly struct CollectionFilterCardPalette
{
    public CollectionFilterCardPalette(
        Color background,
        Color borderTopLeft,
        Color borderTopRight,
        Color borderBottomLeft,
        Color borderBottomRight,
        Color topGlow,
        Color bottomGlow,
        Color decoration
    )
    {
        Background = background;
        BorderTopLeft = borderTopLeft;
        BorderTopRight = borderTopRight;
        BorderBottomLeft = borderBottomLeft;
        BorderBottomRight = borderBottomRight;
        TopGlow = topGlow;
        BottomGlow = bottomGlow;
        Decoration = decoration;
    }

    public Color Background { get; }
    public Color BorderTopLeft { get; }
    public Color BorderTopRight { get; }
    public Color BorderBottomLeft { get; }
    public Color BorderBottomRight { get; }
    public Color TopGlow { get; }
    public Color BottomGlow { get; }
    public Color Decoration { get; }
}

internal static class Colors
{
    public static Color White => Color.white;
    public static Color Clear => Color.clear;

    public static Color HistoryPanelBackground => Rgba(0.08f, 0.10f, 0.13f, 1f);
    public static Color HistorySectionBackground => Rgba(0.11f, 0.13f, 0.18f, 0.98f);
    public static Color HistoryListFrameBackground => Rgba(0.09f, 0.11f, 0.15f, 0.96f);
    public static Color HistoryListFrameBorder => Rgba(0.24f, 0.29f, 0.38f, 0.55f);
    public static Color GameTitleText => Rgba(1f, 0.8352941f, 0.6745098f, 1f);
    public static Color HistoryTitleText => Rgba(0.97f, 0.85f, 0.57f, 1f);
    public static Color HistorySubtitleText => Rgba(0.82f, 0.86f, 0.91f, 0.94f);
    public static Color HistorySectionTitleText => Rgba(0.76f, 0.91f, 1f, 1f);
    public static Color HistoryChipText => Rgba(0.95f, 0.96f, 0.98f, 1f);
    public static Color HistoryChipBackground => Rgba(0.14f, 0.18f, 0.23f, 0.96f);
    public static Color HistoryStatusText => Rgba(0.86f, 0.90f, 0.96f, 0.92f);
    public static Color HistoryStatusBackground => Rgba(0.16f, 0.20f, 0.26f, 0.72f);
    public static Color HistoryStatusBorder => Rgba(0.34f, 0.40f, 0.49f, 0.36f);
    public static Color HistoryButtonBackground => Rgba(0.23f, 0.27f, 0.32f, 0.98f);
    public static Color HistoryButtonBorder => Rgba(0.34f, 0.40f, 0.48f, 0.55f);
    public static Color HistoryRowBackground => Rgba(0.11f, 0.14f, 0.18f, 0.98f);
    public static Color HistoryRowCornerText => Rgba(0.72f, 0.78f, 0.85f, 0.92f);
    public static Color HistoryDayBubbleBackground => Rgba(0.24f, 0.28f, 0.36f, 0.98f);
    public static Color HistoryDayBubbleBorder => Rgba(0.52f, 0.60f, 0.72f, 0.45f);
    public static Color HistoryUnrankedBackground => Rgba(0.22f, 0.24f, 0.29f, 0.98f);
    public static Color HistoryUnrankedText => Rgba(0.90f, 0.94f, 1f, 1f);
    public static Color HistoryProgressBackground => Rgba(0.20f, 0.24f, 0.31f, 0.98f);
    public static Color HistoryProgressText => Rgba(0.89f, 0.94f, 1f, 1f);
    public static Color HistoryHealthAccent => Rgba(0.63f, 0.98f, 0.35f, 1f);
    public static Color HistoryPrestigeAccent => Rgba(1f, 0.65f, 0.13f, 1f);
    public static Color HistoryLevelAccent => Rgba(0.36f, 0.79f, 1f, 1f);
    public static Color HistoryGoldAccent => Rgba(1f, 0.86f, 0.10f, 1f);
    public static Color HistoryPlayerAccent => Rgba(0.44f, 0.76f, 1f, 1f);
    public static Color HistoryOpponentAccent => Rgba(0.96f, 0.77f, 0.39f, 1f);
    public static Color HistoryOpponentNameText => Rgba(0.76f, 0.80f, 0.87f, 0.92f);
    public static Color HistoryEliminatedText => Rgba(0.99f, 0.90f, 0.68f, 1f);
    public static Color HistoryEliminatedBackground => Rgba(0.32f, 0.24f, 0.10f, 0.96f);
    public static Color HistoryEliminatedBorder => Rgba(0.94f, 0.70f, 0.28f, 0.55f);
    public static Color HistoryEliminatedNoticeBorder => Rgba(0.94f, 0.70f, 0.28f, 0.48f);
    public static Color HistoryPreviewBackground => Rgba(0.07f, 0.09f, 0.12f, 0.99f);
    public static Color HistoryPreviewStatusText => Rgba(0.82f, 0.87f, 0.93f, 0.96f);
    public static Color HistoryPreviewDebugText => Rgba(0.97f, 0.85f, 0.57f, 0.96f);
    public static Color HistoryFooterBackground => Rgba(0.10f, 0.12f, 0.16f, 0.98f);
    public static Color HistoryFooterSecondaryText => Rgba(0.72f, 0.77f, 0.84f, 0.94f);

    // Collection Panel filter chrome follows the compact, near-black tag treatment used by the
    // native settings backdrop (#101113), but fully opaque: the game scene showing through the
    // full-screen catalog reads as clutter, and the grid occluders reuse this token to hide
    // cards scrolled outside the viewport, so any alpha here leaks card edges.
    public static Color CollectionPanelBackground => FromRgb(16, 17, 19, 1f);
    public static Color CollectionFilterCardBackground => FromRgb(22, 24, 28, 1f);
    public static Color CollectionFilterCardBorder => FromRgb(0, 0, 0, 1f);

    // The reference palette belongs only to the control deck. The grid still uses the neutral
    // CollectionFilterCardBackground, so its occlusion behavior remains unchanged.
    public static CollectionFilterCardPalette CollectionFilterCardReferencePalette =>
        new(
            CollectionFilterCardBackground,
            FromRgb(116, 127, 213, 0.54f),
            FromRgb(73, 169, 185, 0.28f),
            FromRgb(70, 110, 158, 0.24f),
            FromRgb(76, 194, 180, 0.54f),
            FromRgb(91, 107, 255, 0.30f),
            FromRgb(67, 221, 231, 0.26f),
            FromRgb(153, 235, 247, 0.14f)
        );

    // Secondary filters use the same cool direction as the control deck, but as a quiet
    // hierarchy cue rather than individual themed cards.
    public static CollectionFilterCardPalette CollectionFilterCardMutedPalette =>
        new(
            FromRgb(19, 21, 24, 1f),
            FromRgb(116, 127, 213, 0.16f),
            FromRgb(73, 169, 185, 0.08f),
            FromRgb(70, 110, 158, 0.07f),
            FromRgb(76, 194, 180, 0.16f),
            FromRgb(91, 107, 255, 0.07f),
            FromRgb(67, 221, 231, 0.06f),
            FromRgb(153, 235, 247, 0.025f)
        );

    public static CollectionFilterCardPalette CollectionFilterCardGridPalette =>
        new(
            FromRgb(19, 21, 24, 1f),
            FromRgb(116, 127, 213, 0.34f),
            FromRgb(73, 169, 185, 0.12f),
            FromRgb(70, 110, 158, 0.14f),
            FromRgb(76, 194, 180, 0.20f),
            FromRgb(91, 107, 255, 0.11f),
            FromRgb(67, 221, 231, 0.045f),
            FromRgb(153, 235, 247, 0.02f)
        );
    public static Color CollectionFilterTitleText => FromRgb(232, 236, 242, 1f);
    public static Color CollectionChipBackground => FromRgb(28, 30, 34, 1f);
    public static Color CollectionChipBorder => FromRgb(63, 68, 73, 1f);
    public static Color CollectionChipText => FromRgb(252, 252, 252, 1f);
    public static Color CollectionChipHoverBackground => FromRgb(33, 37, 41, 1f);
    public static Color CollectionChipPressedBackground => FromRgb(25, 27, 31, 1f);
    public static Color CollectionChipHoverBorder => FromRgb(75, 80, 86, 1f);
    public static Color CollectionChipSelectedBackground => FromRgb(36, 56, 77, 1f);
    public static Color CollectionChipSelectedBorder => FromRgb(74, 101, 125, 1f);
    public static Color CollectionChipSelectedText => FromRgb(252, 252, 252, 1f);
    public static Color CollectionChipSelectedHoverBackground => FromRgb(42, 66, 91, 1f);
    public static Color CollectionChipSelectedPressedBackground => FromRgb(31, 46, 63, 1f);
    public static Color CollectionChipSelectedHoverBorder => FromRgb(87, 115, 140, 1f);

    public static Color StatusCompletedBackground => Rgba(0.16f, 0.30f, 0.24f, 0.74f);
    public static Color StatusAbandonedBackground => Rgba(0.31f, 0.22f, 0.15f, 0.72f);
    public static Color StatusDefaultBackground => Rgba(0.18f, 0.24f, 0.33f, 0.72f);
    public static Color StatusCompletedText => Rgba(0.82f, 0.98f, 0.90f, 0.90f);
    public static Color StatusAbandonedText => Rgba(0.99f, 0.90f, 0.85f, 0.88f);
    public static Color StatusDefaultText => Rgba(0.84f, 0.92f, 1f, 0.88f);

    public static Color RunRowSelectedBackground => Rgba(0.17f, 0.24f, 0.32f, 0.99f);
    public static Color RunRowSelectedAccent => Rgba(0.46f, 0.70f, 0.92f, 0.94f);
    public static Color RunRowDefaultAccent => Rgba(0.24f, 0.31f, 0.39f, 0.96f);
    public static Color RunRowSelectedBorder => Rgba(0.37f, 0.57f, 0.79f, 0.56f);
    public static Color RunRowDefaultBorder => Rgba(0.28f, 0.35f, 0.45f, 0.40f);

    public static Color BattleRowEliminatedSelectedBackground => Rgba(0.22f, 0.18f, 0.10f, 0.99f);
    public static Color BattleRowWinSelectedBackground => Rgba(0.13f, 0.23f, 0.22f, 0.99f);
    public static Color BattleRowLossSelectedBackground => Rgba(0.24f, 0.18f, 0.16f, 0.99f);
    public static Color BattleRowNeutralSelectedBackground => Rgba(0.18f, 0.24f, 0.31f, 0.99f);
    public static Color BattleRowEliminatedBackground => Rgba(0.18f, 0.14f, 0.08f, 0.98f);
    public static Color BattleRowWinBackground => Rgba(0.10f, 0.15f, 0.16f, 0.98f);
    public static Color BattleRowLossBackground => Rgba(0.15f, 0.13f, 0.15f, 0.98f);
    public static Color BattleRowNeutralBackground => Rgba(0.13f, 0.15f, 0.18f, 0.98f);
    public static Color BattleRowEliminatedAccent => Rgba(0.94f, 0.70f, 0.28f, 0.95f);
    public static Color BattleRowWinAccent => Rgba(0.23f, 0.54f, 0.47f, 0.95f);
    public static Color BattleRowLossAccent => Rgba(0.63f, 0.36f, 0.24f, 0.95f);
    public static Color BattleRowNeutralAccent => Rgba(0.34f, 0.47f, 0.64f, 0.95f);
    public static Color BattleRowEliminatedBorder => Rgba(0.62f, 0.46f, 0.18f, 0.50f);
    public static Color BattleRowWinBorder => Rgba(0.22f, 0.44f, 0.40f, 0.42f);
    public static Color BattleRowLossBorder => Rgba(0.44f, 0.27f, 0.20f, 0.42f);
    public static Color BattleRowNeutralBorder => Rgba(0.24f, 0.31f, 0.41f, 0.42f);
    public static Color BattleDayEliminatedBackground => Rgba(0.24f, 0.18f, 0.08f, 0.98f);
    public static Color BattleDayWinBackground => Rgba(0.13f, 0.28f, 0.23f, 0.98f);
    public static Color BattleDayLossBackground => Rgba(0.33f, 0.20f, 0.15f, 0.98f);
    public static Color BattleDayNeutralBackground => Rgba(0.18f, 0.23f, 0.31f, 0.98f);

    public static Color OutcomeDiamondBackground => Rgba(0.15f, 0.34f, 0.46f, 0.98f);
    public static Color OutcomeDiamondBorder => Rgba(0.42f, 0.78f, 0.98f, 0.42f);
    public static Color OutcomeGoldBackground => Rgba(0.37f, 0.28f, 0.10f, 0.98f);
    public static Color OutcomeGoldBorder => Rgba(0.86f, 0.68f, 0.24f, 0.42f);
    public static Color OutcomeSilverBackground => Rgba(0.31f, 0.34f, 0.40f, 0.98f);
    public static Color OutcomeSilverBorder => Rgba(0.74f, 0.80f, 0.90f, 0.42f);
    public static Color OutcomeBronzeBackground => Rgba(0.36f, 0.22f, 0.15f, 0.98f);
    public static Color OutcomeBronzeBorder => Rgba(0.78f, 0.52f, 0.36f, 0.42f);
    public static Color OutcomeMisfortuneBackground => Rgba(0.25f, 0.18f, 0.18f, 0.98f);
    public static Color OutcomeMisfortuneBorder => Rgba(0.64f, 0.38f, 0.38f, 0.42f);

    public static Color ButtonSelectedBackground => Rgba(0.78f, 0.60f, 0.24f, 0.98f);
    public static Color ButtonSelectedText => Rgba(0.10f, 0.07f, 0.03f, 1f);
    public static Color RunsTabBackground => Rgba(0.25f, 0.30f, 0.37f, 0.98f);
    public static Color GhostFilterBackground => Rgba(0.19f, 0.22f, 0.27f, 0.98f);
    public static Color DeleteConfirmBackground => Rgba(0.60f, 0.19f, 0.16f, 0.98f);
    public static Color DeleteConfirmText => Rgba(1f, 0.94f, 0.92f, 1f);
    public static Color DeleteDisabledBackground => Rgba(0.28f, 0.20f, 0.19f, 0.88f);
    public static Color DeleteDisabledText => Rgba(0.86f, 0.82f, 0.80f, 0.88f);
    public static Color DeleteBackground => Rgba(0.40f, 0.24f, 0.20f, 0.98f);
    public static Color DeleteText => Rgba(1f, 0.93f, 0.90f, 1f);
    public static Color ReplayBackground => Rgba(0.19f, 0.31f, 0.39f, 0.98f);
    public static Color ReplayText => Rgba(0.88f, 0.95f, 1f, 1f);
    public static Color RecordReplayBackground => Rgba(0.45f, 0.19f, 0.24f, 0.98f);
    public static Color RecordReplayText => Rgba(1f, 0.90f, 0.92f, 1f);
    public static Color CloseBackground => Rgba(0.29f, 0.20f, 0.20f, 0.98f);
    public static Color CloseText => Rgba(0.98f, 0.92f, 0.90f, 1f);

    public static Color RankLegendaryBackground => FromRgb(241, 54, 41);
    public static Color RankBronzeBackground => Rgba(0.39f, 0.24f, 0.17f, 0.98f);
    public static Color RankBronzeText => Rgba(0.98f, 0.88f, 0.80f, 1f);
    public static Color RankSilverBackground => Rgba(0.34f, 0.37f, 0.43f, 0.98f);
    public static Color RankSilverText => Rgba(0.94f, 0.97f, 1f, 1f);
    public static Color RankGoldBackground => Rgba(0.41f, 0.31f, 0.12f, 0.98f);
    public static Color RankGoldText => Rgba(0.99f, 0.90f, 0.66f, 1f);
    public static Color RankDiamondBackground => Rgba(0.18f, 0.35f, 0.47f, 0.98f);
    public static Color RankDiamondText => Rgba(0.84f, 0.97f, 1f, 1f);
    public static Color RankDefaultBackground => Rgba(0.24f, 0.28f, 0.36f, 0.98f);

    public static Color SupporterTier1Text => Rgba(0.78f, 0.83f, 0.90f, 0.90f);
    public static Color SupporterTier2Text => Rgba(1f, 0.66f, 0.34f, 1f);
    public static Color SupporterTier3Text => Rgba(0.78f, 0.86f, 1f, 1f);
    public static Color SupporterTier4Text => Rgba(1f, 0.78f, 0.20f, 1f);

    public static Color HeroUnknownBackground => Rgba(0.20f, 0.29f, 0.38f, 0.95f);
    public static Color HeroVanessaBackground => FromRgb(192, 33, 33);
    public static Color HeroPygmalienBackground => FromRgb(39, 103, 192);
    public static Color HeroDooleyBackground => FromRgb(225, 154, 8);
    public static Color HeroMakBackground => FromRgb(190, 230, 91);
    public static Color HeroJulesBackground => FromRgb(180, 52, 236);
    public static Color HeroKarnokBackground => FromRgb(59, 136, 156);
    public static Color HeroStelleBackground => FromRgb(255, 235, 24);
    public static Color HeroTheDragonsBackground => FromRgb(45, 210, 208);
    public static Color HeroDefaultBackground => FromRgb(57, 73, 97);
    public static Color HeroDarkText => Rgba(0.10f, 0.12f, 0.15f, 1f);

    public static Color CombatBarBackground => Rgba(0.06f, 0.07f, 0.09f, 0.90f);
    public static Color CombatBarGlow => Rgba(0.28f, 0.22f, 0.12f, 0.10f);

    public static Color ButtonBorderFor(Color background) =>
        Rgba(
            Mathf.Clamp01(background.r + 0.08f),
            Mathf.Clamp01(background.g + 0.08f),
            Mathf.Clamp01(background.b + 0.08f),
            0.58f
        );

    public static Color ButtonHoverBackgroundFor(Color background) =>
        Mix(background, Color.white, 0.12f, Mathf.Clamp01(background.a + 0.02f));

    public static Color ButtonPressedBackgroundFor(Color background) =>
        Mix(background, Color.black, 0.10f, background.a);

    public static Color ButtonHoverBorderFor(Color background) =>
        Rgba(
            Mathf.Clamp01(background.r + 0.18f),
            Mathf.Clamp01(background.g + 0.18f),
            Mathf.Clamp01(background.b + 0.18f),
            0.78f
        );

    public static Color RowHoverBackgroundFor(Color background) =>
        Mix(background, Color.white, 0.07f, background.a);

    public static Color RowPressedBackgroundFor(Color background) =>
        Mix(background, Color.black, 0.06f, background.a);

    public static Color RowHoverBorderFor(Color border) =>
        Mix(border, HistoryLevelAccent, 0.32f, Mathf.Clamp01(border.a + 0.18f));

    public static Color RowPressedBorderFor(Color border) =>
        Mix(border, ButtonSelectedBackground, 0.36f, Mathf.Clamp01(border.a + 0.14f));

    public static Color InfoChipBackground(Color accent) =>
        Rgba(
            Mathf.Lerp(0.14f, accent.r, 0.10f),
            Mathf.Lerp(0.16f, accent.g, 0.10f),
            Mathf.Lerp(0.20f, accent.b, 0.10f),
            0.98f
        );

    public static Color InfoChipBorder(Color accent) => Rgba(accent.r, accent.g, accent.b, 0.95f);

    // Collection Panel display-case grid. Slot backgrounds are a very weak translucent fill so
    // the fixed column order reads without competing with the native card frames. Hover is
    // communicated by the card's scale and shadow, not a blue frame behind it.
    public static Color CollectionSlotBackground => Rgba(1f, 1f, 1f, 0.02f);
    public static Color CollectionSlotHover => Rgba(1f, 1f, 1f, 0f);

    public static Color WithAlpha(Color color, float alpha) =>
        Rgba(color.r, color.g, color.b, alpha);

    public static Color FromRgb(int r, int g, int b, float alpha = 0.98f) =>
        Rgba(r / 255f, g / 255f, b / 255f, alpha);

    private static Color Mix(Color from, Color to, float amount, float alpha) =>
        Rgba(
            Mathf.Lerp(from.r, to.r, amount),
            Mathf.Lerp(from.g, to.g, amount),
            Mathf.Lerp(from.b, to.b, amount),
            alpha
        );

    private static Color Rgba(float r, float g, float b, float a) => new(r, g, b, a);
}
