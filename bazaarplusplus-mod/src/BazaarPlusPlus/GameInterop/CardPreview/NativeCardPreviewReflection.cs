#nullable enable
using System;
using System.Reflection;
using System.Threading;
using BazaarGameShared.Domain.Cards;
using HarmonyLib;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeCardPreviewReflection
{
    private const string CardPreviewBaseTypeName = "TheBazaar.UI.CardPreviewBase";

    public static readonly Type? CardPreviewBaseType = AccessTools.TypeByName(
        CardPreviewBaseTypeName
    );

    public static readonly MethodInfo? SetUpMethod = ResolveSetUpMethod();

    public static readonly MethodInfo? ShowMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "Show") : null;

    public static readonly MethodInfo? ResizeMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "Resize") : null;

    public static readonly PropertyInfo? SizeProperty =
        CardPreviewBaseType != null ? AccessTools.Property(CardPreviewBaseType, "Size") : null;

    public static MethodInfo? ResolvePublicInstanceMethod(string name)
    {
        if (CardPreviewBaseType == null || string.IsNullOrWhiteSpace(name))
            return null;

        return CardPreviewBaseType.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null
        );
    }

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

    private static MethodInfo? ResolveSetUpMethod()
    {
        if (CardPreviewBaseType == null)
            return null;

        MethodInfo? legacy = null;
        foreach (
            var method in CardPreviewBaseType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            )
        )
        {
            if (!string.Equals(method.Name, "SetUp", StringComparison.Ordinal))
                continue;

            var parameters = method.GetParameters();
            if (IsSetUpSignature(parameters, includeCancellationToken: true))
                return method;

            if (legacy == null && IsSetUpSignature(parameters, includeCancellationToken: false))
                legacy = method;
        }

        return legacy;
    }

    private static bool IsSetUpSignature(ParameterInfo[] parameters, bool includeCancellationToken)
    {
        var expectedLength = includeCancellationToken ? 4 : 3;
        return parameters.Length == expectedLength
            && typeof(TCardBase).IsAssignableFrom(parameters[0].ParameterType)
            && parameters[1].ParameterType == typeof(bool)
            && typeof(TCardInstance).IsAssignableFrom(parameters[2].ParameterType)
            && (
                !includeCancellationToken
                || parameters[3].ParameterType == typeof(CancellationToken)
            );
    }
}
