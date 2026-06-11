#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.GameInterop.Encounter;

/// <summary>Reads the active <c>PedestalState</c>'s eligible-card set once per tick.
/// Calls <c>PedestalState.ValidateCards()</c> via reflection (it's private but
/// idempotent — just recomputes <c>_validCards</c> from Hand+Stash through the
/// pedestal template's SelectionCriteria), then reads <c>_validCards</c> and
/// returns the InstanceIds. Replaces N invocations of the public
/// <c>CanBeUpgraded(Card)</c> — each of which would re-run ValidateCards internally
/// for an O(N²) cost per tick.</summary>
internal static class PedestalEligibilityProbe
{
    private static MethodInfo? _validateCardsMethod;
    private static FieldInfo? _validCardsField;
    private static bool _reflectionAttempted;

    private static readonly HashSet<string> EmptySet = new();

    /// <summary>Main thread only. Returns the set of InstanceIds the pedestal would
    /// accept right now. Empty when not on a pedestal, reflection fails, or no
    /// owned card satisfies the template's SelectionCriteria.</summary>
    public static HashSet<string> ReadEligibleInstanceIds(PedestalState pedestalState)
    {
        try
        {
            if (!_reflectionAttempted)
            {
                _reflectionAttempted = true;
                _validateCardsMethod = AccessTools.Method(typeof(PedestalState), "ValidateCards");
                _validCardsField = AccessTools.Field(typeof(PedestalState), "_validCards");
                if (_validateCardsMethod is null)
                    BppLog.Info(
                        "Encounter",
                        "PedestalState.ValidateCards not found via reflection"
                    );
                if (_validCardsField is null)
                    BppLog.Info("Encounter", "PedestalState._validCards not found via reflection");
            }
            if (_validateCardsMethod is null || _validCardsField is null)
                return EmptySet;

            _validateCardsMethod.Invoke(pedestalState, null);
            if (_validCardsField.GetValue(pedestalState) is not IList list || list.Count == 0)
                return EmptySet;

            var ids = new HashSet<string>(list.Count);
            foreach (var entry in list)
            {
                if (entry is Card card)
                {
                    var iid = card.InstanceId.Value;
                    if (!string.IsNullOrEmpty(iid))
                        ids.Add(iid);
                }
            }
            return ids;
        }
        catch (Exception ex)
        {
            BppLog.Info(
                "Encounter",
                $"PedestalEligibilityProbe transient failure: {ex.GetType().Name}: {ex.Message}"
            );
            return EmptySet;
        }
    }
}
