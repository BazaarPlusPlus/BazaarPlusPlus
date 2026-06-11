#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
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
}
