using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.GameInterop.MonsterBoardPreview;
using Xunit;

public sealed class LiveBuildPanelLayoutTests
{
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3456, 2168)]
    [InlineData(3440, 1440)]
    [InlineData(5120, 1440)]
    [InlineData(3840, 1080)]
    [InlineData(1600, 1200)]
    public void Four_equal_boards_and_their_text_fit_the_scaled_canvas(float width, float height)
    {
        // Match the panel's height-scaled canvas, including ultrawide displays.
        var canvasScale = height / 1000;
        var panel = new LiveBuildPanelLayout(width / canvasScale, height / canvasScale);
        var previousBottom = LiveBuildPanelLayout.HeaderHeight;
        for (var row = 0; row < 4; row++)
        {
            Assert.True(panel.TitleTop(row) >= previousBottom - .001f);
            Assert.True(panel.BoardTop(row) >= panel.TitleTop(row) + 28);
            previousBottom = panel.BoardTop(row) + panel.BoardHeight;
            if (row == 0)
            {
                Assert.Equal(previousBottom, panel.MetricsTop);
                previousBottom += LiveBuildPanelLayout.MetricsHeight;
            }
        }
        Assert.InRange(previousBottom, panel.FooterTop - .001f, panel.FooterTop + .001f);
        Assert.True(panel.FooterTop + LiveBuildPanelLayout.FooterHeight <= panel.Height + .001f);

        var board = NativeMonsterBoardLayout.Calculate(
            panel.BoardWidth * canvasScale,
            panel.BoardHeight * canvasScale,
            2600,
            550,
            0,
            0,
            itemsOnly: true,
            itemFooterHeight: LiveBuildPanelLayout.BoardFooterHeight * canvasScale,
            itemTopInset: LiveBuildPanelLayout.BoardTopInset * canvasScale
        );
        var carpetHeight = 550 * board.BoardScale / canvasScale;
        Assert.True(carpetHeight >= 125);
        var contentWidth = 2600 * board.BoardScale / canvasScale;
        Assert.True(contentWidth <= panel.BoardWidth + .001f);
        var carpetLeft = panel.BoardLeft + (panel.BoardWidth - contentWidth) / 2;
        var sidebarLeft = panel.SidebarLeft(carpetLeft, contentWidth);
        Assert.True(carpetLeft >= panel.Padding - .001f);
        Assert.True(carpetLeft + contentWidth + 12 < sidebarLeft);
        Assert.True(
            sidebarLeft + LiveBuildPanelLayout.SidebarWidth <= panel.Width - panel.Padding + .001f
        );
        Assert.Equal(
            panel.Width / 2,
            (carpetLeft + sidebarLeft + LiveBuildPanelLayout.SidebarWidth) / 2,
            precision: 3
        );
        Assert.True(
            carpetHeight + LiveBuildPanelLayout.BadgeHeight + 8 <= panel.BoardHeight + .001f
        );

        // Each metric cell must stay wide enough for an 11px label without ellipsizing.
        var cellWidth = contentWidth / LiveBuildPanelLayout.MetricCells;
        Assert.True(cellWidth >= 120);
    }

    [Fact]
    public void Metric_cell_stacks_an_11px_label_over_a_16px_value()
    {
        // Two stacked lines, so the label must not eat the value's half of the row.
        Assert.True(LiveBuildPanelLayout.MetricsHeight * .42f >= 13);
        Assert.True(LiveBuildPanelLayout.MetricsHeight * .58f >= 18);
    }
}
