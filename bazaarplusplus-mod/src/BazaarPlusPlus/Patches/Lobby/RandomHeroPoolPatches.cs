#pragma warning disable CS0436
#nullable enable
using BazaarPlusPlus.Game.Lobby;
using BazaarPlusPlus.Game.Lobby.RandomHeroPool;
using HarmonyLib;
using TheBazaar.UI;

namespace BazaarPlusPlus.Patches.Lobby;

[HarmonyPatch(typeof(HeroSelectButtonsView), "Awake")]
internal static class RandomHeroPoolAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance) => RefreshWithGuard(__instance);

    internal static void RefreshWithGuard(HeroSelectButtonsView instance)
    {
        try
        {
            RandomHeroPoolNativeController.Attach(instance);
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportHeroPoolDegraded(
                HeroPoolOperation.Attach,
                LobbyLogReasonCode.OperationException,
                ex
            );
        }
    }
}

[HarmonyPatch(typeof(HeroSelectButtonsView), "RefreshButtons")]
internal static class RandomHeroPoolRefreshButtonsPatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance) =>
        RandomHeroPoolAwakePatch.RefreshWithGuard(__instance);
}

[HarmonyPatch(typeof(HeroSelectButtonsView), "ShowHeroesButtons")]
internal static class RandomHeroPoolShowHeroesButtonsPatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance, bool show)
    {
        if (show)
            RandomHeroPoolAwakePatch.RefreshWithGuard(__instance);
    }
}

[HarmonyPatch(typeof(HeroSelectButtonsView), "OnRandomHeroToggleChanged")]
internal static class RandomHeroPoolTogglePatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance) =>
        RandomHeroPoolAwakePatch.RefreshWithGuard(__instance);
}

[HarmonyPatch(typeof(HeroSelectButtonsView), "OnHeroSelected")]
internal static class RandomHeroPoolOnHeroSelectedPatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance) =>
        RandomHeroPoolAwakePatch.RefreshWithGuard(__instance);
}

[HarmonyPatch(typeof(HeroSelectButtonsView), "OnHeroPurchased")]
internal static class RandomHeroPoolOnHeroPurchasedPatch
{
    [HarmonyPrefix]
    private static void Prefix(HeroSelectButtonsView __instance, out int __state)
    {
        __state = HeroProgrammaticSelectionScope.Enter(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance) =>
        RandomHeroPoolAwakePatch.RefreshWithGuard(__instance);

    [HarmonyFinalizer]
    private static Exception? Finalizer(
        HeroSelectButtonsView __instance,
        int __state,
        Exception? __exception
    )
    {
        HeroProgrammaticSelectionScope.Restore(__instance, __state);
        return __exception;
    }
}

[HarmonyPatch(typeof(HeroItemView), "Start")]
internal static class RandomHeroPoolHeroItemStartPatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroItemView __instance)
    {
        try
        {
            RandomHeroPoolNativeController.NotifyItemStarted(__instance);
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportHeroPoolDegraded(
                HeroPoolOperation.ProjectInitialVisual,
                LobbyLogReasonCode.OperationException,
                ex
            );
        }
    }
}

[HarmonyPatch(typeof(HeroItemView), "UpdateView")]
internal static class RandomHeroPoolHeroItemUpdateViewPatch
{
    [HarmonyPrefix]
    private static bool Prefix(HeroItemView __instance)
    {
        try
        {
            return !RandomHeroPoolNativeController.TryProjectRandomModeItem(__instance);
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportHeroPoolDegraded(
                HeroPoolOperation.ProjectVisualUpdate,
                LobbyLogReasonCode.OperationException,
                ex
            );
            return true;
        }
    }
}

[HarmonyPatch(typeof(HeroItemView), nameof(HeroItemView.OnItemSelected))]
internal static class RandomHeroPoolHeroItemSelectedPatch
{
    [HarmonyPrefix]
    private static bool Prefix(HeroItemView __instance)
    {
        try
        {
            var route = RandomHeroPoolNativeController.RouteSelection(__instance);
            return NativePoolInteractionRouting.ShouldRunNativeAction(route);
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportHeroPoolDegraded(
                HeroPoolOperation.RouteCardClick,
                LobbyLogReasonCode.OperationException,
                ex
            );
            return true;
        }
    }
}

[HarmonyPatch(typeof(HeroSelectButtonsView), "SelectRandomHeroImmediate")]
internal static class RandomHeroPoolSelectRandomHeroImmediatePatch
{
    private static readonly RandomHeroPoolSelector Selector = new();
    private static readonly System.Reflection.FieldInfo? UnlockedHeroesField = AccessTools.Field(
        typeof(HeroSelectButtonsView),
        "_unlockedHeroes"
    );
    private static readonly System.Reflection.FieldInfo? IsProgrammaticSelectionField =
        AccessTools.Field(typeof(HeroSelectButtonsView), "_isProgrammaticSelection");

    [HarmonyPrefix]
    private static bool Prefix(HeroSelectButtonsView __instance)
    {
        try
        {
            return !TrySelectConfiguredRandomHero(__instance);
        }
        catch (Exception ex)
        {
            LobbyLogWriter.ReportHeroPoolDegraded(
                HeroPoolOperation.RouteRandomSelection,
                LobbyLogReasonCode.OperationException,
                ex
            );
            return true;
        }
    }

    private static bool TrySelectConfiguredRandomHero(HeroSelectButtonsView instance)
    {
        if (
            UnlockedHeroesField?.GetValue(instance)
            is not IEnumerable<HeroItemView> reflectedUnlockedHeroes
        )
        {
            return false;
        }

        var unlockedHeroViews = new List<HeroItemView>();
        var unlockedHeroIds = new List<string>();
        foreach (var view in reflectedUnlockedHeroes)
        {
            if (view == null)
                continue;

            unlockedHeroViews.Add(view);
            unlockedHeroIds.Add(view.Hero.ToString());
        }

        if (unlockedHeroViews.Count == 0)
            return false;

        var candidateHeroIds = RandomHeroPoolPlayerPrefs.ResolveEffectivePool(unlockedHeroIds);
        if (candidateHeroIds.Count == 0)
            return false;

        var randomIndex = UnityEngine.Random.Range(0, candidateHeroIds.Count);
        var selectedHeroId = Selector.SelectHero(candidateHeroIds, randomIndex);
        HeroItemView? selectedHeroView = null;
        foreach (var view in unlockedHeroViews)
        {
            if (!string.Equals(view.Hero.ToString(), selectedHeroId, StringComparison.Ordinal))
                continue;

            selectedHeroView = view;
            break;
        }

        if (selectedHeroView == null || IsProgrammaticSelectionField == null)
            return false;

        IsProgrammaticSelectionField.SetValue(instance, true);
        try
        {
            selectedHeroView.OnItemSelected(showVisuals: false);
        }
        finally
        {
            IsProgrammaticSelectionField.SetValue(instance, false);
        }

        return true;
    }
}
