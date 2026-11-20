using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.Tooltips;
using TheBazaar.Utilities;
using UnityEngine.EventSystems;

namespace BazaarPlusPlus.Patches.Input;

[HarmonyPatch]
internal static class DesktopSkillOverlayPlatformPatch
{
    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MobileSkillBoardInputHandler), "Awake");
        yield return AccessTools.Method(
            typeof(MobileSkillBoardInputHandler),
            nameof(MobileSkillBoardInputHandler.TryOpenOverlay)
        );
        var show = AccessTools.Method(
            typeof(PopupManager),
            nameof(PopupManager.ShowMobileSkillOverlay),
            [typeof(IReadOnlyList<SkillProxyRenderer>), typeof(bool)]
        );
        // The platform guard executes inside MoveNext, before the prefab-load await.
        var stateMachine =
            show.GetCustomAttribute<AsyncStateMachineAttribute>()
            ?? throw new InvalidOperationException("Native skill overlay async entry changed.");
        yield return AccessTools.Method(stateMachine.StateMachineType, "MoveNext");
        yield return AccessTools.Method(
            typeof(MobileSkillOverlayController),
            "Show",
            [typeof(IReadOnlyList<SkillProxyRenderer>), typeof(bool)]
        );
    }

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var platformCheck = AccessTools.Method(
            typeof(ApplicationUtils),
            nameof(ApplicationUtils.IsMobilePlatform)
        );
        var guards = codes.Where(code => code.Calls(platformCheck)).ToArray();
        if (guards.Length != 1)
            throw new InvalidOperationException("Native skill overlay platform guard changed.");

        guards[0].opcode = OpCodes.Ldc_I4_1;
        guards[0].operand = null;
        return codes;
    }
}

[HarmonyPatch(
    typeof(MobileSkillBoardInputHandler),
    nameof(MobileSkillBoardInputHandler.TryOpenOverlay)
)]
internal static class DesktopSkillBoardTooltipPatch
{
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        var visible = AccessTools.PropertyGetter(
            typeof(TooltipParentComponent),
            nameof(TooltipParentComponent.IsPrimaryTooltipVisible)
        );
        var guards = codes
            .Select((code, index) => (code, index))
            .Where(entry => entry.code.Calls(visible))
            .ToArray();
        if (guards.Length != 1)
            throw new InvalidOperationException("Native skill overlay tooltip guard changed.");

        // HasShown stays true until fade-out completes. Bypass this guard at the native entry
        // so a pending fade cannot consume the same click that dismissed the tooltip.
        guards[0].code.opcode = OpCodes.Pop;
        guards[0].code.operand = null;
        codes.Insert(guards[0].index + 1, new CodeInstruction(OpCodes.Ldc_I4_0));
        return codes;
    }

    [HarmonyPrefix]
    private static void Prefix()
    {
        // Desktop hover can open a tooltip before the click, while the native mobile entry
        // rejects visible tooltips. Dismiss the current tooltip before entering.
        if (Data.TooltipParentComponent != null)
        {
            Data.TooltipParentComponent.UnlockAllLockedTooltipControllers();
            Data.TooltipParentComponent.HideAuxiliaryTooltipController();
            Data.TooltipParentComponent.HideCardTooltipController();
        }
    }
}

[HarmonyPatch(typeof(SkillProxyRenderer), nameof(SkillProxyRenderer.OnPointerClick))]
internal static class DesktopSkillBoardClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SkillProxyRenderer __instance, PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return true;
        var board = __instance.GetComponentInParent<MobileSkillBoardInputHandler>();
        if (board == null)
            return true;

        board.TryOpenOverlay();
        return false;
    }
}
