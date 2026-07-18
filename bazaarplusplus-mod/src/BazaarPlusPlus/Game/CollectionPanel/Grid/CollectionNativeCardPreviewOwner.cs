#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

internal sealed class CollectionNativeCardPreviewOwner : INativeCardPreviewOwner
{
    private readonly Transform _parent;
    private readonly CollectionCardCacheSession _cacheSession;

    internal CollectionNativeCardPreviewOwner(
        Transform parent,
        CollectionCardCacheSession cacheSession
    )
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _cacheSession = cacheSession ?? throw new ArgumentNullException(nameof(cacheSession));
    }

    public int Layer => CollectionGridOverlay.DefaultLayer;

    public Transform ResolveParent(NativeCardPreviewSubject subject) => _parent;

    public void PrepareWhileInactive(NativeCardPreviewOwnerContext context)
    {
        var marker = context.Root.GetComponent<CollectionPanelOwnedMarker>();
        if (marker == null)
            marker = context.Root.AddComponent<CollectionPanelOwnedMarker>();
        marker.CacheOwner = _cacheSession;
        marker.PreviewOwner = this;

        var canvasGroup = context.Root.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = context.Root.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
    }

    public void OnAcquired(NativeCardPreviewOwnerContext context)
    {
        var marker = context.Root.GetComponent<CollectionPanelOwnedMarker>();
        var tooltipData =
            context.TooltipData ?? context.Root.GetComponent<CardPreviewBase>()?._tooltipData;
        if (tooltipData == null || marker == null || marker.TooltipRegistered)
            return;

        CollectionTierTooltipRegistry.Register(tooltipData.CardInstance);
        marker.TooltipRegistered = true;
    }

    public void BeforeRelease(NativeCardPreviewOwnerContext context)
    {
        var marker = context.Root.GetComponent<CollectionPanelOwnedMarker>();
        var cardPreview = context.Root.GetComponent<CardPreviewBase>();
        ReleaseOwnedState(marker, cardPreview, context.TooltipData ?? cardPreview?._tooltipData);
    }

    public void ReportFailure(NativeCardPreviewFailure failure)
    {
        if (
            failure.Operation
            is NativeCardPreviewOperation.InvokeHover
                or NativeCardPreviewOperation.InvokeHoverOut
        )
        {
            var operation =
                failure.Operation == NativeCardPreviewOperation.InvokeHover
                    ? CollectionHoverOperation.OnHover
                    : CollectionHoverOperation.OnHoverOut;
            var field = CollectionPanelLogEvents.HoverInvokeFailedOperation.Bind(operation);
            if (failure.Exception == null)
            {
                BppLog.DebugEvent(CollectionPanelLogEvents.HoverInvokeFailed, () => [field]);
            }
            else
            {
                BppLog.DebugEvent(
                    CollectionPanelLogEvents.HoverInvokeFailed,
                    failure.Exception,
                    () => [field]
                );
            }
            return;
        }

        var fields = new[]
        {
            CollectionPanelLogEvents.CardBindDegradedStage.Bind(CollectionCardBindStage.Bind),
            CollectionPanelLogEvents.CardBindDegradedTemplateId.Bind(
                failure.TemplateId ?? Guid.Empty
            ),
            CollectionPanelLogEvents.CardBindDegradedReasonCode.Bind(MapFailureReason(failure)),
        };
        if (failure.Exception == null)
            BppLog.WarnEvent(CollectionPanelLogEvents.CardBindDegraded, fields);
        else
            BppLog.WarnEvent(CollectionPanelLogEvents.CardBindDegraded, failure.Exception, fields);
    }

    internal void OnNativeDestroyed(CardPreviewBase cardPreview)
    {
        var marker = cardPreview.GetComponent<CollectionPanelOwnedMarker>();
        ReleaseOwnedState(marker, cardPreview, cardPreview._tooltipData);
    }

    private static void ReleaseOwnedState(
        CollectionPanelOwnedMarker? marker,
        CardPreviewBase? cardPreview,
        TheBazaar.Tooltips.CardTooltipData? tooltipData
    )
    {
        if (tooltipData != null && marker?.TooltipRegistered == true)
        {
            CollectionTierTooltipRegistry.Unregister(tooltipData.CardInstance);
            marker.TooltipRegistered = false;
        }

        marker?.ReleaseCurrentArtKey();
        if (cardPreview != null && marker?.CardMaterialOwnedByCache == true)
            cardPreview._cardMaterial = null!;
    }

    internal static CollectionPanelLogReasonCode MapFailureReason(
        NativeCardPreviewFailure failure
    ) =>
        failure.Reason switch
        {
            NativeCardPreviewFailureReason.StaticDataUnavailable =>
                CollectionPanelLogReasonCode.StaticDataNotReady,
            NativeCardPreviewFailureReason.TemplateUnavailable =>
                CollectionPanelLogReasonCode.TemplateLookupFailed,
            NativeCardPreviewFailureReason.SetUpException
            or NativeCardPreviewFailureReason.ResizeException
            or NativeCardPreviewFailureReason.ShowException =>
                CollectionPanelLogReasonCode.NativePreviewRuntimeFailed,
            _ => CollectionPanelLogReasonCode.NativePreviewUnavailable,
        };
}
