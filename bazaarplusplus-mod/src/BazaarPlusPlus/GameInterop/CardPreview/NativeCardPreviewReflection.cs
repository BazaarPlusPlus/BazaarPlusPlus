#nullable enable
using System.Reflection;
using BazaarGameClient.Domain.Models.Cards;
using HarmonyLib;
using TheBazaar.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeCardPreviewReflection
{
    private const string CardPreviewBaseTypeName = "TheBazaar.UI.CardPreviewBase";

    public static readonly Type? CardPreviewBaseType = AccessTools.TypeByName(
        CardPreviewBaseTypeName
    );

    public static readonly MethodInfo? SetUpMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "SetUp") : null;

    public static readonly MethodInfo? ShowMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "Show") : null;

    public static readonly MethodInfo? OnHoverMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "OnHover") : null;

    public static readonly MethodInfo? OnHoverOutMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "OnHoverOut") : null;

    public static readonly MethodInfo? ResizeMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "Resize") : null;

    public static readonly PropertyInfo? SizeProperty =
        CardPreviewBaseType != null ? AccessTools.Property(CardPreviewBaseType, "Size") : null;

    private static readonly FieldInfo? TooltipDataField =
        CardPreviewBaseType != null ? AccessTools.Field(CardPreviewBaseType, "_tooltipData") : null;

    private static readonly FieldInfo? ClientCardField =
        CardPreviewBaseType != null ? AccessTools.Field(CardPreviewBaseType, "_clientCard") : null;

    public static bool TryGetTooltipData(
        Component cardPreview,
        out CardTooltipData tooltipData,
        Action<NativeCardPreviewFailure>? reportFailure = null
    )
    {
        tooltipData = null!;
        if (!IsCardPreview(cardPreview) || TooltipDataField == null)
        {
            ReportUnavailable(reportFailure, NativeCardPreviewOperation.GetTooltipData);
            return false;
        }

        try
        {
            if (TooltipDataField.GetValue(cardPreview) is not CardTooltipData value)
                return false;

            tooltipData = value;
            return true;
        }
        catch (Exception ex)
        {
            ReportException(reportFailure, NativeCardPreviewOperation.GetTooltipData, ex);
            return false;
        }
    }

    public static bool TryGetClientCard(
        Component cardPreview,
        out Card card,
        Action<NativeCardPreviewFailure>? reportFailure = null
    )
    {
        card = null!;
        if (!IsCardPreview(cardPreview) || ClientCardField == null)
        {
            ReportUnavailable(reportFailure, NativeCardPreviewOperation.GetClientCard);
            return false;
        }

        try
        {
            if (ClientCardField.GetValue(cardPreview) is not Card value)
                return false;

            card = value;
            return true;
        }
        catch (Exception ex)
        {
            ReportException(reportFailure, NativeCardPreviewOperation.GetClientCard, ex);
            return false;
        }
    }

    public static bool TrySetTooltipData(
        Component cardPreview,
        CardTooltipData tooltipData,
        Action<NativeCardPreviewFailure>? reportFailure = null
    )
    {
        if (!IsCardPreview(cardPreview) || TooltipDataField == null || tooltipData == null)
        {
            ReportUnavailable(reportFailure, NativeCardPreviewOperation.SetTooltipData);
            return false;
        }

        try
        {
            TooltipDataField.SetValue(cardPreview, tooltipData);
            return true;
        }
        catch (Exception ex)
        {
            ReportException(reportFailure, NativeCardPreviewOperation.SetTooltipData, ex);
            return false;
        }
    }

    public static bool CanInvokeOnHover(Component cardPreview)
    {
        return IsCardPreview(cardPreview) && OnHoverMethod != null;
    }

    public static bool TryInvokeOnHover(
        Component cardPreview,
        Action<NativeCardPreviewFailure>? reportFailure = null
    ) =>
        TryInvoke(
            cardPreview,
            OnHoverMethod,
            NativeCardPreviewOperation.InvokeHover,
            reportFailure
        );

    public static bool TryInvokeOnHoverOut(
        Component cardPreview,
        Action<NativeCardPreviewFailure>? reportFailure = null
    ) =>
        TryInvoke(
            cardPreview,
            OnHoverOutMethod,
            NativeCardPreviewOperation.InvokeHoverOut,
            reportFailure
        );

    private static bool TryInvoke(
        Component cardPreview,
        MethodInfo? method,
        NativeCardPreviewOperation operation,
        Action<NativeCardPreviewFailure>? reportFailure
    )
    {
        if (!IsCardPreview(cardPreview) || method == null)
        {
            ReportUnavailable(reportFailure, operation);
            return false;
        }

        try
        {
            method.Invoke(cardPreview, Array.Empty<object>());
            return true;
        }
        catch (TargetInvocationException ex)
        {
            ReportException(reportFailure, operation, ex.InnerException ?? ex);
            return false;
        }
        catch (Exception ex)
        {
            ReportException(reportFailure, operation, ex);
            return false;
        }
    }

    private static void ReportUnavailable(
        Action<NativeCardPreviewFailure>? reportFailure,
        NativeCardPreviewOperation operation
    ) =>
        reportFailure?.Invoke(
            new NativeCardPreviewFailure(
                operation,
                NativeCardPreviewFailureReason.ReflectionUnavailable,
                templateId: null
            )
        );

    private static void ReportException(
        Action<NativeCardPreviewFailure>? reportFailure,
        NativeCardPreviewOperation operation,
        Exception exception
    ) =>
        reportFailure?.Invoke(
            new NativeCardPreviewFailure(
                operation,
                NativeCardPreviewFailureReason.ReflectionException,
                templateId: null,
                exception
            )
        );

    public static void ApplyLayerRecursive(GameObject root, int layer)
    {
        if (root == null)
            return;

        root.layer = layer;
        var transform = root.transform;
        for (var i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child != null)
                ApplyLayerRecursive(child.gameObject, layer);
        }
    }

    private static bool IsCardPreview(Component cardPreview)
    {
        return cardPreview != null
            && CardPreviewBaseType != null
            && CardPreviewBaseType.IsInstanceOfType(cardPreview);
    }
}
