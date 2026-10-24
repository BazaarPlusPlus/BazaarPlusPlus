using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Core.Ui;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>
/// Lays chips, headers and notes top to bottom in a fixed column grid under one parent. Columns
/// are fractions of the parent's width, so nothing waits for a layout pass to measure it. Sizes
/// come from the shared touch frame (<see cref="TouchChrome"/>): a chip is one touch target tall
/// with button-sized text that shrinks to fit, never under body size, and a chip whose label
/// would not fit whole at body size widens across more columns (<see cref="TouchChrome.ChipSpan"/>);
/// a note is as tall as its wrapped text.
/// </summary>
internal sealed class AndroidChipFlow
{
    private readonly RectTransform _parent;
    private readonly TMP_FontAsset _font;
    private readonly TouchChrome _chrome;
    private readonly int _columns;
    private readonly float _headerHeight;
    private int _column;
    private float _y;
    private TextMeshProUGUI? _probe;

    public AndroidChipFlow(
        RectTransform parent,
        TMP_FontAsset font,
        TouchChrome chrome,
        int columns,
        float headerHeight
    )
    {
        _parent = parent;
        _font = font;
        _chrome = chrome;
        _columns = columns;
        _headerHeight = headerHeight;
    }

    private float ChipHeight => _chrome.Target;

    private float Gap => _chrome.Gap;

    /// <summary>The height laid out so far, the next row included when it has chips.</summary>
    public float Height => _y + (_column > 0 ? ChipHeight : 0f);

    public void Chip(string label, bool selected, Action onTap, int span = 1)
    {
        span = TouchChrome.ChipSpan(_parent.rect.width, _columns, span, Gap, ChipWidth(label));
        if (_column + span > _columns)
            Break();
        var rect = AndroidUiKit.Rect(
            "Chip",
            _parent,
            new((float)_column / _columns, 1f),
            new((float)(_column + span) / _columns, 1f),
            new(_column == 0 ? 0f : Gap * 0.5f, -_y - ChipHeight),
            new(_column + span == _columns ? 0f : -Gap * 0.5f, -_y)
        );
        var (_, background, text) = AndroidUiKit.Button(
            rect,
            _font,
            label,
            _chrome.ButtonFont,
            _chrome.LabelInset,
            onTap
        );
        background.color = selected ? AndroidUiKit.ActiveColor : AndroidUiKit.ButtonColor;
        text.enableWordWrapping = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = _chrome.BodyFont;
        text.fontSizeMax = _chrome.ButtonFont;
        text.overflowMode = TextOverflowModes.Ellipsis;
        _column += span;
    }

    // The label at body size, the smallest autosize goes, plus the button's padding.
    private float ChipWidth(string label)
    {
        if (_probe == null)
        {
            _probe = AndroidUiKit.Text(
                AndroidUiKit.Fixed("ChipProbe", _parent, Vector2.zero, Vector2.zero, Vector2.zero),
                _font,
                "",
                _chrome.BodyFont,
                TextAlignmentOptions.Left,
                Color.clear
            );
            _probe.enableWordWrapping = false;
        }
        return _probe.GetPreferredValues(label).x + 2f * _chrome.LabelInset;
    }

    public void Header(string text) =>
        Line(
            text,
            _headerHeight,
            _chrome.ButtonFont,
            AndroidUiKit.TextColor,
            TextAlignmentOptions.BottomLeft
        );

    /// <summary>Wrapped text as tall as it needs, under the chips so far.</summary>
    public TextMeshProUGUI Note(string text, Color color)
    {
        Break();
        var rect = AndroidUiKit.Rect(
            "Note",
            _parent,
            new(0f, 1f),
            new(1f, 1f),
            new(0f, -_y),
            new(0f, -_y)
        );
        var label = AndroidUiKit.Text(
            rect,
            _font,
            text,
            _chrome.BodyFont,
            TextAlignmentOptions.TopLeft,
            color
        );
        var width = Math.Max(1f, _parent.rect.width);
        var height = Math.Max(_chrome.BodyFont, label.GetPreferredValues(text, width, 0f).y);
        rect.offsetMin = new Vector2(0f, -_y - height);
        _y += height + Gap;
        return label;
    }

    /// <summary>Ends the current chip row.</summary>
    public void Break()
    {
        if (_column == 0)
            return;
        _y += ChipHeight + Gap;
        _column = 0;
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
        var rect = AndroidUiKit.Rect(
            "Line",
            _parent,
            new(0f, 1f),
            new(1f, 1f),
            new(0f, -_y - height),
            new(0f, -_y)
        );
        var label = AndroidUiKit.Text(rect, _font, text, size, alignment, color);
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        _y += height + Gap;
    }
}
