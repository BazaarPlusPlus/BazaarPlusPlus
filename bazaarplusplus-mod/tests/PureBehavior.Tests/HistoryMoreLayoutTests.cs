using BazaarPlusPlus.Game.HistoryPanel.Ui;
using Xunit;

public sealed class HistoryMoreLayoutTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Visible_rows_do_not_overlap_even_with_wrapped_messages(
        bool actions,
        bool form,
        bool messages
    )
    {
        var layout = new HistoryMoreLayout(
            messages ? 72 : 0,
            48,
            actions ? 72 : 0,
            actions,
            form,
            48,
            messages ? 72 : 0,
            alreadyLinkedVisible: actions
        );
        var rows = new List<HistoryRect> { layout.Title, layout.Database };
        if (messages)
            rows.Add(layout.Status);
        rows.Add(layout.AccountTitle);
        rows.Add(layout.AccountStatus);
        if (actions)
        {
            rows.Add(layout.Guidance);
            foreach (
                var row in new[] { layout.AccountTitle, layout.AccountStatus, layout.Guidance }
            )
            {
                Assert.False(row.Anchors.Overlaps(layout.Toggle.Anchors));
                Assert.False(row.Anchors.Overlaps(layout.AlreadyLinked.Anchors));
            }
        }
        if (form)
        {
            rows.Add(layout.Hint);
            rows.Add(layout.Input);
            if (messages)
                rows.Add(layout.Banner);
        }
        for (var i = 0; i < rows.Count; i++)
        {
            Assert.InRange(rows[i].Y, 0, 1);
            Assert.InRange(rows[i].Bottom, 0, 1);
            if (i > 0)
                Assert.True(rows[i - 1].Bottom <= rows[i].Y);
        }
        foreach (
            var action in new[]
            {
                layout.Close,
                layout.Health,
                layout.Toggle,
                layout.AlreadyLinked,
                layout.Submit,
            }
        )
            Assert.Equal(HistoryMoreLayout.ControlHeight, action.Height * layout.Height, 3);
        Assert.False(layout.Toggle.Anchors.Overlaps(layout.AlreadyLinked.Anchors));
        Assert.False(layout.Input.Anchors.Overlaps(layout.Submit.Anchors));
        Assert.Equal(layout.Health.X, layout.Toggle.X);
        Assert.Equal(layout.Health.X, layout.AlreadyLinked.X);
        Assert.Equal(layout.Health.X, layout.Submit.X);
    }

    [Fact]
    public void Empty_feedback_collapses_while_expansion_keeps_control_sizes()
    {
        var compact = new HistoryMoreLayout(0, 24, 48, true, false, 24, 0);
        var expanded = new HistoryMoreLayout(0, 24, 48, true, true, 24, 0);
        var feedback = new HistoryMoreLayout(60, 24, 48, true, true, 24, 60);
        Assert.True(compact.Height < expanded.Height);
        Assert.True(expanded.Height < feedback.Height);
        Assert.Equal(compact.Toggle.Y * compact.Height, expanded.Toggle.Y * expanded.Height, 3);
        var scale = feedback.FitScale(900, 400);
        Assert.InRange(feedback.Height * scale, 0, 400);
        Assert.InRange(HistoryMoreLayout.Width * scale, 0, 900);
    }
}
