#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// Lays chips, headers and notes top to bottom in a fixed column grid under one parent. Columns
/// are fractions of the parent's width, so nothing waits for a layout pass to measure it. Sizes
/// come from the shared touch frame (<see cref="TouchChrome"/>): a chip is one touch target tall
/// with button-sized text that shrinks to fit, never under body size, and a chip whose label
/// would not fit whole at body size widens across more columns (<see cref="TouchChrome.ChipSpan"/>);
/// a note is as tall as its wrapped text.
/// </summary>
internal sealed class TouchChipFlow
{
    private readonly RectTransform _parent;
    private readonly TouchNativeSkin _skin;
    private readonly TMP_FontAsset _font;
    private readonly TouchChrome _chrome;
    private readonly int _columns;
    private readonly float _headerHeight;
    private int _column;
    private float _y;
    private float _rowHeight;
    private TextMeshProUGUI? _probe;

    public TouchChipFlow(
        RectTransform parent,
        TMP_FontAsset font,
        TouchChrome chrome,
        int columns,
        float headerHeight,
        TouchNativeSkin skin
    )
    {
        _parent = parent;
        _skin = skin;
        _font = font;
        _chrome = chrome;
        _columns = columns;
        _headerHeight = headerHeight;
    }

    private float ChipHeight => _chrome.Target;

    private float Gap => _chrome.Gap;

    /// <summary>The height laid out so far, the next row included when it has chips.</summary>
    public float Height => _y + (_column > 0 ? Math.Max(ChipHeight, _rowHeight) : 0f);

    public void Chip(string label, bool selected, Action onTap, int span = 1)
    {
        span = TouchChrome.ChipSpan(_parent.rect.width, _columns, span, Gap, ChipWidth(label));
        if (_column + span > _columns)
            Break();
        var rect = TouchUiKit.Rect(
            "Chip",
            _parent,
            new((float)_column / _columns, 1f),
            new((float)(_column + span) / _columns, 1f),
            new(_column == 0 ? 0f : Gap * 0.5f, -_y - ChipHeight),
            new(_column + span == _columns ? 0f : -Gap * 0.5f, -_y)
        );
        var (_, background, text) = _skin.Button(
            rect,
            _font,
            label,
            _chrome.ButtonFont,
            _chrome.LabelInset,
            onTap
        );
        _skin.Select(background, selected);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.enableAutoSizing = true;
        text.fontSizeMin = _chrome.BodyFont;
        text.fontSizeMax = _chrome.ButtonFont;
        text.overflowMode = TextOverflowModes.Ellipsis;
        _column += span;
    }

    public void Identity(
        string label,
        bool selected,
        Action onTap,
        Func<Task<Sprite?>>? portrait,
        int span = 3,
        bool? expanded = null,
        CardHero? hero = null
    )
    {
        if (_column + span > _columns)
            Break();
        var compact = span * 2 <= _columns;
        var height = _chrome.Metrics.Dp(compact ? 88f : 64f);
        var rect = TouchUiKit.Rect(
            "Identity",
            _parent,
            new((float)_column / _columns, 1f),
            new((float)(_column + span) / _columns, 1f),
            new(_column == 0 ? 0f : Gap * 0.5f, -_y - height),
            new(_column + span == _columns ? 0f : -Gap * 0.5f, -_y)
        );
        var (_, background, text) = _skin.Button(
            rect,
            _font,
            label,
            _chrome.ButtonFont,
            _chrome.LabelInset,
            onTap
        );
        _skin.Select(background, selected);
        text.alignment = compact ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.enableAutoSizing = true;
        text.fontSizeMin = _chrome.BodyFont;
        text.fontSizeMax = compact ? _chrome.BodyFont : _chrome.ButtonFont;
        text.overflowMode = TextOverflowModes.Ellipsis;
        if (compact)
        {
            text.rectTransform.anchorMax = new Vector2(1f, 0f);
            text.rectTransform.offsetMin = new Vector2(_chrome.Padding, _chrome.Padding);
            text.rectTransform.offsetMax = new Vector2(-_chrome.Padding, _chrome.Metrics.Dp(36f));
        }
        else
        {
            text.rectTransform.offsetMin = new Vector2(_chrome.Metrics.Dp(56f), _chrome.Padding);
            text.rectTransform.offsetMax = new Vector2(
                -(expanded.HasValue ? _chrome.Metrics.Dp(28f) : _chrome.Padding),
                -_chrome.Padding
            );
        }
        var portraitY = compact ? _chrome.Metrics.Dp(16f) : 0f;
        var icon = TouchUiKit.Fixed(
            "Portrait",
            rect,
            new(compact ? 0.5f : 0f, 0.5f),
            new(compact ? 0f : _chrome.Padding, portraitY),
            Vector2.one * _chrome.Metrics.Dp(40f)
        );
        new TouchPortraitView(icon, _skin, _chrome.Metrics).Bind(portrait, hero);
        if (expanded is { } open)
        {
            var arrow = TouchUiKit.Fixed(
                "Chevron",
                rect,
                new(1f, 0.5f),
                new(-_chrome.Padding, portraitY),
                Vector2.one * _chrome.Metrics.Dp(10f)
            );
            foreach (var side in new[] { -1f, 1f })
            {
                var stroke = TouchUiKit.Fixed(
                    "Stroke",
                    arrow,
                    new(0.5f, 0.5f),
                    new(side * _chrome.Metrics.Dp(2f), 0f),
                    new(_chrome.Metrics.Dp(6f), _chrome.Metrics.Dp(1.5f))
                );
                stroke.localRotation = Quaternion.Euler(0f, 0f, side * (open ? -45f : 45f));
                TouchUiKit.Fill(stroke, TouchUiKit.MutedTextColor, false);
            }
        }
        _column += span;
        _rowHeight = Math.Max(_rowHeight, height);
    }

