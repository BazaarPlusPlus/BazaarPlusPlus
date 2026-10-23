using System.Text;
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameClient.Domain.Tooltips;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Domain.Effect.AuraActions;
using Il2CppTheBazaar;
using Il2CppTheBazaar.Assets.Scripts.Core.Data.Tooltip;
using Il2CppTheBazaar.Tooltips;
using Il2CppTheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// BPP's next-tier values in the touch upgrade preview. The game's own preview fuses a value
/// only when the next tier's template carries its attribute, and then adds the current runtime
/// delta to the template base; it leaves literal ability and aura values, and aura values with
/// no referenced attribute, unprojected. While <see cref="AndroidUpgradePreview"/> holds an item,
/// each render of its tooltip leases one <see cref="AndroidUpgradePreviewValueProjection"/>,
/// renders the item's tooltip text with every value token resolved against it, sets the
/// cooldown clock from it, and returns the lease when the render ends. The projection is keyed
/// by the tooltip data's native pointer, so no other tooltip ever reads it.
/// </summary>
/// <remarks>
/// The desktop postfixes the game's private <c>RenderStylizedAttributeValue</c>. Under IL2CPP
/// that method takes a <c>Nullable</c> and returns a <c>ValueTuple</c> by value, and no
/// Android patch has a detour over struct arguments or results to stand on, so this host
/// replaces <c>RenderTooltip</c>, whose argument and result are references, with the same loop
/// in managed code, and calls the game's formatter (an ordinary interop call, which boxes the
/// structs) for every value BPP does not project.
/// </remarks>
internal static class AndroidUpgradePreviewValues
{
    private const long LoggedEvents = 20;
    private const int LoggedFailures = 5;
    private static Lease? _active;
    private static long _leased;
    private static long _returned;
    private static long _projected;
    private static long _unchanged;
    private static int _failures;

    public static long Leased => Interlocked.Read(ref _leased);

    public static long Returned => Interlocked.Read(ref _returned);

    public static long Projected => Interlocked.Read(ref _projected);

