#nullable enable
using System;
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewHoverRelay
{
    private static readonly MethodInfo? OnHoverMethod =
        NativeCardPreviewReflection.ResolvePublicInstanceMethod("OnHover");
    private static readonly MethodInfo? OnHoverOutMethod =
        NativeCardPreviewReflection.ResolvePublicInstanceMethod("OnHoverOut");

    private readonly string _logComponent;
    private Component? _card;
    private bool _hovered;

    public NativeCardPreviewHoverRelay(string logComponent)
    {
        _logComponent = string.IsNullOrWhiteSpace(logComponent)
            ? "NativeCardPreviewHoverRelay"
            : logComponent;
    }

    public void Bind(Component? card)
    {
        if (ReferenceEquals(_card, card))
            return;

        Clear();
        _card = card;
    }

    public void Clear()
    {
        InvokeHoverOut();
        _card = null;
    }

    public bool InvokeHover()
    {
        if (_card == null)
            return false;
        if (_hovered)
            return true;

        if (!InvokeSafe(_card, OnHoverMethod, "OnHover"))
            return false;

        _hovered = true;
        return true;
    }

    public void InvokeHoverOut()
    {
        if (_card == null || !_hovered)
            return;

        InvokeSafe(_card, OnHoverOutMethod, "OnHoverOut");
        _hovered = false;
    }

    private bool InvokeSafe(Component target, MethodInfo? method, string label)
    {
        if (target == null || method == null)
            return false;

        try
        {
            method.Invoke(target, Array.Empty<object>());
            return true;
        }
        catch (TargetInvocationException ex)
        {
            BppLog.Debug(
                _logComponent,
                $"{label} threw: {ex.InnerException?.Message ?? ex.Message}"
            );
            return false;
        }
        catch (Exception ex)
        {
            BppLog.Debug(_logComponent, $"{label} invocation failed: {ex.Message}");
            return false;
        }
    }
}