    public void Toggle(string label, bool value, Action<bool> onChanged)
    {
        Break();
        var rect = TouchUiKit.Rect(
            "Toggle",
            _parent,
            new(0f, 1f),
            new(1f, 1f),
            new(0f, -_y - _chrome.Metrics.Dp(56f)),
            new(0f, -_y)
        );
        _skin.Toggle(
            rect,
            _font,
            label,
            _chrome.ButtonFont,
            _chrome.Metrics.Dp(32f),
            value,
            onChanged
        );
        _y += _chrome.Metrics.Dp(56f) + Gap;
    }

    // The label at body size, the smallest autosize goes, plus the button's padding.
    private float ChipWidth(string label)
    {
        if (_probe == null)
        {
            _probe = TouchUiKit.Text(
                TouchUiKit.Fixed("ChipProbe", _parent, Vector2.zero, Vector2.zero, Vector2.zero),
                _font,
                "",
                _chrome.BodyFont,
                TextAlignmentOptions.Left,
                Color.clear
            );
            _probe.textWrappingMode = TextWrappingModes.NoWrap;
        }
        return _probe.GetPreferredValues(label).x + 2f * _chrome.LabelInset;
    }

    public void Header(string text) =>
        Line(
            text,
            _headerHeight,
            _chrome.ButtonFont,
            TouchUiKit.TextColor,
            TextAlignmentOptions.BottomLeft
        );

    /// <summary>Wrapped text as tall as it needs, under the chips so far.</summary>
    public TextMeshProUGUI Note(string text, Color color, float? fontSize = null)
    {
        Break();
        var rect = TouchUiKit.Rect(
            "Note",
            _parent,
            new(0f, 1f),
            new(1f, 1f),
            new(0f, -_y),
            new(0f, -_y)
        );
        var label = TouchUiKit.Text(
            rect,
            _font,
            text,
            fontSize ?? _chrome.BodyFont,
            TextAlignmentOptions.TopLeft,
            color
        );
        TouchKeywordSprites.Apply(label);
        var width = Math.Max(1f, _parent.rect.width);
        var height = Math.Max(
            fontSize ?? _chrome.BodyFont,
            label.GetPreferredValues(text, width, 0f).y
        );
        rect.offsetMin = new Vector2(0f, -_y - height);
        _y += height + Gap;
        return label;
    }

    public void Divider()
    {
        Break();
        _y += Gap * 0.5f;
        var line = TouchUiKit.Rect(
            "Divider",
            _parent,
            new(0f, 1f),
            new(1f, 1f),
            new(0f, -_y - _chrome.Metrics.Dp(1f)),
            new(0f, -_y)
        );
        TouchUiKit.Fill(line, InGameTheme.RuleFaint, false);
        _y += Gap;
    }

    public void Attributes(IReadOnlyList<(string Label, string Value, float Weight)> attributes)
    {
        Break();
        if (attributes.Count == 0)
            return;
        var availableWidth = Math.Max(1f, _parent.rect.width - Gap * (attributes.Count - 1));
        var totalWeight = attributes.Sum(entry => entry.Weight);
        var rowHeight = 0f;
        var x = 0f;
        foreach (var entry in attributes)
        {
            var width = availableWidth * entry.Weight / totalWeight;
            var text = $"<color=#B8A98F><size=60%>{entry.Label}</size></color>\n{entry.Value}";
            var rect = TouchUiKit.Rect(
                "Attribute",
                _parent,
                new(0f, 1f),
                new(0f, 1f),
                new(x, -_y),
                new(x + width, -_y)
            );
            var label = TouchUiKit.Text(
                rect,
                _font,
                text,
                _chrome.NameFont,
                TextAlignmentOptions.TopLeft,
                TouchUiKit.TextColor
            );
            TouchKeywordSprites.Apply(label);
            var height = label.GetPreferredValues(text, Math.Max(1f, width), 0f).y;
            rect.offsetMin = new(x, -_y - height);
            rowHeight = Math.Max(rowHeight, height);
            x += width + Gap;
        }
        _y += rowHeight + Gap;
    }

