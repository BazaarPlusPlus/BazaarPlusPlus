#nullable enable
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

// This method runs after native asynchronous tooltip spawning. A hide issued while
// spawning cannot cancel it, so retire the exact old data rather than hiding globally.
[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ShowTooltipController))]
internal static class NativeMonsterBoardTooltipPatch
{
    internal static readonly NativeMonsterBoardTooltipGate Gate = new();

    [HarmonyPrefix]
    private static bool Prefix(ITooltipData iTooltipData) => Gate.Allows(iTooltipData);
}
