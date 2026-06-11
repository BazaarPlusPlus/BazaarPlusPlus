#nullable enable

using System;
using System.Linq;
using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.TempoNet.Models;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;
using TheBazaar.UI.Components;

namespace BazaarPlusPlus.Game.CombatReplay.PlaybackUi;

internal static class PlayerAttributeRepairer
{
    internal static void EnsureSequencePlayerAttributes(CombatSequenceMessages sequence)
    {
        EnsurePlayerAttributes(sequence.SpawnMessage?.Data?.Player, ECombatantId.Player);
        EnsurePlayerAttributes(sequence.SpawnMessage?.Data?.Opponent, ECombatantId.Opponent);
        EnsurePlayerAttributes(sequence.DespawnMessage?.Data?.Player, ECombatantId.Player);
        EnsurePlayerAttributes(sequence.DespawnMessage?.Data?.Opponent, ECombatantId.Opponent);
    }

    internal static void EnsureRunPlayerAttributes()
    {
        EnsurePlayerAttributes(Data.Run?.Player, ECombatantId.Player);
        EnsurePlayerAttributes(Data.Run?.Opponent, ECombatantId.Opponent);
    }

    internal static void RecalculateHealthBarDividers(BoardUIController controller, object? player)
    {
        if (player == null)
            return;

        var healthBar = HealthBarBinder.GetBoardUiHealthBar(controller);
        if (healthBar == null)
            return;

        ApplyHealthBarMaxValue(healthBar, player);
    }

    internal static void InitializeBoardUiHealthBar(BoardUIController controller, object? player)
    {
        if (player == null)
            return;

        var healthBar = HealthBarBinder.GetBoardUiHealthBar(controller);
        if (healthBar == null)
            return;

        var initMethod = healthBar
            .GetType()
            .GetMethod(
                "Init",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (initMethod == null)
            return;

        try
        {
            initMethod.Invoke(healthBar, [player]);
            ApplyHealthBarMaxValue(healthBar, player);
        }
        catch (TargetInvocationException ex)
        {
            BppLog.Warn(
                "PlayerAttributeRepairer",
                $"Skipping health bar init for {controller.combatantId}: {ex.InnerException?.Message ?? ex.Message}"
            );
        }
    }

    internal static void UnregisterPlayerPortraitPlacedHandler(BoardUIController controller)
    {
        try
        {
            var handlerMethod = controller
                .GetType()
                .GetMethod(
                    "HandleOnPlayerPortraitPlaced",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
            if (handlerMethod == null)
                return;

            var handler = Delegate.CreateDelegate(typeof(Action), controller, handlerMethod);
            var eventField = typeof(BoardManager).GetField(
                "_playerPortraitPlaced",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            if (eventField?.GetValue(null) is Action currentDelegate)
            {
                eventField.SetValue(null, (Action)Delegate.Remove(currentDelegate, handler));
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "PlayerAttributeRepairer",
                $"Failed to unregister PlayerPortraitPlaced handler: {ex.Message}"
            );
        }
    }

    internal static void EnsurePlayerAttributes(object? player, ECombatantId combatantId)
    {
        if (player == null)
            return;

        try
        {
            var attributesProperty = player
                .GetType()
                .GetProperty(
                    "Attributes",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
            if (
                attributesProperty?.GetValue(player)
                is not System.Collections.IDictionary attributes
            )
                return;

            EnsurePlayerAttributeDefaults(attributes);
            EnsureHealthMax(attributes);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "PlayerAttributeRepairer",
                $"Failed to backfill replay player attributes for {combatantId}: {ex.Message}"
            );
        }
    }

    private static void EnsureHealthMax(System.Collections.IDictionary attributes)
    {
        if (
            attributes.Contains(EPlayerAttributeType.HealthMax)
            && Convert.ToInt32(attributes[EPlayerAttributeType.HealthMax]) > 0
        )
            return;

        if (!attributes.Contains(EPlayerAttributeType.Health))
            return;

        var healthValue = Convert.ToInt32(attributes[EPlayerAttributeType.Health]);
        if (healthValue <= 0)
            return;

        attributes[EPlayerAttributeType.HealthMax] = healthValue;
    }

    private static void EnsurePlayerAttributeDefaults(System.Collections.IDictionary attributes)
    {
        foreach (EPlayerAttributeType attributeType in Enum.GetValues(typeof(EPlayerAttributeType)))
        {
            EnsurePlayerAttribute(
                attributes,
                attributeType,
                attributeType == EPlayerAttributeType.Level ? 1 : 0
            );
        }
    }

    private static void EnsurePlayerAttribute(
        System.Collections.IDictionary attributes,
        EPlayerAttributeType attributeType,
        int defaultValue
    )
    {
        if (!attributes.Contains(attributeType))
            attributes[attributeType] = defaultValue;
    }

    private static void ApplyHealthBarMaxValue(object healthBar, object player)
    {
        var healthMax = TryGetPlayerAttribute(player, EPlayerAttributeType.HealthMax);
        if (!healthMax.HasValue)
            return;

        var updateMaxHealth = healthBar
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "UpdateMaxHealth", StringComparison.Ordinal))
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(uint)
                    && parameters[1].ParameterType == typeof(uint)
                    && parameters[2].ParameterType == typeof(bool);
            });
        if (updateMaxHealth == null)
            return;

        updateMaxHealth.Invoke(healthBar, [healthMax.Value, healthMax.Value, false]);
    }

    private static uint? TryGetPlayerAttribute(object player, EPlayerAttributeType attributeType)
    {
        var attributesProperty = player
            .GetType()
            .GetProperty(
                "Attributes",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (attributesProperty?.GetValue(player) is not System.Collections.IDictionary attributes)
            return null;
        if (!attributes.Contains(attributeType))
            return null;

        return Convert.ToUInt32(attributes[attributeType]);
    }
}
