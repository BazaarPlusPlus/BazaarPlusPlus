#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Lobby.RandomHeroPool;
using HarmonyLib;
using TheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(HeroSelectButtonsView), "Awake")]
internal static class RandomHeroPoolAwakePatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance)
    {
        AttachWithGuard(__instance);
    }

    private static void AttachWithGuard(HeroSelectButtonsView instance)
    {
        try
        {
            RandomHeroPoolPanelController.Attach(instance);
        }
        catch (Exception ex)
        {
            BppLog.Warn("RandomHeroPool", $"Failed to attach random hero pool panel: {ex}");
        }
    }
}

[HarmonyPatch(typeof(HeroSelectButtonsView), "RefreshButtons")]
internal static class RandomHeroPoolRefreshButtonsPatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance)
    {
        try
        {
            RandomHeroPoolPanelController.NotifyRosterChanged(__instance, forceRebuild: true);
        }
        catch (Exception ex)
        {
            BppLog.Warn("RandomHeroPool", $"Failed to refresh random hero pool: {ex}");
        }
    }
}

[HarmonyPatch(typeof(HeroSelectButtonsView), "ShowHeroesButtons")]
internal static class RandomHeroPoolShowHeroesButtonsPatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance, bool show)
    {
        if (!show)
            return;

        RandomHeroPoolPanelController.NotifyRosterChanged(__instance, forceRebuild: true);
    }
}

[HarmonyPatch(typeof(HeroSelectButtonsView), "OnHeroPurchased")]
internal static class RandomHeroPoolOnHeroPurchasedPatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance, EHero hero)
    {
        RandomHeroPoolPanelController.NotifyRosterChanged(__instance, forceRebuild: true);
    }
}

[HarmonyPatch(typeof(HeroSelectButtonsView), "OnHeroSelected")]
internal static class RandomHeroPoolOnHeroSelectedPatch
{
    [HarmonyPostfix]
    private static void Postfix(HeroSelectButtonsView __instance, EHero hero)
    {
        RandomHeroPoolPanelController.NotifyVisibilityChanged(__instance);
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
            BppLog.Warn("RandomHeroPool", $"Failed to route random hero selection: {ex}");
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

        var unlockedHeroViews = reflectedUnlockedHeroes.Where(view => view != null).ToArray();
        if (unlockedHeroViews.Length == 0)
        {
            return false;
        }

        var candidateHeroIds = RandomHeroPoolPlayerPrefs.ResolveEffectivePool(
            unlockedHeroViews.Select(view => view.Hero.ToString())
        );
        if (candidateHeroIds.Count == 0)
        {
            return false;
        }

        var randomIndex = UnityEngine.Random.Range(0, candidateHeroIds.Count);
        var selectedHeroId = Selector.SelectHero(candidateHeroIds, randomIndex);
        var selectedHeroView = unlockedHeroViews.FirstOrDefault(view =>
            string.Equals(view.Hero.ToString(), selectedHeroId, StringComparison.Ordinal)
        );
        if (selectedHeroView == null || IsProgrammaticSelectionField == null)
        {
            return false;
        }

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
