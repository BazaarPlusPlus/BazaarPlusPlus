#pragma warning disable CS0436
using BazaarPlusPlus.Game.MonsterPreview;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus;

/// <summary>
/// When ShowTooltips runs on a showcase card, set a bypass flag so the
/// tooltip lock check does not block it.
/// </summary>
[HarmonyPatch(typeof(CardController), "ShowTooltips")]
internal static class ShowcaseCardShowTooltipsPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardController __instance)
    {
        if (__instance != null && __instance.GetComponent<ShowcaseCardMarker>() != null)
            ShowcaseTooltipBypass.Active = true;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        ShowcaseTooltipBypass.Active = false;
    }
}

/// <summary>
/// While the bypass flag is active, pretend the tooltip controller is not locked.
/// </summary>
[HarmonyPatch(
    typeof(TooltipParentComponent),
    nameof(TooltipParentComponent.IsCardTooltipControllerLocked)
)]
internal static class ShowcaseTooltipLockBypassPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        if (!ShowcaseTooltipBypass.Active)
            return true;

        __result = false;
        return false;
    }
}

/// <summary>
/// Track when ShowTooltipController executes for a showcase card so we can
/// prevent DisableLockModeCanvas from tearing down the big-picture lock.
///
/// ShowTooltipController is synchronous and calls DisableLockModeCanvas
/// directly, so a simple flag works reliably here.
/// </summary>
[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ShowTooltipController))]
internal static class ShowcaseShowTooltipControllerPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        if (ShowcaseTooltipBypass.Active)
            ShowcaseTooltipBypass.ShowingTooltip = true;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        ShowcaseTooltipBypass.ShowingTooltip = false;
    }
}

/// <summary>
/// Skip DisableLockModeCanvas when showing a tooltip for a showcase card.
/// This keeps the big-picture lock mode canvas (raycast blocking, visuals) intact.
/// </summary>
[HarmonyPatch(typeof(CardTooltipController), "DisableLockModeCanvas")]
internal static class ShowcaseDisableLockCanvasPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !ShowcaseTooltipBypass.ShowingTooltip;
    }
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.LockTooltipToggle))]
public static class CardTooltipControllerLockTogglePatch
{
    [HarmonyPrefix]
    static bool Prefix(CardTooltipController __instance)
    {
        var currentCard = __instance?.CurrentCard;
        if (MonsterPreviewFeature.UseCustomLivePreview)
        {
            var runtime = MonsterLockShowcaseRuntime.Instance;
            if (
                runtime != null
                && runtime.TryConsumeNextClickToClosePreview(
                    UnityEngine.EventSystems.PointerEventData.InputButton.Right,
                    "next right click"
                )
            )
            {
                return false;
            }

            if (
                runtime != null
                && runtime.ShouldInterceptLockToggle(currentCard)
                && runtime.HandleLockToggle(currentCard)
            )
                return false;
        }

        if (currentCard == null)
            return true;

        var controller = Data.CardAndSkillLookup?.GetCardController(currentCard);
        if (controller == null || controller.GetComponent<ShowcaseCardMarker>() == null)
            return true;

        BppLog.Debug(
            "EncounterTooltipPreview",
            $"Suppressed lock toggle for showcase card {currentCard.Template?.InternalName ?? currentCard.TemplateId.ToString()}"
        );
        return false;
    }
}

internal static class ShowcaseTooltipBypass
{
    /// <summary>
    /// Set during CardController.ShowTooltips() for showcase cards.
    /// Makes IsCardTooltipControllerLocked return false.
    /// </summary>
    public static bool Active;

    /// <summary>
    /// Set during CardTooltipController.ShowTooltipController() when
    /// triggered by a showcase card. Blocks DisableLockModeCanvas.
    /// </summary>
    public static bool ShowingTooltip;
}
