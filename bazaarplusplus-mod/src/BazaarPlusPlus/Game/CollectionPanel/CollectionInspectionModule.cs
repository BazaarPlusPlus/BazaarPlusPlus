#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.Tooltips;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionInspectionModule
{
    private readonly CollectionInspectionState _state = new();
    private readonly CollectionInspectionCardAnchor _cardAnchor = new();
    private INativeCardPreviewHost? _host;
    private Action<bool>? _blockInput;
    private Func<Rect>? _sourceBounds;
    private CardTooltipController? _controller;
    private NativeInspectionPresentation? _presentation;
    private INativeCardPreviewScope? _scope;
    private INativeCardPreviewSession? _card;
    private InspectionPreviewOwner? _previewOwner;
    private NativeCardCellBoundsCache _bounds = new();
    private Rect _origin;
    private float _animation;
    private bool _cardRevealed;
    private bool _measurementPending;
    private float _measurementDeadline;

    public bool BlocksInput => _state.BlocksInput(Time.frameCount);

    public void OpenHovered(Guid templateId)
    {
        var controller = TheBazaar.Data.TooltipParentComponent?.CardTooltipController;
        if (
            BlocksInput
            || controller == null
            || !controller.isActiveAndEnabled
            || controller.IsLocked
            || controller.CurrentTooltipData is not CardTooltipData data
            || data.CardInstance.TemplateId != templateId
            || !CollectionTierTooltipRegistry.Contains(data.CardInstance)
        )
            return;
        controller.LockTooltipToggle();
    }

    public void Attach(
        INativeCardPreviewHost host,
        Action<bool> blockInput,
        Func<Rect> sourceBounds
    )
    {
        _host = host;
        _blockInput = blockInput;
        _sourceBounds = sourceBounds;
    }

    public void Detach()
    {
        Close();
        _host = null;
        _blockInput = null;
        _sourceBounds = null;
    }

    public void OnLocked(CardTooltipController controller)
    {
        if (
            _host == null
            || controller == null
            || !controller.IsLocked
            || controller.CurrentTooltipData is not CardTooltipData data
            || !CollectionTierTooltipRegistry.Contains(data.CardInstance)
        )
            return;
        Close();
        _controller = controller;
        _origin = _sourceBounds?.Invoke() ?? default;
        _state.Open();
        _cardAnchor.Reset();
        _animation = 0f;
        try
        {
            _blockInput?.Invoke(true);
            _presentation = new NativeInspectionPresentation(controller);
            _previewOwner = new InspectionPreviewOwner(_presentation.CardRoot);
            _scope = _host.OpenScope(_previewOwner);
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.inspection.opened"
                )
            );
            OnPreviewChanged(controller, data);
            RefreshLayout(0f);
        }
        catch (Exception ex)
        {
            Report(ex);
            Close();
        }
    }

    public void OnPreviewChanged(CardTooltipController controller, CardTooltipData data)
    {
        if (!ReferenceEquals(controller, _controller) || _scope == null)
            return;
        var card = data.CardInstance;
        var subject = new NativeCardPreviewSubject
        {
            TemplateId = card.TemplateId,
            Tier = card.Tier,
            EnchantmentType = (card as ItemCard)?.Enchantment,
            Attributes = new Dictionary<BazaarGameShared.Domain.Core.Types.ECardAttributeType, int>(
                card.Attributes
            ),
            InstanceIdPrefix = "bpp-inspection",
        };
        _ = AcquirePreviewAsync(_scope, subject, _state.Select());
    }

    public void OnNativeClosed(CardTooltipController controller, bool unlock = false)
    {
        if (ReferenceEquals(controller, _controller))
            Close(unlock);
    }

    public void Close() => Close(unlock: true);

    private void Close(bool unlock)
    {
        if (!_state.IsOpen)
            return;
        _state.Close(Time.frameCount);
        _cardAnchor.Reset();
        var controller = _controller;
        _controller = null;
        // Stop native ownership synchronously before destroying anything its lock can reference.
        try
        {
            if (unlock && controller != null && controller.IsLocked)
                controller.Unlock();
        }
        catch (Exception ex)
        {
            Report(ex);
        }
        finally
        {
            _card?.Dispose();
            _card = null;
            _previewOwner = null;
            var scope = _scope;
            var presentation = _presentation;
            _scope = null;
            _presentation = null;
            presentation?.ReleaseNative();
            if (scope != null)
                _ = DisposeScopeAsync(scope, presentation);
            else
                presentation?.Dispose();
            // Input remains blocked until a later frame, so dismissing cannot click through.
        }
    }

    public void Tick(float dt)
    {
        if (
            _state.IsOpen
            && (_controller == null || !_controller.isActiveAndEnabled || !_controller.IsLocked)
        )
            Close(unlock: false);
        _blockInput?.Invoke(BlocksInput);
        RefreshLayout(dt);
        if (_presentation?.PollDismiss() == true)
        {
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.inspection.background_dismissed"
                )
            );
            Close();
        }
    }

    private void RefreshLayout(float dt)
    {
        if (_presentation == null)
            return;
        if (_card != null && _cardAnchor.NeedsLayout(Screen.width, Screen.height))
            _bounds.InvalidateOnScaleDirty();
        if (_card != null && !_bounds.IsValid)
        {
            if (!_measurementPending)
            {
                _measurementPending = true;
                _measurementDeadline = Time.unscaledTime + 1f;
            }
            NativeCardCellFitter.PrepareGridRect(_card.Rect);
            NativeCardPreviewArtworkMeasurement.PrepareLayoutSize(
                _card.Rect,
                NativeCardCellFitter.FallbackNativeCardHeight
            );
            Canvas.ForceUpdateCanvases();
            if (!NativeCardPreviewArtworkMeasurement.TryMeasure(_card.Rect, out var artwork))
            {
                if (Time.unscaledTime >= _measurementDeadline)
                {
                    Report(
                        new InvalidOperationException(
                            "Native inspection artwork did not become measurable. "
                                + NativeCardPreviewArtworkMeasurement.DescribeLayout(_card.Rect)
                        )
                    );
                    Close();
                }
                return;
            }
            _measurementPending = false;
            _bounds.Store(
                new CardVisualBounds(
                    artwork.width,
                    artwork.height,
                    artwork.center.x,
                    artwork.center.y
                ),
                null
            );
        }
        var detail = _presentation.MeasureDescription(Screen.width * .92f, Screen.height * .74f);
        if (_card == null)
        {
            _presentation.PositionDescription(Screen.width / 2f, Screen.height / 2f, 1f);
            return;
        }
        NativeCardCellFitter.ApplyScale(
            _card.Rect,
            new CollectionGridRect(0f, 0f, Screen.width * .50f, Screen.height * .72f),
            0f,
            _bounds
        );
        if (!_bounds.TryGet(out var visual, out _))
            return;
        var baseScale = _card.Rect.localScale;
        var layout = CollectionInspectionLayout.ForContent(
            Screen.width,
            Screen.height,
            visual.Width * baseScale.x,
            visual.Height * baseScale.y,
            detail.x,
            detail.y
        );
        _presentation.PositionDescription(layout.DetailCenterX, layout.DetailCenterY, layout.Scale);
        var placement = _cardAnchor.Resolve(Screen.width, Screen.height, layout);
        _animation = Mathf.Min(1f, _animation + dt / .25f);
        var progress = 1f - Mathf.Pow(1f - _animation, 3f);
        var target = new Rect(placement.X, placement.Y, placement.Width, placement.Height);
        var origin = _origin.width > 0f ? _origin : target;
        var rect = new Rect(
            Vector2.Lerp(origin.position, target.position, progress),
            Vector2.Lerp(origin.size, target.size, progress)
        );
        var cell = new CollectionGridRect(rect.x, rect.y, rect.width, rect.height);
        var animatedPlacement = new CollectionInspectionCardPlacement(
            rect.x,
            rect.y,
            rect.width,
            rect.height
        );
        var scale = animatedPlacement.ScaleFor(visual.Width, visual.Height);
        _card.Rect.localScale = new Vector3(scale.X, scale.Y, 1f);
        NativeCardCellFitter.Reposition(_card.Rect, cell, 0f, _bounds, allowMeasure: false);
        if (!_cardRevealed)
        {
            if (_previewOwner?.Reveal(_card.Root) != true)
            {
                Report(
                    new InvalidOperationException(
                        "Native inspection visibility lease was unavailable."
                    )
                );
                Close();
                return;
            }
            _cardRevealed = true;
        }
    }

    private async Task AcquirePreviewAsync(
        INativeCardPreviewScope scope,
        NativeCardPreviewSubject subject,
        int revision
    )
    {
        try
        {
            var result = await scope.AcquireAsync(subject);
            if (result.Session == null)
            {
                if (_state.CanPublish(revision))
                    Close();
                return;
            }
            if (!_state.CanPublish(revision))
            {
                result.Session.Dispose();
                return;
            }
            _card?.Dispose();
            _card = result.Session;
            _bounds = new NativeCardCellBoundsCache();
            _cardRevealed = false;
            _measurementPending = false;
            var shown = _card.Show();
            if (shown.Status != NativePreviewActionStatus.Applied)
            {
                Report(
                    shown.Failure?.Exception
                        ?? new InvalidOperationException(
                            $"Native inspection preview activation failed: {shown.Status}."
                        )
                );
                Close();
                return;
            }
            RefreshLayout(0f);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Report(ex);
            if (_state.CanPublish(revision))
                Close();
        }
    }

    private static async Task DisposeScopeAsync(
        INativeCardPreviewScope scope,
        NativeInspectionPresentation? presentation
    )
    {
        try
        {
            await scope.DisposeAsync();
        }
        catch (Exception ex)
        {
            Report(ex);
        }
        finally
        {
            presentation?.Dispose();
        }
    }

    private static void Report(Exception ex) =>
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.CollectionPanel,
                "collection_panel.inspection.failed",
                storm: ["reason_code"]
            ),
            ex,
            ("reason_code", CollectionPanelLogReasonCode.NativePreviewRuntimeFailed)
        );

    private sealed class InspectionPreviewOwner(Transform parent) : INativeCardPreviewOwner
    {
        private readonly Dictionary<GameObject, (CanvasGroup Group, float Alpha)> _visibility =
            new();
        public int Layer => CollectionGridOverlay.DefaultLayer;
        public bool UsePremiumVisuals => true;

        public Transform ResolveParent(NativeCardPreviewSubject subject) => parent;

        public void PrepareWhileInactive(NativeCardPreviewOwnerContext context) { }

        public void OnAcquired(NativeCardPreviewOwnerContext context)
        {
            var group =
                context.Root.GetComponent<CanvasGroup>()
                ?? context.Root.AddComponent<CanvasGroup>();
            _visibility.Add(context.Root, (group, group.alpha));
            group.alpha = 0f;
            foreach (
                var graphic in context.Root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)
            )
                graphic.raycastTarget = false;
        }

        public bool Reveal(GameObject root)
        {
            if (!_visibility.TryGetValue(root, out var state) || state.Group == null)
                return false;
            state.Group.alpha = 1f;
            return true;
        }

        public void BeforeRelease(NativeCardPreviewOwnerContext context)
        {
            if (_visibility.Remove(context.Root, out var state) && state.Group != null)
                state.Group.alpha = state.Alpha;
            // Keep the neutral component until the pooled root is destroyed. Deferred
            // Destroy could remove a new lease's alpha gate after same-frame reuse.
        }

        public void ReportFailure(NativeCardPreviewFailure failure)
        {
            if (failure.Exception != null)
                Report(failure.Exception);
        }
    }
}
