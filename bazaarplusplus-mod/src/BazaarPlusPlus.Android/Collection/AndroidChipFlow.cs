using BazaarPlusPlus.Android.Ui;
using Il2CppTMPro;
using UnityEngine;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>
/// Lays chips, headers and notes top to bottom in a fixed column grid under one parent. Columns
/// are fractions of the parent's width, so nothing waits for a layout pass to measure it.
/// </summary>
internal sealed class AndroidChipFlow
{
    private const float Gap = 8f;
    private const float HeaderHeight = 46f;

    private readonly RectTransform _parent;
    private readonly TMP_FontAsset _font;
    private readonly int _columns;
    private readonly float _chipHeight;
    private readonly float _fontSize;
    private int _column;
    private float _y;

    public AndroidChipFlow(
        RectTransform parent,
        TMP_FontAsset font,
        int columns,
        float chipHeight,
        float fontSize
    )
    {
        _parent = parent;
        _font = font;
        _columns = columns;
        _chipHeight = chipHeight;
        _fontSize = fontSize;
    }

    /// <summary>The height laid out so far, the next row included when it has chips.</summary>
    public float Height => _y + (_column > 0 ? _chipHeight : 0f);

    public void Chip(string label, bool selected, Action onTap, int span = 1)
    {
        span = Math.Clamp(span, 1, _columns);
        if (_column + span > _columns)
            Break();
        var rect = AndroidUiKit.Rect(
            "Chip",
            _parent,
            new((float)_column / _columns, 1f),
            new((float)(_column + span) / _columns, 1f),
            new(_column == 0 ? 0f : Gap * 0.5f, -_y - _chipHeight),
            new(_column + span == _columns ? 0f : -Gap * 0.5f, -_y)
        );
        var (_, background, text) = AndroidUiKit.Button(rect, _font, label, _fontSize, onTap);
        background.color = selected ? AndroidUiKit.ActiveColor : AndroidUiKit.ButtonColor;
        text.enableWordWrapping = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = 14f;
        text.fontSizeMax = _fontSize;
        _column += span;
    }

    public void Header(string text) => Line(text, HeaderHeight, 26f, AndroidUiKit.TextColor);

    public void Note(string text, float height) =>
        Line(text, height, 20f, AndroidUiKit.MutedTextColor);

    /// <summary>Ends the current chip row.</summary>
    public void Break()
    {
        if (_column == 0)
            return;
        _y += _chipHeight + Gap;
        _column = 0;
    }

    private void Line(string text, float height, float size, Color color)
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
        AndroidUiKit.Text(rect, _font, text, size, TextAlignmentOptions.BottomLeft, color);
        _y += height + Gap;
    }
}
