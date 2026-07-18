#nullable enable
using HarmonyLib;
using TheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeCardPreviewLifecycle
{
    internal static void Bind(GameObject root, Action onDestroyed)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));
        if (onDestroyed == null)
            throw new ArgumentNullException(nameof(onDestroyed));

        var marker = root.GetComponent<NativeCardPreviewLifecycleMarker>();
        if (marker == null)
            marker = root.AddComponent<NativeCardPreviewLifecycleMarker>();
        marker.Bind(onDestroyed);
    }

    internal static void Disarm(GameObject root)
    {
        if (root == null)
            return;
        root.GetComponent<NativeCardPreviewLifecycleMarker>()?.Disarm();
    }

    internal static void NotifyDestroyed(GameObject root)
    {
        if (root == null)
            return;
        root.GetComponent<NativeCardPreviewLifecycleMarker>()?.NotifyDestroyed();
    }
}

internal sealed class NativeCardPreviewLifecycleMarker : MonoBehaviour
{
    private Action? _onDestroyed;

    internal void Bind(Action onDestroyed) => _onDestroyed = onDestroyed;

    internal void Disarm() => _onDestroyed = null;

    internal void NotifyDestroyed()
    {
        var callback = _onDestroyed;
        _onDestroyed = null;
        callback?.Invoke();
    }
}

[HarmonyPatch(typeof(CardPreviewBase), "OnDestroy")]
internal static class NativeCardPreviewLifecyclePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(CardPreviewBase __instance) =>
        NativeCardPreviewLifecycle.NotifyDestroyed(__instance.gameObject);
}
