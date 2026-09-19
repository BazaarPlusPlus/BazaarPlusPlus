using Xunit;

namespace EncounterTooltip.Tests;

public sealed class TooltipMarkupTests
{
    [Fact]
    public void Prose_renderer_distinguishes_paragraphs_lists_and_nested_lists()
    {
        var text = TooltipMarkup.Render(
            new TooltipMarkup.Block[]
            {
                new TooltipMarkup.Paragraph("Intro"),
                new TooltipMarkup.ListBlock(
                    "Choose one:",
                    new[]
                    {
                        new TooltipMarkup.ListItem(
                            "first",
                            new[] { new TooltipMarkup.ListItem("detail") }
                        ),
                        new TooltipMarkup.ListItem("second"),
                    }
                ),
            }
        );

        Assert.Equal(
            "<line-height=1.4em>Intro"
                + "<line-height=1.9em>\n"
                + "<line-height=1.4em>Choose one:"
                + "<line-height=1.9em>\n<line-height=1.4em>· <indent=1em>first</indent>"
                + "<line-height=1.65em>\n<line-height=1.4em><indent=2.2em>- detail</indent>"
                + "<line-height=1.9em>\n<line-height=1.4em>· <indent=1em>second</indent>",
            text
        );
    }

    [Fact]
    public void Font_scaling_wraps_the_complete_semantic_element()
    {
        var text = TooltipMarkup.Render(
            new TooltipMarkup.Block[]
            {
                new TooltipMarkup.Paragraph("Full size"),
                new TooltipMarkup.ListBlock(
                    null,
                    new[] { new TooltipMarkup.ListItem("dimmed") },
                    fontSizePercent: 85
                ),
            }
        );

        Assert.Equal(
            "<line-height=1.4em>Full size"
                + "<line-height=1.9em>\n"
                + "<size=85%><line-height=1.4em>· <indent=1em>dimmed</indent></size>",
            text
        );
    }

    [Fact]
    public void Unknown_block_type_fails_instead_of_rendering_an_empty_block()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TooltipMarkup.Render(new TooltipMarkup.Block[] { new UnknownBlock() })
        );
    }

    [Fact]
    public void Paragraph_gap_uses_the_preceding_elements_font_size()
    {
        var text = TooltipMarkup.Render(
            new TooltipMarkup.Block[]
            {
                new TooltipMarkup.Paragraph("Small", fontSizePercent: 85),
                new TooltipMarkup.Paragraph("Full"),
            }
        );

        Assert.Equal(
            "<size=85%><line-height=1.4em>Small<line-height=1.9em>\n</size>"
                + "<line-height=1.4em>Full",
            text
        );
    }

    [Fact]
    public void List_item_gap_uses_the_preceding_items_font_size()
    {
        var text = TooltipMarkup.Render(
            new TooltipMarkup.Block[]
            {
                new TooltipMarkup.ListBlock(
                    null,
                    new[]
                    {
                        new TooltipMarkup.ListItem("Small", fontSizePercent: 85),
                        new TooltipMarkup.ListItem("Full"),
                    }
                ),
            }
        );

        Assert.Equal(
            "<size=85%><line-height=1.4em>· <indent=1em>Small</indent>"
                + "<line-height=1.9em>\n</size>"
                + "<line-height=1.4em>· <indent=1em>Full</indent>",
            text
        );
    }

    [Fact]
    public void Native_colorizer_line_height_does_not_override_prose_rhythm()
    {
        Assert.Equal(
            "Gain <color=green>Health</color>",
            TooltipMarkup.NormalizeInlineFragment(
                "<line-height=1.6em>Gain <color=green>Health</color></line-height>"
            )
        );
    }

    private sealed class UnknownBlock : TooltipMarkup.Block
    {
        public UnknownBlock()
            : base(fontSizePercent: 100) { }
    }
}
