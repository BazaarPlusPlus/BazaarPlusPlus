#nullable enable

using System.Reflection;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.PvpBattles;
using TheBazaar;
using TheBazaar.UI.Components;

namespace BazaarPlusPlus.Game.CombatReplay.PlaybackUi;

internal static class PlayerAttributeRepairer
{
    internal static void EnsureSequencePlayerAttributes(
        CombatSequenceMessages sequence,
        IReplayPlaybackOutcomeSink outcome
    )
    {
        EnsurePlayerAttributes(sequence.SpawnMessage?.Data?.Player, ECombatantId.Player, outcome);
        EnsurePlayerAttributes(
            sequence.SpawnMessage?.Data?.Opponent,
            ECombatantId.Opponent,
            outcome
        );
        EnsurePlayerAttributes(sequence.DespawnMessage?.Data?.Player, ECombatantId.Player, outcome);
        EnsurePlayerAttributes(
            sequence.DespawnMessage?.Data?.Opponent,
            ECombatantId.Opponent,
            outcome
        );
    }

    internal static void EnsureRunPlayerAttributes(IReplayPlaybackOutcomeSink outcome)
    {
        EnsurePlayerAttributes(Data.Run?.Player, ECombatantId.Player, outcome);
        EnsurePlayerAttributes(Data.Run?.Opponent, ECombatantId.Opponent, outcome);
    }

    internal static void RestoreRecordedPlayerAttributes(
        PvpBattleManifest manifest,
        IReplayPlaybackOutcomeSink? outcome = null
    )
    {
        if (manifest == null)
            return;

        var player = Data.Run?.Player;
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

            ApplyRecordedAttribute(
                attributes,
                EPlayerAttributeType.Level,
                manifest.Participants.PlayerLevel
            );
            ApplyRecordedAttribute(
                attributes,
                EPlayerAttributeType.Prestige,
                manifest.Participants.PlayerPrestige
            );
            ApplyRecordedAttribute(
                attributes,
                EPlayerAttributeType.Income,
                manifest.Participants.PlayerIncome
            );
            ApplyRecordedAttribute(
                attributes,
                EPlayerAttributeType.Gold,
                manifest.Participants.PlayerGold
            );
            RefreshRecordedAttributePresentation();
        }
        catch (Exception ex)
        {
            outcome?.ReportDegradation(ReplayPlaybackReasonCode.PlayerAttributesUnavailable, ex);
        }
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

    internal static void InitializeBoardUiHealthBar(
        BoardUIController controller,
        object? player,
        IReplayPlaybackOutcomeSink? outcome = null
    )
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
            outcome?.ReportDegradation(
                ReplayPlaybackReasonCode.PlayerAttributesUnavailable,
                ex.InnerException ?? ex
            );
        }
    }

    internal static void UnregisterPlayerPortraitPlacedHandler(
        BoardUIController controller,
        IReplayPlaybackOutcomeSink? outcome = null
    )
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
            outcome?.ReportDegradation(ReplayPlaybackReasonCode.PlayerAttributesUnavailable, ex);
        }
    }

    internal static void EnsurePlayerAttributes(
        object? player,
        ECombatantId combatantId,
        IReplayPlaybackOutcomeSink? outcome = null
    )
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
            outcome?.ReportDegradation(ReplayPlaybackReasonCode.PlayerAttributesUnavailable, ex);
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

    private static void ApplyRecordedAttribute(
        System.Collections.IDictionary attributes,
        EPlayerAttributeType attributeType,
        int? value
    )
    {
        if (!value.HasValue)
            return;

        attributes[attributeType] = value.Value;
    }

    private static void RefreshRecordedAttributePresentation()
    {
        foreach (var prestigeBar in UnityEngine.Object.FindObjectsOfType<PrestigeBarController>())
        {
            prestigeBar?.ImmediateUpdate();
        }

        var updateUiFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var bank in UnityEngine.Object.FindObjectsOfType<BankToyController>())
        {
            bank?.GetType().GetMethod("UpdateUI", updateUiFlags)?.Invoke(bank, null);
        }
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
