#nullable enable
namespace BazaarPlusPlus.Game.LiveBuildPanel.Ui;

// Layout units are the owning canvas's units, not physical screen pixels.
internal readonly record struct LiveBuildPanelLayout(float Width, float Height)
{
    internal const float HeaderHeight = 24;
    internal const float RowTitleHeight = 28;
    internal const float RowGap = 12;

    // Two stacked lines per metric cell: an 11px label over a 16px value. Every unit
    // here comes straight out of BoardHeight, and the carpet is height-constrained.
    internal const float MetricsHeight = 34;
    internal const int MetricCells = 4;
    internal const float FooterHeight = 28;
    internal const float SidebarWidth = 290;
    internal const float SidebarGap = 28;
    internal const float BadgeHeight = 18;
    internal const float BoardFooterHeight = BadgeHeight + 5;
    internal const float BoardTopInset = 3;
    internal float Padding => Math.Min(28, Width * .025f);
    internal float BoardLeft => Padding;
    internal float BoardWidth => Math.Max(1, Width - Padding * 2 - SidebarWidth - SidebarGap);

    internal float SidebarLeft(float carpetLeft, float carpetWidth) =>
        carpetLeft + carpetWidth + SidebarGap;

    internal float BoardHeight =>
        Math.Max(
            1,
            (Height - HeaderHeight - FooterHeight - RowTitleHeight * 4 - RowGap * 3 - MetricsHeight)
                / 4
        );

    internal float TitleTop(int row) =>
        HeaderHeight
        + row * (RowTitleHeight + BoardHeight + RowGap)
        + (row > 0 ? MetricsHeight : 0);

    internal float BoardTop(int row) => TitleTop(row) + RowTitleHeight;

    internal float MetricsTop => BoardTop(0) + BoardHeight;
    internal float FooterTop => Height - FooterHeight;
}
