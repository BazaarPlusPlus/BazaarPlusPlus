#nullable enable
using System.Threading.Tasks;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewHandle
{
    public NativeCardPreviewHandle(
        Component card,
        RectTransform rect,
        NativeCardPreviewKind kind,
        Task setUpTask,
        NativeCardPreviewSpec spec
    )
    {
        Card = card;
        Rect = rect;
        Kind = kind;
        SetUpTask = setUpTask;
        Spec = spec;
    }

    public Component Card { get; }
    public RectTransform Rect { get; }
    public NativeCardPreviewKind Kind { get; }
    public Task SetUpTask { get; }
    public NativeCardPreviewSpec Spec { get; }
}
