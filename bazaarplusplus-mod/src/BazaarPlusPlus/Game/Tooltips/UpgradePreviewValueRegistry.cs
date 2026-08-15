#nullable enable
using System.Runtime.CompilerServices;
using BazaarGameClient.Domain.Tooltips;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class UpgradePreviewValueRegistry
{
    [ThreadStatic]
    private static int _tooltipRenderDepth;

    [ThreadStatic]
    private static ITooltipComponent? _fallbackComponent;

    private static readonly ConditionalWeakTable<
        CardTooltipData,
        UpgradePreviewValueProjection
    > Projections = new();

    internal static void Register(
        CardTooltipData tooltipData,
        UpgradePreviewValueProjection projection
    ) => Projections.Add(tooltipData, projection);

    internal static bool TryResolveNextValue(
        CardTooltipData tooltipData,
        ITooltipComponent? component,
        out float value
    )
    {
        value = default;
        component ??= TakeFallbackComponent();
        return tooltipData != null
            && Projections.TryGetValue(tooltipData, out var projection)
            && projection.TryResolve(component, out value);
    }

    internal static void BeginTooltipRender() => _tooltipRenderDepth++;

    internal static void EndTooltipRender()
    {
        if (_tooltipRenderDepth > 0)
            _tooltipRenderDepth--;

        if (_tooltipRenderDepth == 0)
            _fallbackComponent = null;
    }

    internal static void CaptureFallbackComponent(ITooltipComponent component)
    {
        // Native RenderTooltip omits the component argument for styled tokens whose
        // ReferencedAttribute is null. Resolve() runs immediately before that formatter,
        // so retain the token only for the duration of this synchronous render scope.
        if (_tooltipRenderDepth > 0)
            _fallbackComponent = component;
    }

    private static ITooltipComponent? TakeFallbackComponent()
    {
        var component = _fallbackComponent;
        _fallbackComponent = null;
        return component;
    }

    internal static bool TryResolveEffectiveCooldowns(
        CardTooltipData tooltipData,
        out float currentSeconds,
        out float upgradedSeconds
    )
    {
        currentSeconds = default;
        upgradedSeconds = default;
        return tooltipData != null
            && Projections.TryGetValue(tooltipData, out var projection)
            && projection.TryResolveEffectiveCooldowns(out currentSeconds, out upgradedSeconds);
    }

    internal static string Format(float value) =>
        value.IsDecimal() ? value.GetDecimalValueString() : value.ToString();

    internal static bool HaveSameFormattedValue(float currentValue, float projectedValue) =>
        string.Equals(Format(currentValue), Format(projectedValue), StringComparison.Ordinal);

    internal static float ConvertProjectedValueToRenderedUnits(
        ECardAttributeType attributeType,
        ECardAttributeType? styleAsAttribute,
        float projectedValue
    ) =>
        (
            styleAsAttribute?.RequiresConversionToSeconds()
            ?? attributeType.RequiresConversionToSeconds()
        )
            ? TooltipExtensions.MillisecondsToSeconds(projectedValue)
            : projectedValue;
}
