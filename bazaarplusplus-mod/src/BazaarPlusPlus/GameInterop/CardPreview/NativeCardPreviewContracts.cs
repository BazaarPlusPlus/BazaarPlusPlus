#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal interface INativeCardPreviewHost
{
    NativeCardMeasureResult Measure(NativeCardPreviewSubject subject);
    INativeCardPreviewScope OpenScope(INativeCardPreviewOwner owner);
    NativeTooltipRefreshResult RefreshHoveredTooltip(NativeTooltipRefreshRequest request);
}

internal interface INativeCardPreviewScope : IAsyncDisposable
{
    ValueTask<NativeCardAcquireResult> AcquireAsync(
        NativeCardPreviewSubject subject,
        CancellationToken cancellationToken = default
    );
}

internal interface INativeCardPreviewSession : IDisposable
{
    GameObject Root { get; }
    RectTransform Rect { get; }
    NativePreviewActionResult Show();
    NativePreviewActionResult Hide();
    NativePreviewActionResult HoverEnter();
    NativePreviewActionResult HoverExit();
}

internal interface INativeCardPreviewOwner
{
    int Layer { get; }
    Transform? ResolveParent(NativeCardPreviewSubject subject);
    void PrepareWhileInactive(NativeCardPreviewOwnerContext context);
    void OnAcquired(NativeCardPreviewOwnerContext context);
    void BeforeRelease(NativeCardPreviewOwnerContext context);
    void ReportFailure(NativeCardPreviewFailure failure);
}

internal interface INativeTooltipDataFactory
{
    CardTooltipData Create(Card card, CardTooltipData source, NativeTooltipRefreshMode mode);
}

internal sealed class NativeCardPreviewOwnerContext
{
    internal NativeCardPreviewOwnerContext(
        GameObject root,
        RectTransform rect,
        NativeCardPreviewSubject subject,
        CardTooltipData? tooltipData
    )
    {
        Root = root;
        Rect = rect;
        Subject = subject;
        TooltipData = tooltipData;
    }

    internal GameObject Root { get; }
    internal RectTransform Rect { get; }
    internal NativeCardPreviewSubject Subject { get; }
    internal CardTooltipData? TooltipData { get; }
}

internal enum NativeCardMeasureStatus
{
    Measured,
    Unavailable,
    Failed,
}

internal readonly record struct NativeCardMeasureResult(
    NativeCardMeasureStatus Status,
    int Span,
    NativeCardPreviewFailure? Failure
);

internal enum NativeCardAcquireStatus
{
    Acquired,
    Unavailable,
    Failed,
    ScopeClosed,
}

internal readonly record struct NativeCardAcquireResult(
    NativeCardAcquireStatus Status,
    INativeCardPreviewSession? Session,
    NativeCardPreviewFailure? Failure
);

internal enum NativeTooltipRefreshMode
{
    Normal,
    Upgrade,
    Enchant,
}

internal readonly record struct NativeTooltipRefreshRequest(
    TooltipParentComponent TooltipParent,
    NativeTooltipRefreshMode Mode
);

internal enum NativeTooltipRefreshStatus
{
    Refreshed,
    NoHoveredPreview,
    TooltipMismatch,
    NoChange,
    Failed,
}

internal readonly record struct NativeTooltipRefreshResult(
    NativeTooltipRefreshStatus Status,
    Card? Card,
    NativeCardPreviewFailure? Failure
);
