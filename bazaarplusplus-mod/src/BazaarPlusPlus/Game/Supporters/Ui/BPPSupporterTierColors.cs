#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;

namespace BazaarPlusPlus.Game.Supporters.Ui;

/// <summary>
/// A supporter tier's name colour on the Desktop UI, the same tiers as Core's
/// <see cref="BPPSupporterAttribution.TierHex"/> uses for the touch host's rich text.
/// </summary>
internal static class BPPSupporterTierColors
{
    internal static Color For(int tier) =>
        tier switch
        {
            >= 4 => InGameTheme.Supporter4,
            3 => InGameTheme.Supporter3,
            2 => InGameTheme.Supporter2,
            _ => InGameTheme.Supporter1,
        };

    /// <summary>
    /// The desktop attribution rows draw the lowest tier slightly faded, below the names of
    /// those who give more.
    /// </summary>
    internal static Color ForRow(int tier) =>
        tier >= 2 ? For(tier) : InGameTheme.WithAlpha(InGameTheme.Supporter1, .9f);
}
