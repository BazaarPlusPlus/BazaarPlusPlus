#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;

namespace BazaarPlusPlus.Game.Supporters.Ui;

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

    internal static Color ForRow(int tier) =>
        tier >= 2 ? For(tier) : InGameTheme.WithAlpha(InGameTheme.Supporter1, .9f);
}
