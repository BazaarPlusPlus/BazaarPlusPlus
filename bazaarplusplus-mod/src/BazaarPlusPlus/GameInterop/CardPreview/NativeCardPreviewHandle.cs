#nullable enable
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewHandle
{
    public NativeCardPreviewHandle(
        NativeCardPreviewKind kind,
        NativeCardPreviewSpec spec
    )
    {
        Kind = kind;
        Spec = spec;
        SetUpTask = _ready.Task;
    }

    private readonly TaskCompletionSource<object?> _ready = new();

    public Component? Card { get; private set; }
    public RectTransform? Rect { get; private set; }
    public NativeCardPreviewKind Kind { get; }
    public Task SetUpTask { get; }
    public NativeCardPreviewSpec Spec { get; }
    public bool IsReleased { get; private set; }

    public void Bind(Component card, RectTransform rect)
    {
        Card = card ?? throw new ArgumentNullException(nameof(card));
        Rect = rect ?? throw new ArgumentNullException(nameof(rect));
    }

    public void MarkReady() => _ready.TrySetResult(null);

    public void MarkFailed(Exception ex) => _ready.TrySetException(ex);

    public void MarkReleased() => IsReleased = true;
}
