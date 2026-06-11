#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.TagTypography;

/// <summary>Resolved native display data for one tag: the locale-correct label, the game's
/// official keyword accent color (null when no keyword config), and the keyword's TMP sprite
/// icon name (empty when the tag has no icon, which is common for ECardTag values).</summary>
internal readonly struct NativeTagDisplay(string label, Color? accentColor, string iconName)
{
    public string Label { get; } = label;

    public Color? AccentColor { get; } = accentColor;

    public string IconName { get; } = iconName ?? string.Empty;
}
