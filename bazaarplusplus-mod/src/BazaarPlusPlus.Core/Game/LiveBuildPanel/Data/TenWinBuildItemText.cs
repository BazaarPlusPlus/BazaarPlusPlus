#nullable enable

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

/// <summary>
/// One item of a copied Ten-Win Build as the game names and describes it, read by the host:
/// its name, enchantment and tag labels, its cooldown and ammo, and its text as the game's
/// tooltip renders it for the item's tier, enchantment and Saved Card Values (rich text).
/// </summary>
public sealed record TenWinBuildItemText(
    string Name,
    string? Enchantment,
    IReadOnlyList<string> Tags,
    float? CooldownSeconds,
    float? Ammo,
    string Description
);
