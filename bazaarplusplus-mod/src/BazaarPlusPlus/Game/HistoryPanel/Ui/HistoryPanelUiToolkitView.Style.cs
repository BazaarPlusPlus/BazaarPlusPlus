#nullable enable
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelUiToolkitView
{
    private static VisualElement CreateSectionPanel(float? width)
    {
        var panel = new VisualElement();
        panel.style.backgroundColor = Colors.HistorySectionBackground;
        UiStyle.Radius(panel.style, Radii.Md);
        UiStyle.Padding(panel.style, UiSpacing.Xxl);
        panel.style.flexDirection = FlexDirection.Column;
        panel.style.overflow = Overflow.Hidden;
        if (width.HasValue)
            panel.style.width = width.Value;
        return panel;
    }

    private static VisualElement CreateListFrame(VisualElement content)
    {
        var frame = new VisualElement();
        frame.style.flexGrow = 1f;
        frame.style.flexShrink = 1f;
        frame.style.minHeight = UiSpacing.None;
        frame.style.marginTop = UiSpacing.Lg;
        UiStyle.Padding(frame.style, UiSpacing.Sm);
        frame.style.backgroundColor = Colors.HistoryListFrameBackground;
        UiStyle.Radius(frame.style, Radii.Md);
        UiStyle.Border(frame.style, Borders.Thin, Colors.HistoryListFrameBorder);
        frame.style.overflow = Overflow.Hidden;
        content.style.marginTop = UiSpacing.None;
        frame.Add(content);
        return frame;
    }

    private static Label CreateSectionTitle(string text)
    {
        var label = CreateLabel(
            Sizes.FontSectionTitle,
            FontStyle.Bold,
            Colors.HistorySectionTitleText
        );
        label.text = text.ToUpperInvariant();
        UiStyle.FixedHeight(label.style, Sizes.SectionTitleHeight);
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        return label;
    }

    private static Label CreateChip()
    {
        var chip = CreateLabel(Sizes.FontSmall, FontStyle.Bold, Colors.HistoryChipText);
        chip.style.backgroundColor = Colors.HistoryChipBackground;
        chip.style.flexBasis = 0f;
        chip.style.flexGrow = 1f;
        chip.style.flexShrink = 1f;
        chip.style.minWidth = 0f;
        chip.style.height = Sizes.ChipHeight;
        chip.style.whiteSpace = WhiteSpace.NoWrap;
        chip.style.overflow = Overflow.Hidden;
        UiStyle.HorizontalPadding(chip.style, UiSpacing.Md);
        chip.style.unityTextAlign = TextAnchor.MiddleCenter;
        UiStyle.Radius(chip.style, Radii.Md);
        return chip;
    }

    private static Label CreateLabel(int fontSize, FontStyle fontStyle, Color color)
    {
        var label = new Label();
        label.style.fontSize = fontSize;
        label.style.unityFontStyleAndWeight = fontStyle;
        label.style.color = color;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        return label;
    }

    private static Button CreateButton(
        string text,
        Action onClick,
        float width,
        float height,
        bool fixedWidth = true
    )
    {
        var button = new Button(() => onClick()) { text = text };
        if (fixedWidth)
            UiStyle.FixedWidth(button.style, width);
        else
        {
            button.style.flexBasis = 0f;
            button.style.minWidth = 0f;
        }
        button.style.height = height;
        button.style.flexGrow = fixedWidth ? 0f : 1f;
        button.style.flexShrink = fixedWidth ? 0f : 1f;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        UiStyle.Padding(button.style, UiSpacing.None);
        button.style.backgroundColor = Colors.HistoryButtonBackground;
        button.style.color = Colors.White;
        UiStyle.Border(button.style, Borders.Thin, Colors.HistoryButtonBorder);
        UiStyle.Radius(button.style, Radii.Md);

        var textElement = button.Q<TextElement>();
        if (textElement != null)
        {
            textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            textElement.style.flexGrow = 1f;
            textElement.style.flexShrink = 1f;
            textElement.style.minWidth = 0f;
            textElement.style.whiteSpace = WhiteSpace.NoWrap;
            textElement.style.overflow = Overflow.Hidden;
        }
        button.tooltip = text;
        button.style.overflow = Overflow.Hidden;
        UiHover.ApplyButtonPalette(button, Colors.HistoryButtonBackground, Colors.White);
        return button;
    }

    private static Button CreateRailButton(string text, Action onClick)
    {
        var button = new Button(() => onClick()) { text = text };
        button.style.width = Length.Percent(100f);
        button.style.height = Sizes.ButtonFooterHeight;
        button.style.flexGrow = 0f;
        button.style.flexShrink = 0f;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.justifyContent = Justify.Center;
        button.style.alignItems = Align.Center;
        UiStyle.Padding(button.style, UiSpacing.None);
        button.style.backgroundColor = Colors.HistoryButtonBackground;
        button.style.color = Colors.White;
        UiStyle.Border(button.style, Borders.Thin, Colors.HistoryButtonBorder);
        UiStyle.Radius(button.style, Radii.Md);

        var textElement = button.Q<TextElement>();
        if (textElement != null)
        {
            textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            textElement.style.flexGrow = 1f;
            textElement.style.flexShrink = 1f;
            textElement.style.minWidth = 0f;
            textElement.style.whiteSpace = WhiteSpace.NoWrap;
            textElement.style.overflow = Overflow.Hidden;
        }
        button.tooltip = text;
        button.style.overflow = Overflow.Hidden;
        UiHover.ApplyButtonPalette(button, Colors.HistoryButtonBackground, Colors.White);
        return button;
    }

    private static void UseActionRowRatio(Button button, float flexGrow)
    {
        button.style.width = StyleKeyword.Auto;
        button.style.flexBasis = 0f;
        button.style.flexGrow = flexGrow;
        button.style.flexShrink = 1f;
        button.style.minWidth = 0f;
    }

    private static void StyleButton(Button button, Color background, Color textColor)
    {
        UiHover.ApplyButtonPalette(button, background, textColor);
    }

    private NativeGameTypography.PanelScope GetTypography() =>
        _typography
        ?? throw new InvalidOperationException("Native game typography is unavailable.");

    // Styles one segmented code cell so it reads as a single clean box with a centered glyph in the
    // game UI font. The game's USS gives the inner 'unity-text-input' explicit font/align/chrome that
    // beats the inherited cascade, so de-chrome the root AND re-apply the text props on the queried
    // inner element (which then re-inherits to its 'unity-text-element' child). Call after the field
    // is attached so Q(...) resolves a non-null inner element.
    private void StyleCodeCell(TextField cell, int fontSize, Color textColor)
    {
        cell.style.marginLeft = 0f;
        cell.style.marginRight = 0f;
        cell.style.marginTop = 0f;
        cell.style.marginBottom = 0f;
        cell.style.paddingLeft = 0f;
        cell.style.paddingRight = 0f;
        cell.style.paddingTop = 0f;
        cell.style.paddingBottom = 0f;

        GetTypography().Apply(cell);
        cell.style.unityTextAlign = TextAnchor.MiddleCenter;
        cell.style.fontSize = fontSize;
        cell.style.color = textColor;

        var input = cell.Q(TextField.textInputUssName);
        if (input == null)
            return;

        input.style.flexGrow = 1f; // fill the cell so centered text is centered within the box
        input.style.backgroundColor = Color.clear;
        input.style.borderLeftWidth = 0f;
        input.style.borderRightWidth = 0f;
        input.style.borderTopWidth = 0f;
        input.style.borderBottomWidth = 0f;
        input.style.marginLeft = 0f;
        input.style.marginRight = 0f;
        input.style.marginTop = 0f;
        input.style.marginBottom = 0f;
        input.style.paddingLeft = 0f;
        input.style.paddingRight = 0f;
        input.style.paddingTop = 0f;
        input.style.paddingBottom = 0f;

        GetTypography().Apply(input);
        input.style.unityTextAlign = TextAnchor.MiddleCenter;
        input.style.fontSize = fontSize;
        input.style.color = textColor;

        // The glyphs render in the inner TextElement, which carries an explicit alignment from the
        // game USS that beats the inherited value — set alignment directly there and let it fill the
        // input so MiddleCenter centers vertically as well as horizontally.
        var glyphs = input.Q<TextElement>();
        if (glyphs == null)
            return;

        glyphs.style.flexGrow = 1f;
        GetTypography().Apply(glyphs);
        glyphs.style.unityTextAlign = TextAnchor.MiddleCenter;
        glyphs.style.fontSize = fontSize;
        glyphs.style.color = textColor;
    }

    private static VisualElement CreateSpacer()
    {
        var spacer = new VisualElement();
        spacer.style.flexGrow = 1f;
        return spacer;
    }

    private static VisualElement CreateRowShell()
    {
        var row = new VisualElement();
        row.style.height = Length.Percent(100);
        row.style.marginBottom = UiSpacing.Sm;
        row.style.backgroundColor = Colors.HistoryRowBackground;
        UiStyle.Radius(row.style, Radii.Row);
        row.style.flexDirection = FlexDirection.Row;
        row.style.overflow = Overflow.Hidden;
        return row;
    }

    private static VisualElement CreateAccentBar()
    {
        var accent = new VisualElement();
        accent.style.width = Sizes.RowAccentWidth;
        accent.style.flexShrink = 0f;
        return accent;
    }

    private static VisualElement CreateRowContent()
    {
        var content = new VisualElement();
        content.style.flexGrow = 1f;
        content.style.flexShrink = 1f;
        content.style.minWidth = 0f;
        UiStyle.Padding(content.style, UiSpacing.Xl, UiSpacing.RowVerticalPadding);
        content.style.flexDirection = FlexDirection.Column;
        return content;
    }

    private static VisualElement CreateRowTopRow()
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        return row;
    }

    private static Label CreateRowCornerLabel(VisualElement row, int fontSize)
    {
        var label = CreateLabel(fontSize, FontStyle.Normal, Colors.HistoryRowCornerText);
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.flexShrink = 0f;
        label.style.marginLeft = UiSpacing.Md;
        row.Add(label);
        return label;
    }

    private static Label CreateInlineText(VisualElement row, int fontSize, Color color)
    {
        var label = CreateLabel(fontSize, FontStyle.Normal, color);
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.flexShrink = 1f;
        label.style.minWidth = 0f;
        label.style.overflow = Overflow.Hidden;
        row.Add(label);
        return label;
    }
}