    public void Definitions(IReadOnlyList<(string Label, string Text)> entries)
    {
        Break();
        var width = Math.Max(1f, _parent.rect.width);
        var nameWidth = Math.Min(
            width * 0.32f,
            entries
                .Select(entry => ChipWidth(entry.Label) - 2f * _chrome.LabelInset)
                .DefaultIfEmpty(0f)
                .Max()
        );
        foreach (var (name, text) in entries)
        {
            var labelRect = TouchUiKit.Rect(
                "EnchantmentName",
                _parent,
                new(0f, 1f),
                new(0f, 1f),
                new(0f, -_y),
                new(nameWidth, -_y)
            );
            var effectRect = TouchUiKit.Rect(
                "EnchantmentEffect",
                _parent,
                new(0f, 1f),
                new(1f, 1f),
                new(nameWidth + Gap, -_y),
                new(0f, -_y)
            );
            var label = TouchUiKit.Text(
                labelRect,
                _font,
                name,
                _chrome.BodyFont,
                TextAlignmentOptions.TopLeft,
                TouchUiKit.TextColor
            );
            var effect = TouchUiKit.Text(
                effectRect,
                _font,
                text,
                _chrome.BodyFont,
                TextAlignmentOptions.TopLeft,
                TouchUiKit.TextColor
            );
            TouchKeywordSprites.Apply(effect);
            var labelHeight = label.GetPreferredValues(name, nameWidth, 0f).y;
            var effectHeight = effect
                .GetPreferredValues(text, Math.Max(1f, width - nameWidth - Gap), 0f)
                .y;
            var height = Math.Max(labelHeight, effectHeight);
            labelRect.offsetMin = new(0f, -_y - height);
            effectRect.offsetMin = new(nameWidth + Gap, -_y - height);
            label.ForceMeshUpdate();
            effect.ForceMeshUpdate();
            // Native effect icons have taller ascenders; align the first text baselines.
            var delta =
                label.textInfo.lineCount > 0 && effect.textInfo.lineCount > 0
                    ? label.textInfo.lineInfo[0].baseline - effect.textInfo.lineInfo[0].baseline
                    : 0f;
            var labelOffset = Math.Max(0f, delta);
            var effectOffset = Math.Max(0f, -delta);
            labelRect.anchoredPosition += new Vector2(0f, -labelOffset);
            effectRect.anchoredPosition += new Vector2(0f, -effectOffset);
            _y += Math.Max(labelHeight + labelOffset, effectHeight + effectOffset) + Gap;
        }
    }

    /// <summary>Content-sized, noninteractive native tags; wrap without shrinking the text.</summary>
    public void Tags(IReadOnlyList<string> tags)
    {
        Break();
        var width = Math.Max(1f, _parent.rect.width);
        var inset = _chrome.LabelInset;
        var x = 0f;
        var rowHeight = 0f;
        foreach (var text in tags)
        {
            var tagWidth = Math.Min(width, ChipWidth(text));
            if (x > 0f && x + tagWidth > width)
            {
                _y += rowHeight + Gap * 0.5f;
                x = 0f;
                rowHeight = 0f;
            }
            var rect = TouchUiKit.Rect(
                "Tag",
                _parent,
                new(0f, 1f),
                new(0f, 1f),
                new(x, -_y),
                new(x + tagWidth, -_y)
            );
            _skin.Tag(rect);
            var label = TouchUiKit.Text(
                TouchUiKit.Rect(
                    "Label",
                    rect,
                    Vector2.zero,
                    Vector2.one,
                    new(inset, Gap * 0.25f),
                    new(-inset, -Gap * 0.25f)
                ),
                _font,
                text,
                _chrome.BodyFont,
                TextAlignmentOptions.Center,
                TouchUiKit.MutedTextColor
            );
            TouchKeywordSprites.Apply(label);
            var height =
                Math.Max(
                    _chrome.BodyFont,
                    label.GetPreferredValues(text, Math.Max(1f, tagWidth - 2f * inset), 0f).y
                )
                + Gap * 0.5f;
            rect.offsetMin = new Vector2(x, -_y - height);
            rowHeight = Math.Max(rowHeight, height);
            x += tagWidth + Gap * 0.5f;
        }
        if (rowHeight > 0f)
            _y += rowHeight + Gap;
    }

    /// <summary>Ends the current chip row.</summary>
    public void Break()
    {
        if (_column == 0)
            return;
        _y += Math.Max(ChipHeight, _rowHeight) + Gap;
        _column = 0;
        _rowHeight = 0f;
    }

    private void Line(
        string text,
        float height,
        float size,
        Color color,
        TextAlignmentOptions alignment
    )
    {
        Break();
        var rect = TouchUiKit.Rect(
            "Line",
            _parent,
            new(0f, 1f),
            new(1f, 1f),
            new(0f, -_y - height),
            new(0f, -_y)
        );
        var label = TouchUiKit.Text(rect, _font, text, size, alignment, color);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        _y += height + Gap;
    }
}
