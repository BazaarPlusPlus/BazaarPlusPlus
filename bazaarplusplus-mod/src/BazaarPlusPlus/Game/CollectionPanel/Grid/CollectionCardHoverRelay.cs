#nullable enable
using System;
using System.Reflection;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Tiny IPointerEnter/Exit relay attached to the per-cell transparent hit Image. The native
// CardPreviewBase exposes OnHover() / OnHoverOut() as [UsedImplicitly] public methods, so
// it never receives pointer events on its own; this relay forwards them. We resolve the
// methods through reflection once at startup so the call site does not need a compile-time
// reference to TheBazaar.UI.CardPreviewBase.
internal sealed class CollectionCardHoverRelay
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
{
    private static readonly MethodInfo? OnHoverMethod = ResolveHoverMethod("OnHover");
    private static readonly MethodInfo? OnHoverOutMethod = ResolveHoverMethod("OnHoverOut");

    private Component? _card;

    public void Bind(Component card) => _card = card;

    public void Clear()
    {
        TryInvokeHoverOut();
        _card = null;
    }

    public void OnPointerEnter(PointerEventData _)
    {
        if (_card == null)
            return;
        InvokeSafe(_card, OnHoverMethod, "OnHover");
    }

    public void OnPointerExit(PointerEventData _)
    {
        if (_card == null)
            return;
        InvokeSafe(_card, OnHoverOutMethod, "OnHoverOut");
    }

    public void TryInvokeHoverOut()
    {
        if (_card == null)
            return;
        InvokeSafe(_card, OnHoverOutMethod, "OnHoverOut");
    }

    private static MethodInfo? ResolveHoverMethod(string name)
    {
        return NativeCardPreviewReflection.ResolvePublicInstanceMethod(name);
    }

    private static void InvokeSafe(Component target, MethodInfo? method, string label)
    {
        if (target == null || method == null)
            return;
        try
        {
            method.Invoke(target, Array.Empty<object>());
        }
        catch (TargetInvocationException ex)
        {
            BppLog.Debug(
                "CollectionCardHoverRelay",
                $"{label} threw: {ex.InnerException?.Message ?? ex.Message}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Debug("CollectionCardHoverRelay", $"{label} invocation failed: {ex.Message}");
        }
    }
}
