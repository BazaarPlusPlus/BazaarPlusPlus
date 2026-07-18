#nullable enable
using System.Reflection;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Core.GameState;
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
    /// accept right now and preserves reflection failures as a typed outcome.</summary>
    public static PedestalEligibilityProbeOutcome ReadEligibleInstanceIds(
        PedestalState pedestalState
    )
    {
        try
        {
            if (!_reflectionAttempted)
            {
                _reflectionAttempted = true;
                _validateCardsMethod = AccessTools.Method(typeof(PedestalState), "ValidateCards");
                _validCardsField = AccessTools.Field(typeof(PedestalState), "_validCards");
            }
            if (_validateCardsMethod is null || _validCardsField is null)
                return PedestalEligibilityProbeOutcome.Failure(
                    EncounterProbeFailureReason.PedestalReflectionUnavailable,
                    exception: null
                );

            _validateCardsMethod.Invoke(pedestalState, null);
            if (
                !EncounterReflectionCollection.TryGetList(
                    _validCardsField.GetValue(pedestalState),
                    out var list
                )
            )
                return PedestalEligibilityProbeOutcome.Failure(
                    EncounterProbeFailureReason.PedestalReflectionUnavailable,
                    exception: null
                );
            if (list.Count == 0)
                return PedestalEligibilityProbeOutcome.Success(EmptySet);

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
            return PedestalEligibilityProbeOutcome.Success(ids);
        }
        catch (Exception ex)
        {
            return PedestalEligibilityProbeOutcome.Failure(
                EncounterProbeFailureReason.PedestalReadException,
                ex
            );
        }
    }
}

internal readonly record struct PedestalEligibilityProbeOutcome(
    bool IsSuccess,
    HashSet<string> InstanceIds,
    EncounterProbeFailureReason FailureReason,
    Exception? Exception
)
{
    internal static PedestalEligibilityProbeOutcome Success(HashSet<string> instanceIds) =>
        new(true, instanceIds, EncounterProbeFailureReason.None, null);

    internal static PedestalEligibilityProbeOutcome Failure(
        EncounterProbeFailureReason reason,
        Exception? exception
    ) => new(false, new HashSet<string>(), reason, exception);
}
