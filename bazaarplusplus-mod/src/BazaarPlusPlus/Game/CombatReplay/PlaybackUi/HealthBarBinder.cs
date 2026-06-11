#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using TheBazaar.UI.Components;
using TheBazaar.UI.EncounterPicker;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.PlaybackUi;

internal static class HealthBarBinder
{
    internal static void HideEncounterPickerOverlays()
    {
        HideObjectsOfType<EncounterPickerMapController>();
        HideObjectsOfType<InjectedEncounterPickerMapController>();

        foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (
                transform?.gameObject != null
                && string.Equals(
                    transform.gameObject.name,
                    "EncounterPicker_Map(Clone)",
                    StringComparison.Ordinal
                )
            )
            {
                transform.gameObject.SetActive(false);
            }
        }
    }

    internal static void EnsureOpponentPortraitVisible()
    {
        var replayPortrait = PlaybackUiState.ActiveOpponentPortrait;
        if (replayPortrait != null)
        {
            if (Data.CurrentEncounterController != null)
                Data.CurrentEncounterController.ShowCard(show: false);

            replayPortrait.gameObject.SetActive(true);
            replayPortrait.ShowCard(show: true);
            return;
        }

        var encounterController = Data.CurrentEncounterController;
        if (encounterController?.gameObject == null)
            return;

        encounterController.gameObject.SetActive(true);
        encounterController.ShowCard(show: true);
    }

    internal static async Task PrepareHealthBarsAsync()
    {
        var bindings = await RefreshHealthBarBindingsAsync();
        ShowPlayerHealthBar(bindings.PlayerController);
        Data.PlayerExperienceBar?.ToggleExperienceBarAndText(isVisible: false);
        Events.TryShowEmptyOpponentHealthBar.Trigger();
    }

    internal static void RefillOpponentHealthBar()
    {
        Events.TryRefillOpponentHealthBar.Trigger();
    }

    private static async Task<ReplayBoardUiBindings> RefreshHealthBarBindingsAsync()
    {
        var bindings = ResolveBoardUiControllers();

        if (bindings.PlayerController != null)
            BindBoardUiController(bindings.PlayerController, registerPlayerHealthBar: true);

        if (bindings.OpponentController != null)
            BindBoardUiController(bindings.OpponentController, registerPlayerHealthBar: false);

        await Task.Delay(150);
        return bindings;
    }

    private static IEnumerable<BoardUIController> GetSceneBoardUiControllers()
    {
        return UnityEngine
            .Object.FindObjectsOfType<BoardUIController>(true)
            .Where(controller => controller != null && controller.gameObject.scene.rootCount > 0);
    }

    private static ReplayBoardUiBindings ResolveBoardUiControllers()
    {
        var controllers = GetSceneBoardUiControllers().ToList();
        return new ReplayBoardUiBindings(
            SelectBoardUiController(controllers, ECombatantId.Player, AnchorSide.Player),
            SelectBoardUiController(controllers, ECombatantId.Opponent, AnchorSide.Opponent)
        );
    }

    private static BoardUIController? SelectBoardUiController(
        IEnumerable<BoardUIController> controllers,
        ECombatantId combatantId,
        AnchorSide anchorSide
    )
    {
        var anchor = Singleton<BoardManager>.Instance?.GetAnchor(anchorSide, AnchorType.Portrait);
        return controllers
            .Where(controller => controller.combatantId == combatantId)
            .OrderByDescending(controller => controller.gameObject.activeInHierarchy)
            .ThenByDescending(controller => controller.isActiveAndEnabled)
            .ThenByDescending(HasActiveHealthBar)
            .ThenBy(controller => GetControllerAnchorDistance(controller, anchor))
            .FirstOrDefault();
    }

    private static bool HasActiveHealthBar(BoardUIController controller)
    {
        var healthBar = GetBoardUiHealthBar(controller) as Component;
        return healthBar?.gameObject.activeInHierarchy == true;
    }

    private static float GetControllerAnchorDistance(
        BoardUIController controller,
        Transform? anchor
    )
    {
        if (anchor == null)
            return float.MaxValue;

        return Vector3.SqrMagnitude(controller.transform.position - anchor.position);
    }

    private static void BindBoardUiController(
        BoardUIController controller,
        bool registerPlayerHealthBar
    )
    {
        var player =
            controller.combatantId == ECombatantId.Player ? Data.Run?.Player : Data.Run?.Opponent;
        if (player == null)
            return;

        PlayerAttributeRepairer.EnsurePlayerAttributes(player, controller.combatantId);

        if (PlaybackUiState.InitializedBoardUiControllers.Add(controller.GetInstanceID()))
            InvokeBoardUiMethod(controller, "Init", player);

        if (controller.combatantId == ECombatantId.Player)
            PlayerAttributeRepairer.UnregisterPlayerPortraitPlacedHandler(controller);

        InvokeBoardUiMethod(controller, "SetBattlePlayer", player);
        ApplyBoardUiDividerConfig(controller);
        PlayerAttributeRepairer.InitializeBoardUiHealthBar(controller, player);

        if (registerPlayerHealthBar && controller.combatantId == ECombatantId.Player)
            Data.RegisterPlayerHealthBar(controller);
    }

    private static void ShowPlayerHealthBar(BoardUIController? playerController)
    {
        if (playerController != null)
        {
            if (Data.Run?.Player != null)
                PlayerAttributeRepairer.EnsurePlayerAttributes(
                    Data.Run.Player,
                    ECombatantId.Player
                );

            InvokeBoardUiMethod(playerController, "SetBattlePlayer", Data.Run?.Player);
            PlayerAttributeRepairer.InitializeBoardUiHealthBar(playerController, Data.Run?.Player);
            playerController.ShowEmptyPlayerHealthBar();
            RevealBoardUiHealthBar(playerController, showStatusNumbers: true);
            PlayerAttributeRepairer.RecalculateHealthBarDividers(
                playerController,
                Data.Run?.Player
            );
            return;
        }

        Data.PlayerHealthBar?.ShowEmptyPlayerHealthBar();
    }

    private static void ApplyBoardUiDividerConfig(BoardUIController controller)
    {
        var healthBarDividerConfigField = controller
            .GetType()
            .GetField(
                "healthBarDividerConfigSO",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        var dividerConfig =
            healthBarDividerConfigField?.GetValue(controller) as HealthBarDividerConfigSO;
        if (dividerConfig == null)
            return;

        var healthBar = GetBoardUiHealthBar(controller);
        if (healthBar == null)
            return;

        InvokeOptionalMethod(healthBar, "SetDividerConfig", dividerConfig);
    }

    private static void RevealBoardUiHealthBar(BoardUIController controller, bool showStatusNumbers)
    {
        var healthBar = GetBoardUiHealthBar(controller);
        if (healthBar == null)
            return;

        InvokeOptionalMethod(healthBar, "ToggleBarParent", true);
        InvokeOptionalMethod(healthBar, "ToggleStatusNumbers", showStatusNumbers);
        InvokeOptionalMethod(healthBar, "RefillHealthBar", 1f);
    }

    internal static object? GetBoardUiHealthBar(BoardUIController controller)
    {
        return controller
            .GetType()
            .GetField(
                "HealthBar",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
            ?.GetValue(controller);
    }

    private static void InvokeOptionalMethod(object target, string methodName, object argument)
    {
        var method = target
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                    return false;

                var parameters = candidate.GetParameters();
                return parameters.Length == 1
                    && parameters[0].ParameterType.IsInstanceOfType(argument);
            });

        method?.Invoke(target, [argument]);
    }

    private static void InvokeBoardUiMethod(
        BoardUIController controller,
        string methodName,
        object? argument = null
    )
    {
        var methods = controller
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal));

        MethodInfo? targetMethod = null;
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (argument == null)
            {
                if (parameters.Length == 0)
                {
                    targetMethod = method;
                    break;
                }

                continue;
            }

            if (parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(argument))
            {
                targetMethod = method;
                break;
            }
        }

        if (targetMethod == null)
            return;

        if (argument == null)
        {
            targetMethod.Invoke(controller, null);
            return;
        }

        targetMethod.Invoke(controller, [argument]);
    }

    private static void HideObjectsOfType<T>()
        where T : Component
    {
        foreach (var component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (component?.gameObject != null)
                component.gameObject.SetActive(false);
        }
    }
}

internal sealed class ReplayBoardUiBindings
{
    public ReplayBoardUiBindings(
        BoardUIController? playerController,
        BoardUIController? opponentController
    )
    {
        PlayerController = playerController;
        OpponentController = opponentController;
    }

    public BoardUIController? PlayerController { get; }

    public BoardUIController? OpponentController { get; }
}
