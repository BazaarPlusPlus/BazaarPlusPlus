#nullable enable
using System;
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
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
    /// snapshot. Empty result means either no filter is active or reflection
    /// couldn't reach the field; callers treat both the same.</summary>
    public static System.Collections.Generic.IReadOnlyList<string> ReadCurrentFilter()
    {
        try
        {
            if (!_resolveAttempted)
            {
                _resolveAttempted = true;
                _filterField = AccessTools.Field(typeof(AppState), "_iteractionFilter");
                if (_filterField is null)
                {
                    BppLog.Info(
                        "Encounter",
                        "AppState._iteractionFilter field not found via reflection"
                    );
                }
            }
            if (_filterField is null)
                return EmptyArray;
            if (_filterField.GetValue(null) is not System.Collections.IList list)
                return EmptyArray;
            if (list.Count == 0)
                return EmptyArray;
            var copy = new string[list.Count];
            for (var i = 0; i < list.Count; i++)
                copy[i] = list[i]?.ToString() ?? "";
            return copy;
        }
        catch (Exception ex)
        {
            BppLog.Error("Encounter", "ReadCurrentFilter reflection failed", ex);
            return EmptyArray;
        }
    }
}
