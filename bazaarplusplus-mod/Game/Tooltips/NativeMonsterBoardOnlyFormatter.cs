#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.MonsterPreview;
using HarmonyLib;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class NativeMonsterBoardOnlyFormatter
{
    private static readonly MethodInfo? SetLegendTrayOnlyModeMethod = AccessTools.Method(
        typeof(CardTooltipController),
        "SetLegendTrayOnlyMode"
    );

    private static readonly MethodInfo? ClearHeaderMethod = AccessTools.Method(
        typeof(CardTooltipController),
        "ClearHeader"
    );

    private static readonly MethodInfo? HideCooldownClockMethod = AccessTools.Method(
        typeof(CardTooltipController),
        "HideCooldownClock"
    );

    private static readonly MethodInfo? DisposeLockVariantPanelMethod = AccessTools.Method(
        typeof(CardTooltipController),
        "DisposeLockVariantPanel"
    );

    private static readonly FieldInfo? MonsterRewardTooltipField = AccessTools.Field(
        typeof(CardTooltipController),
        "monsterRewardTooltip"
    );

    private static readonly MethodInfo? MonsterRewardShowAllMethod = AccessTools.Method(
        typeof(MonsterRewardTooltip),
        "ShowAll"
    );

    internal static bool ShouldUseBoardOnly(CardTooltipController? controller)
    {
        return MonsterPreviewFeature.UseBoardOnlyNativePreview
            && controller?.CurrentTooltipData is CardTooltipData tooltipData
            && CardTooltipDataFactory.GetMonster(tooltipData) != null;
    }

    internal static void Apply(CardTooltipController? controller, string context)
    {
        if (!ShouldUseBoardOnly(controller) || controller == null)
            return;

        SetLegendTrayOnlyModeMethod?.Invoke(controller, new object[] { true });
        ClearHeaderMethod?.Invoke(controller, null);
        HideCooldownClockMethod?.Invoke(controller, null);
        DisposeLockVariantPanelMethod?.Invoke(controller, null);
        controller.LegendTooltipComponent?.Hide();
        controller.LegendTooltipTrayComponent?.Hide();
        HideMonsterReward(controller);

        BppLog.Debug(
            "NativeMonsterBoardOnlyFormatter",
            $"Applied board-only formatting context={context} card={controller.CurrentCard?.Template?.InternalName ?? "-"} templateId={controller.CurrentCard?.TemplateId}"
        );
    }

    private static void HideMonsterReward(CardTooltipController controller)
    {
        var rewardTooltip = MonsterRewardTooltipField?.GetValue(controller);
        if (rewardTooltip != null)
            MonsterRewardShowAllMethod?.Invoke(rewardTooltip, new object[] { false });
    }
}