    /// <summary>The contents of a card tooltip start to render: lease a projection if held.</summary>
    public static void Prepare(CardTooltipData? tooltipData)
    {
        Return();
        var card = tooltipData?.CardInstance;
        if (tooltipData == null || !AndroidUpgradePreview.Holds(card))
            return;
        try
        {
            if (!AndroidUpgradePreviewValueProjection.TryCreate(tooltipData, out var projection))
                return;
            if (projection.MultiplierFailure is { } failure)
                ReportDegraded("multiplier_exception", failure);
            _active = new Lease(tooltipData.Pointer, projection);
            var count = Interlocked.Increment(ref _leased);
            if (count <= LoggedEvents)
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.upgrade_values.leased"),
                    ("tier", projection.SourceTier),
                    ("next_tier", projection.ProjectedTier),
                    ("leased", count),
                    ("returned", Returned)
                );
        }
        catch (Exception exception)
        {
            ReportDegraded("projection_exception", exception);
        }
    }

    /// <summary>The render finished: the lease, if any, ends with it.</summary>
    public static void Return()
    {
        if (_active == null)
            return;
        _active = null;
        Interlocked.Increment(ref _returned);
    }

    /// <summary>
    /// The game's <c>CardTooltipData.RenderTooltip</c> loop over <paramref name="builder"/>,
    /// with BPP's projected value in place of each value token the lease resolves. False when
    /// <paramref name="tooltipData"/> holds no lease, and the game renders it unchanged.
    /// </summary>
    public static bool TryRender(
        CardTooltipData tooltipData,
        TooltipBuilder? builder,
        out string rendered
    )
    {
        rendered = string.Empty;
        var lease = _active;
        if (lease == null || tooltipData == null || tooltipData.Pointer != lease.TooltipData)
            return false;
        var components = builder?.Components;
        var typography = Data.TooltipTypography;
        if (components == null || typography == null)
            return false;

        var text = new StringBuilder();
        var count = components.Count;
        var first = count > 0 ? components[0]?.Pointer ?? IntPtr.Zero : IntPtr.Zero;
        var hasIcon = false;
        ECardAttributeType? iconAttribute = null;
        for (var i = 0; i < count; i++)
        {
            var component = components[i];
            if (i + 1 < count)
            {
                var next = components[i + 1]?.TryCast<ITooltipToken>();
                if (next != null && !tooltipData.IsCustomAttributeReferencedFromToken(next))
                {
                    hasIcon = true;
                    iconAttribute = AndroidGameIds.Unboxed(() => next.ReferencedAttribute);
                }
            }
            if (component == null)
                continue;

            var token = component.TryCast<ITooltipToken>();
            if (token == null)
            {
                text.Append(
                    typography.ColorKeywords(
                        component.Render(),
                        component.Pointer == first,
                        hasIcon,
                        NullableOf(iconAttribute)
                    )
                );
                continue;
            }

            hasIcon = false;
            var referenced = AndroidGameIds.Unboxed(() => token.ReferencedAttribute);
            var style = AndroidGameIds.Unboxed(() =>
                TooltipComponentExtensions.GetStyleAttribute(token)
            );
            var value = AndroidGameIds.Unboxed(() => token.Resolve()) ?? 0f;
            if (referenced.HasValue)
            {
                var (segment, withIcon) = RenderValue(
                    tooltipData,
                    lease.Projection,
                    typography,
                    referenced.Value,
                    value,
                    style,
                    token,
                    component
                );
                text.Append(segment);
                hasIcon = withIcon;
                if (withIcon)
                    iconAttribute = style ?? referenced;
            }
            else if (style.HasValue)
            {
                // The game passes no token for these; the desktop recovers only the attribute
                // auras among them (UpgradePreviewAuraValueContextPatch), and so does this.
                var (segment, withIcon) = RenderValue(
                    tooltipData,
                    lease.Projection,
                    typography,
                    style.Value,
                    value,
                    style,
                    IsAttributeAura(token) ? token : null,
                    null
                );
                text.Append(segment);
                hasIcon = withIcon;
                if (withIcon)
                    iconAttribute = style;
            }
            else
            {
                text.Append(component.Render());
            }
        }
        rendered = text.ToString();
        return true;
    }

    /// <summary>The cooldown clock rendered: it shows the projected cooldown instead.</summary>
    public static void OnCooldownRendered(CooldownRenderer renderer, CardTooltipData? tooltipData)
    {
        var lease = _active;
        if (
            lease == null
            || renderer == null
            || tooltipData == null
            || tooltipData.Pointer != lease.TooltipData
            || !lease.Projection.TryResolveEffectiveCooldowns(out var current, out var upgraded)
        )
            return;
        var currentText = UpgradePreviewValueMath.CooldownText(current);
        var upgradedText = UpgradePreviewValueMath.CooldownText(upgraded);
        renderer.SetCooldown(
            currentText,
            !string.Equals(currentText, upgradedText, StringComparison.Ordinal),
            upgradedText
        );
    }

    // The desktop's UpgradePreviewValuePatch, with the game's own result kept for the values
    // the projection does not resolve and for the log's comparison.
    private static (string? Segment, bool WithIcon) RenderValue(
        CardTooltipData tooltipData,
        AndroidUpgradePreviewValueProjection projection,
        TooltipTypography typography,
        ECardAttributeType attribute,
        float value,
        ECardAttributeType? style,
        ITooltipToken? projectedToken,
        ITooltipComponent? nativeComponent
    )
    {
        var native = tooltipData.RenderStylizedAttributeValue(
            attribute,
            value,
            NullableOf(style),
            nativeComponent
        );
        var nativeResult = (native?.Item1, native?.Item2 ?? false);
        if (projectedToken == null || !projection.TryResolve(projectedToken, out var next))
            return nativeResult;

        var inSeconds = TooltipExtensions.RequiresConversionToSeconds(style ?? attribute);
        var current = UpgradePreviewValueMath.ToRenderedUnits(value, inSeconds);
        next = UpgradePreviewValueMath.ToRenderedUnits(next, inSeconds);
        var currentText = UpgradePreviewValueMath.Format(current);
        (string? Segment, bool WithIcon) result;
        if (UpgradePreviewValueMath.HaveSameFormattedValue(current, next))
        {
            if (style.HasValue)
            {
                var withIcon = typography.GetAttributeStringWithIcon(
                    style.Value,
                    currentText,
                    true
                );
                result = (withIcon?.Item1, withIcon?.Item2 ?? false);
            }
            else
            {
                result = (currentText, false);
            }
        }
        else
        {
            var fused = typography.GetFusionString(
                NullableOf(style),
                currentText,
                UpgradePreviewValueMath.Format(next),
                true
            );
            result = (fused?.Item1, fused?.Item2 ?? false);
        }

        if (string.Equals(result.Segment, nativeResult.Item1, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _unchanged);
            return result;
        }
        var count = Interlocked.Increment(ref _projected);
        if (count <= LoggedEvents)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.upgrade_values.projected"),
                ("attribute", attribute),
                ("current", currentText),
                ("next", UpgradePreviewValueMath.Format(next)),
                ("native", nativeResult.Item1 ?? "<null>"),
                ("bpp", result.Segment ?? "<null>"),
                ("count", count),
                ("unchanged", Interlocked.Read(ref _unchanged))
            );
        return result;
    }

    private static bool IsAttributeAura(ITooltipToken token)
    {
        var action = token.TryCast<TooltipComponentAura>()?.Aura?.Action;
        return action?.TryCast<TAuraActionCardModifyAttribute>() != null
            || action?.TryCast<TAuraActionPlayerModifyAttribute>() != null;
    }

    private static Il2CppSystem.Nullable<ECardAttributeType> NullableOf(
        ECardAttributeType? value
    ) =>
        value.HasValue
            ? new Il2CppSystem.Nullable<ECardAttributeType>(value.Value)
            : new Il2CppSystem.Nullable<ECardAttributeType>();

    private static void ReportDegraded(string reason, Exception exception)
    {
        if (Interlocked.Increment(ref _failures) <= LoggedFailures)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.upgrade_values.degraded",
                    storm: ["reason_code"]
                ),
                exception,
                ("reason_code", reason)
            );
    }

    private sealed record Lease(
        IntPtr TooltipData,
        AndroidUpgradePreviewValueProjection Projection
    );
}
