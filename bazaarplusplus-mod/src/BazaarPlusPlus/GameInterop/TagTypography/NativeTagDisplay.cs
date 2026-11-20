#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.TagTypography;

internal readonly struct NativeTagDisplay(string label, Color? accentColor, string iconName)
{
    public string Label { get; } = label;

    public Color? AccentColor { get; } = accentColor;

    public string IconName { get; } = iconName ?? string.Empty;
}
