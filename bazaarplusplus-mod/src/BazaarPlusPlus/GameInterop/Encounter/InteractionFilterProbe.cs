#nullable enable
using System.Reflection;
using BazaarPlusPlus.Core.GameState;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.GameInterop.Encounter;

/// <summary>Reads <c>AppState._iteractionFilter</c> via reflection. When the
/// filter is non-empty, the game is in a target-selection state (upgrade,
/// enchant, etc.) and only owned cards whose templateId is in the filter are
/// accepted by <c>BuyItemCommand</c>; other clicks silently no-op.</summary>
internal static class InteractionFilterProbe
{
    private static FieldInfo? _filterField;
    private static bool _resolveAttempted;

    private static readonly string[] EmptyArray = System.Array.Empty<string>();

    /// <summary>Main thread only. Returns the current filter as an immutable
    /// snapshot and preserves reflection failures as a typed outcome.</summary>
    public static InteractionFilterProbeOutcome ReadCurrentFilter()
    {
        try
        {
            if (!_resolveAttempted)
            {
                _resolveAttempted = true;
                _filterField = AccessTools.Field(typeof(AppState), "_iteractionFilter");
                if (_filterField is null)
                {
                    return InteractionFilterProbeOutcome.Failure(
                        EncounterProbeFailureReason.InteractionFilterReflectionUnavailable,
                        exception: null
                    );
                }
            }
            if (_filterField is null)
                return InteractionFilterProbeOutcome.Failure(
                    EncounterProbeFailureReason.InteractionFilterReflectionUnavailable,
                    exception: null
                );
            if (
                !EncounterReflectionCollection.TryGetList(_filterField.GetValue(null), out var list)
            )
                return InteractionFilterProbeOutcome.Failure(
                    EncounterProbeFailureReason.InteractionFilterReflectionUnavailable,
                    exception: null
                );
            if (list.Count == 0)
                return InteractionFilterProbeOutcome.Success(EmptyArray);
            var copy = new string[list.Count];
            for (var i = 0; i < list.Count; i++)
                copy[i] = list[i]?.ToString() ?? "";
            return InteractionFilterProbeOutcome.Success(copy);
        }
        catch (Exception ex)
        {
            return InteractionFilterProbeOutcome.Failure(
                EncounterProbeFailureReason.InteractionFilterReadException,
                ex
            );
        }
    }
}

internal readonly record struct InteractionFilterProbeOutcome(
    bool IsSuccess,
    IReadOnlyList<string> TemplateIds,
    EncounterProbeFailureReason FailureReason,
    Exception? Exception
)
{
    internal static InteractionFilterProbeOutcome Success(IReadOnlyList<string> templateIds) =>
        new(true, templateIds, EncounterProbeFailureReason.None, null);

    internal static InteractionFilterProbeOutcome Failure(
        EncounterProbeFailureReason reason,
        Exception? exception
    ) => new(false, Array.Empty<string>(), reason, exception);
}
