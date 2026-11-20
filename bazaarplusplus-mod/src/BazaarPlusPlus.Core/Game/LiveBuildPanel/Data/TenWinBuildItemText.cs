#nullable enable

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

// Use the native rendered tooltip for the saved tier, enchantment and card values, including rich-text tags.
public sealed record TenWinBuildItemText(
    string Name,
    string? Enchantment,
    IReadOnlyList<string> Tags,
    float? CooldownSeconds,
    float? Ammo,
    string Description
);
