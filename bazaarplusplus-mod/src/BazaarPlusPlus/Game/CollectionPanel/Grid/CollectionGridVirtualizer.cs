#nullable enable
using System.Diagnostics;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Recycler virtualizer: instead of laying out every card in the visible set up front, we
// only realize cells inside the scroll window (+ overscan) and keep ~realizedRows*cols
// native preview sessions alive at a time. Scrolling just rewrites anchoredPosition on
// the existing cells (O(visible)); only when a cell index moves out of the window do we
// dispose its opaque session and bind a fresh one for the cell entering it.
//
// Native preview acquisition does not complete until SetUp and resize have settled. The scope
// owns cancellation, pool reuse, and late continuation cleanup; realized cells only retain the
// opaque session plus feature layout/hover state.
//
// Filter/tab changes cancel everything in flight by bumping the global generation guard.
// Already-shown cards that remain in the next visible set keep their native instances and
// move directly to their new indices; removed and not-yet-shown cells are recycled.
internal sealed class CollectionGridVirtualizer
{
    internal const float FallbackNativeCardHeight = 484f;

    private readonly CollectionGridOverlay _overlay;
    private readonly INativeCardPreviewScope _previewScope;
    private readonly Dictionary<int, RealizedCell> _realized = new();
    private readonly Dictionary<int, PendingBind> _pendingBinds = new();
    private readonly PendingBindTracker _pendingBindTracker = new();
    private readonly HashSet<Guid> _failedBindGuids = new();
    private readonly List<int> _recycleScratch = new();
    private readonly List<int> _pendingRecycleScratch = new();
    private readonly List<CollectionGridRect> _slotRects = new();
    private readonly CollectionGridSlotLayer? _slots;

    private IReadOnlyList<CollectionCardVm> _visible = Array.Empty<CollectionCardVm>();
    private IReadOnlyDictionary<
        Guid,
        IReadOnlyList<CollectionSourceOfferMatch>
    > _sourceMatchesByCardId = new Dictionary<Guid, IReadOnlyList<CollectionSourceOfferMatch>>();
    private CollectionGridLayout _layout = CollectionGridLayout.Empty;
    private float _viewportWidth;
    private float _viewportHeight;
    private float _scrollY;

    // Pixelization of the fixed unit grid for the current viewport. _unit is one column's width
    // (px), derived from a shared cross-tab grid envelope so Item/Skill switches do not move the
    // display-case edges; _originX/_originY are the inset (horizontal centering + uniform padding).
    private float _unit;
    private float _gap = CollectionGridConstants.GridGap;
    private float _originX = CollectionGridConstants.GridOuterPadding;
    private float _originY = CollectionGridConstants.GridOuterPadding;
    private bool _scaleDirty = true;

    private int _generation;
    private int _perCellGeneration;
    private float _lastScrollY = float.NaN;
    private int _hoverPollIndex = -1;
    private bool _hoverDispatched;
    private FirstWindowBindDiagnostics? _firstWindowDiagnostics;

    public CollectionGridVirtualizer(
        CollectionGridOverlay overlay,
        INativeCardPreviewScope previewScope
    )
    {
        _overlay = overlay;
        _previewScope = previewScope ?? throw new ArgumentNullException(nameof(previewScope));
        if (overlay.BoardRoot != null)
            _slots = new CollectionGridSlotLayer(overlay.BoardRoot);
    }

    // Content height in overlay pixels: top + bottom display-case padding plus the packed
    // shelves. Consumed by the UITK ScrollView spacer so the scrollbar range is correct.
    public float ContentHeight =>
        _layout.ShelfCount == 0
            ? 0f
            : 2f * CollectionGridConstants.GridOuterPadding + _layout.ContentHeight(_unit, _gap);
    public int VisibleCount => _visible.Count;
    public bool HasPendingBinds => _pendingBindTracker.HasPendingBinds;
    public Task WhenPendingBindsSettled => _pendingBindTracker.WhenSettled;

    // SetVisible swaps in a new ordered visible set (typically after filter change). Cards
    // already shown in both sets retain their native instances and current opacity; only newly
    // realized cards enter the fade-in path. Caller is expected to also reset scrollY to 0.
    public void SetVisible(
        IReadOnlyList<CollectionCardVm> visible,
        CollectionTabKind activeTab,
        IReadOnlyDictionary<
            Guid,
            IReadOnlyList<CollectionSourceOfferMatch>
        >? sourceMatchesByCardId = null
    )
    {
        var retention = BuildRetentionPlan(visible);
        BumpGeneration();
        _visible = visible ?? Array.Empty<CollectionCardVm>();
        _sourceMatchesByCardId =
            sourceMatchesByCardId
            ?? new Dictionary<Guid, IReadOnlyList<CollectionSourceOfferMatch>>();
        _gap = CollectionGridConstants.GridGap;
        _layout = CollectionGridLayout.Build(_visible, activeTab);
        RecomputePixelization();
        RetainShownCells(retention);
        _lastScrollY = float.NaN;
        _firstWindowDiagnostics =
            BppBuild.IsDebug && _visible.Count > 0
                ? new FirstWindowBindDiagnostics(_generation)
                : null;
    }

    public void SetViewport(float width, float height)
    {
        if (
            Mathf.Approximately(_viewportWidth, width)
            && Mathf.Approximately(_viewportHeight, height)
        )
            return;
        _viewportWidth = Mathf.Max(0f, width);
        _viewportHeight = Mathf.Max(0f, height);
        RecomputePixelization();
        _lastScrollY = float.NaN;
    }

    public void SetScrollY(float y) => _scrollY = Mathf.Max(0f, y);

    public void Tick()
    {
        var board = _overlay.BoardRoot;
        if (board == null || _visible.Count == 0 || _layout.ShelfCount == 0 || _unit <= 0f)
        {
            if (_realized.Count > 0)
                RecycleAll();
            if (_pendingBinds.Count > 0)
                CancelPendingBinds();
            _slots?.Clear();
            return;
        }

        // Visible window is a contiguous shelf range. Shelves are a uniform pitch within a tab
        // (Items 2 units, Skills 1 unit tall), so the first/last visible shelf is a division,
        // and each shelf maps directly onto a contiguous visible-index span.
        var shelfPitch = _layout.ShelfPitch(_unit, _gap);
        // Clamp BOTH ends into [0, maxShelf] before indexing ShelfAt. _scrollY can transiently
        // overshoot the packed content — a frame during a viewport resize before the ScrollView
        // re-clamps its offset, or a DPI scroll-range mismatch — which would otherwise push
        // firstShelf past the last shelf (or lastShelf below 0) and throw IndexOutOfRange.
        var maxShelf = _layout.ShelfCount - 1;
        var firstShelf = Mathf.Clamp(
            Mathf.FloorToInt((_scrollY - _originY) / shelfPitch)
                - CollectionGridConstants.RowOverscan,
            0,
            maxShelf
        );
        var lastShelf = Mathf.Clamp(
            Mathf.FloorToInt((_scrollY + _viewportHeight - _originY) / shelfPitch)
                + CollectionGridConstants.RowOverscan,
            0,
            maxShelf
        );
        var firstIdx = _layout.ShelfAt(firstShelf).FirstIndex;
        var lastIdx = _layout.ShelfAt(lastShelf).LastIndex;
        _firstWindowDiagnostics?.EnsureWindow(
            firstIdx,
            lastIdx,
            _visible.Count,
            _layout.ShelfCount
        );

        // 1) Recycle cells that scrolled out of window.
        _recycleScratch.Clear();
        foreach (var pair in _realized)
        {
            if (pair.Key < firstIdx || pair.Key > lastIdx)
                _recycleScratch.Add(pair.Key);
        }
        foreach (var index in _recycleScratch)
        {
            var cell = _realized[index];
            _realized.Remove(index);
            RecycleCell(cell);
        }
        _pendingRecycleScratch.Clear();
        foreach (var pair in _pendingBinds)
        {
            if (pair.Key < firstIdx || pair.Key > lastIdx)
                _pendingRecycleScratch.Add(pair.Key);
        }
        foreach (var index in _pendingRecycleScratch)
            CancelPendingBind(index);

        // 2) Realize newly-visible cells, rate-limited by wall-clock budget for cold binds.
        var tickStart = Time.realtimeSinceStartup;
        var coldBudgetSeconds = CollectionGridConstants.ColdBindBudgetMs * 0.001f;
        for (var idx = firstIdx; idx <= lastIdx; idx++)
        {
            if (_realized.ContainsKey(idx) || _pendingBinds.ContainsKey(idx))
                continue;
            if (Time.realtimeSinceStartup - tickStart > coldBudgetSeconds)
                break;
            TryRealize(idx);
        }
        _firstWindowDiagnostics?.TryLogBindingPhase(_generation);

        // 3) Reposition realized cells whenever the scroll offset moved (or the base unit
        // changed on a viewport resize, _scaleDirty). Skipping when nothing changed avoids
        // forcing a Canvas rebuild on idle frames. Slots track the same window so the grid
        // order is visible even where cards have not finished loading.
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (_scrollY != _lastScrollY || _scaleDirty)
        {
            _lastScrollY = _scrollY;
            foreach (var pair in _realized)
            {
                if (_scaleDirty)
                    ApplyCellScale(pair.Key, pair.Value);
                Reposition(pair.Key, pair.Value);
            }
            _scaleDirty = false;
            SyncSlots(firstIdx, lastIdx);
        }
    }

    public void Dispose()
    {
        BumpGeneration();
        RecycleAll();
        _slots?.Clear();
        _visible = Array.Empty<CollectionCardVm>();
        _hoverPollIndex = -1;
        _hoverDispatched = false;
        _firstWindowDiagnostics = null;
    }

    // Polled hover (CollectionGridConstants.UsePolledHover = true, the default). Called
    // from CollectionPanel.Update with the mouse position and the viewport rect, both in
    // screen pixels (bottom-left origin). Maps the cursor into the realized cell under it
    // and dispatches OnHover/OnHoverOut accordingly.
    //
    // Acquisition completes native SetUp before a cell is realized, so hover never targets a
    // half-initialized preview. _hoverDispatched prevents repeated native enter calls while the
    // cursor remains within the same cell.
    public void PollHover(Vector2 mousePixels, Rect viewportBoundsPx)
    {
        if (_visible.Count == 0 || _layout.ShelfCount == 0 || _unit <= 0f)
        {
            DispatchHoverOut();
            return;
        }
        if (!viewportBoundsPx.Contains(mousePixels))
        {
            DispatchHoverOut();
            return;
        }

        var localX = mousePixels.x - viewportBoundsPx.x;
        // Viewport bottom-left origin → invert to top-left origin so layout math matches.
        var localY = viewportBoundsPx.height - (mousePixels.y - viewportBoundsPx.y);
        var contentY = localY + _scrollY;

        // Hit-test against precomputed cell rects: find the shelf band under the cursor, then
        // the cell within it whose span rect contains the point. Rects exclude the gutter, so a
        // cursor in the gap matches nothing and dispatches hover-out — same as the old gap reject.
        var shelfPitch = _layout.ShelfPitch(_unit, _gap);
        if (shelfPitch <= 0f)
        {
            DispatchHoverOut();
            return;
        }
        var shelf = Mathf.FloorToInt((contentY - _originY) / shelfPitch);
        if (shelf < 0 || shelf >= _layout.ShelfCount)
        {
            DispatchHoverOut();
            return;
        }

        var band = _layout.ShelfAt(shelf);
        var idx = -1;
        for (var candidate = band.FirstIndex; candidate <= band.LastIndex; candidate++)
        {
            if (
                _layout
                    .ContentRectFor(candidate, _unit, _gap, _originX, _originY)
                    .Contains(localX, contentY)
            )
            {
                idx = candidate;
                break;
            }
        }
        if (idx < 0)
        {
            DispatchHoverOut();
            return;
        }

        if (idx != _hoverPollIndex)
        {
            DispatchHoverOut();
            _hoverPollIndex = idx;
            _hoverDispatched = false;
        }

        // Move the display-case hover highlight to the pointed cell (board-local rect, y down).
        var hoverRect = _layout.ContentRectFor(idx, _unit, _gap, _originX, _originY);
        _slots?.SetHover(
            new CollectionGridRect(
                hoverRect.X,
                hoverRect.Y - _scrollY,
                hoverRect.Width,
                hoverRect.Height
            )
        );

        // Already fired OnHover for this cell — nothing to do until the cursor leaves.
        if (_hoverDispatched)
            return;

        // Successful acquisition means native SetUp and resize have already completed.
        if (_realized.TryGetValue(idx, out var cell))
        {
            cell.HoverRelay?.OnPointerEnter(null!);
            _hoverDispatched = true;
        }
    }

    private void DispatchHoverOut()
    {
        // Hide the display-case highlight on every hover-out path (outside the viewport, in a
        // gutter, off the grid, or moving to a new cell).
        _slots?.SetHover(null);
        if (_hoverPollIndex < 0 || !_hoverDispatched)
        {
            _hoverPollIndex = -1;
            _hoverDispatched = false;
            return;
        }
        if (_realized.TryGetValue(_hoverPollIndex, out var cell))
            cell.HoverRelay?.TryInvokeHoverOut();
        _hoverPollIndex = -1;
        _hoverDispatched = false;
    }

    private void TryRealize(int index)
    {
        var vm = _visible[index];
        if (_failedBindGuids.Contains(vm.Id))
            return;
        if (_realized.ContainsKey(index) || _pendingBinds.ContainsKey(index))
            return;

        var bindStartedAt = _firstWindowDiagnostics?.StartBind(index) ?? 0L;
        var cancellation = new CancellationTokenSource();
        var pending = new PendingBind(
            index,
            vm,
            _generation,
            bindStartedAt,
            cancellation,
            _pendingBindTracker.Register()
        );
        _pendingBinds[index] = pending;
        _ = BindAndRealizeAsync(pending);
    }

    private async Task BindAndRealizeAsync(PendingBind pending)
    {
        try
        {
            NativeCardAcquireResult acquireResult;
            try
            {
                acquireResult = await _previewScope.AcquireAsync(
                    BuildSubject(pending.Vm),
                    pending.Token
                );
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                BppLog.WarnEvent(
                    CollectionPanelLogEvents.CardBindDegraded,
                    ex,
                    CollectionPanelLogEvents.CardBindDegradedStage.Bind(
                        CollectionCardBindStage.Bind
                    ),
                    CollectionPanelLogEvents.CardBindDegradedTemplateId.Bind(pending.Vm.Id),
                    CollectionPanelLogEvents.CardBindDegradedReasonCode.Bind(
                        CollectionPanelLogReasonCode.BindException
                    )
                );
                return;
            }

            var session = acquireResult.Session;
            var staleOrCanceled =
                pending.IsCanceled
                || pending.Generation != _generation
                || _visible.Count <= pending.Index
                || !ReferenceEquals(_visible[pending.Index], pending.Vm);
            if (staleOrCanceled)
            {
                session?.Dispose();
                return;
            }

            _firstWindowDiagnostics?.RecordBind(
                pending.Index,
                pending.BindStartedAt,
                session != null
            );
            if (acquireResult.Failure?.Reason == NativeCardPreviewFailureReason.TemplateUnavailable)
                _failedBindGuids.Add(pending.Vm.Id);
            if (session == null)
                return;

            if (!TryAdoptSession(pending.Index, pending.Vm, session))
                session.Dispose();
        }
        finally
        {
            if (
                _pendingBinds.TryGetValue(pending.Index, out var current)
                && ReferenceEquals(current, pending)
            )
                _pendingBinds.Remove(pending.Index);
            pending.Dispose();
        }
    }

    private bool TryAdoptSession(int index, CollectionCardVm vm, INativeCardPreviewSession session)
    {
        if (_realized.ContainsKey(index))
            return false;

        var card = session.Root;
        var rect = session.Rect;

        var hover = card.GetComponent<CollectionCardHoverRelay>();
        if (hover == null)
            hover = card.AddComponent<CollectionCardHoverRelay>();
        hover.Bind(session);
        _sourceMatchesByCardId.TryGetValue(vm.Id, out var sourceMatches);
        CollectionSourceAttributionBadge.Bind(card, sourceMatches);

        if (!CollectionGridConstants.UsePolledHover)
            EnsureHitTarget(card);

        var cell = new RealizedCell(index, vm, session, ++_perCellGeneration, hover, rect);
        _realized[index] = cell;
        ApplyCellScale(index, cell);
        Reposition(index, cell);
        ShowCell(cell, _generation);
        return true;
    }

    private static NativeCardPreviewSubject BuildSubject(CollectionCardVm vm) =>
        new()
        {
            TemplateId = vm.Id,
            Tier = vm.StartingTier,
            DisplaySpan = vm.Type == ECardType.Skill ? 1 : CardSizeSpan.Resolve(vm.Size),
            InstanceIdPrefix = "bpp-collection",
        };

    // Scale the native card to fit its span cell, centered, never stretched. The cell is shrunk
    // by CellContentInset on every side so the slot background reads as a frame around the card.
    private void ApplyCellScale(int index, RealizedCell cell)
    {
        var rect = cell.CachedRect;
        if (rect == null)
            return;
        PrepareGridRect(rect);
        var cellRect = _layout.ContentRectFor(index, _unit, _gap, _originX, _originY);
        var inset = CollectionGridConstants.CellContentInset;
        var visualBounds = ResolveNativeVisualBounds(rect);
        var natW = visualBounds.Width;
        var natH = visualBounds.Height;

        // Scale to the cell HEIGHT so every card in a shelf renders the same height. Clamp item
        // cards by body width, not FrameContainer width: Large frame art has native side
        // flourishes that overhang the 3:2 body and should not make only Large cards shorter.
        var targetH = Mathf.Max(1f, cellRect.Height * (1f - 2f * inset));
        var scale = targetH / natH;
        var maxWidth = cellRect.Width + _gap;
        var fitter = rect.GetComponent<AspectRatioFitter>();
        var bodyH = natH / ItemBoardSocketLayout.FrameHeightOverSocket;
        var bodyW = natW;
        if (
            fitter != null
            && fitter.aspectRatio > 0.01f
            && !float.IsNaN(fitter.aspectRatio)
            && !float.IsInfinity(fitter.aspectRatio)
        )
        {
            bodyW = fitter.aspectRatio * bodyH;
        }
        if (bodyW * scale > maxWidth)
            scale = maxWidth / bodyW;
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
            scale = 1f;
        rect.localScale = new Vector3(scale, scale, 1f);
    }

    private void Reposition(int index, RealizedCell cell)
    {
        var rect = cell.CachedRect;
        if (rect == null)
            return;
        PrepareGridRect(rect);
        var cellRect = _layout.ContentRectFor(index, _unit, _gap, _originX, _originY);
        var screenTop = cellRect.Y - _scrollY;
        var visualBounds = ResolveNativeVisualBounds(rect);
        var targetCenter = new Vector2(
            cellRect.X + cellRect.Width * 0.5f,
            -(screenTop + cellRect.Height * 0.5f)
        );

        // Board pivot is top-left, so y goes negative. Place the measured native visual
        // center in the cell center; item-card root RectTransforms can report a zero rect.
        rect.anchoredPosition = new Vector2(
            targetCenter.x - visualBounds.Center.x * rect.localScale.x,
            targetCenter.y - visualBounds.Center.y * rect.localScale.y
        );
    }

    private static void PrepareGridRect(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static NativeVisualBounds ResolveNativeVisualBounds(RectTransform root)
    {
        var frame = FindDescendant(root, "FrameContainer");
        if (frame != null && TryMeasureSubtreeBounds(root, frame, out var frameBounds))
            return frameBounds;
        if (TryMeasureRawImageBounds(root, out var imageBounds))
            return imageBounds;
        if (TryResolveAspectRatioFallbackBounds(root, out var aspectBounds))
            return aspectBounds;

        var rootRect = root.rect;
        if (IsUsableNativeSize(rootRect.width, rootRect.height))
        {
            return new NativeVisualBounds(
                Mathf.Max(1f, Mathf.Abs(rootRect.width)),
                Mathf.Max(1f, Mathf.Abs(rootRect.height)),
                rootRect.center
            );
        }

        var sizeDelta = root.sizeDelta;
        if (IsUsableNativeSize(sizeDelta.x, sizeDelta.y))
        {
            return new NativeVisualBounds(
                Mathf.Max(1f, Mathf.Abs(sizeDelta.x)),
                Mathf.Max(1f, Mathf.Abs(sizeDelta.y)),
                Vector2.zero
            );
        }

        return new NativeVisualBounds(1f, 1f, Vector2.zero);
    }

    private static bool TryResolveAspectRatioFallbackBounds(
        RectTransform root,
        out NativeVisualBounds bounds
    )
    {
        var fitter = root.GetComponent<AspectRatioFitter>();
        if (
            fitter == null
            || fitter.aspectRatio <= 0.01f
            || float.IsNaN(fitter.aspectRatio)
            || float.IsInfinity(fitter.aspectRatio)
        )
        {
            bounds = default;
            return false;
        }

        var width = Mathf.Max(1f, FallbackNativeCardHeight * fitter.aspectRatio);
        var height = FallbackNativeCardHeight;
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        var rect = root.rect;
        bounds = new NativeVisualBounds(width, height, rect.center);
        return true;
    }

    private static bool TryMeasureRawImageBounds(RectTransform root, out NativeVisualBounds bounds)
    {
        var corners = new Vector3[4];
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        var found = false;

        foreach (var image in root.GetComponentsInChildren<RawImage>(true))
        {
            if (image == null || image.rectTransform == null)
                continue;
            AccumulateSingleRectBounds(
                root,
                image.rectTransform,
                corners,
                ref minX,
                ref minY,
                ref maxX,
                ref maxY,
                ref found
            );
        }

        if (!found || !IsUsableNativeSize(maxX - minX, maxY - minY))
        {
            bounds = default;
            return false;
        }

        bounds = new NativeVisualBounds(
            Mathf.Max(1f, maxX - minX),
            Mathf.Max(1f, maxY - minY),
            new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f)
        );
        return true;
    }

    private static bool TryMeasureSubtreeBounds(
        RectTransform root,
        Transform subtree,
        out NativeVisualBounds bounds
    )
    {
        var corners = new Vector3[4];
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        var found = false;

        AccumulateRectBounds(
            root,
            subtree,
            corners,
            ref minX,
            ref minY,
            ref maxX,
            ref maxY,
            ref found
        );
        if (!found || !IsUsableNativeSize(maxX - minX, maxY - minY))
        {
            bounds = default;
            return false;
        }

        bounds = new NativeVisualBounds(
            Mathf.Max(1f, maxX - minX),
            Mathf.Max(1f, maxY - minY),
            new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f)
        );
        return true;
    }

    private static void AccumulateSingleRectBounds(
        RectTransform root,
        RectTransform current,
        Vector3[] corners,
        ref float minX,
        ref float minY,
        ref float maxX,
        ref float maxY,
        ref bool found
    )
    {
        var rect = current.rect;
        if (!IsUsableNativeSize(rect.width, rect.height))
            return;

        current.GetWorldCorners(corners);
        for (var i = 0; i < corners.Length; i++)
        {
            var local = root.InverseTransformPoint(corners[i]);
            minX = Mathf.Min(minX, local.x);
            minY = Mathf.Min(minY, local.y);
            maxX = Mathf.Max(maxX, local.x);
            maxY = Mathf.Max(maxY, local.y);
        }
        found = true;
    }

    private static void AccumulateRectBounds(
        RectTransform root,
        Transform current,
        Vector3[] corners,
        ref float minX,
        ref float minY,
        ref float maxX,
        ref float maxY,
        ref bool found
    )
    {
        if (current is RectTransform currentRect)
            AccumulateSingleRectBounds(
                root,
                currentRect,
                corners,
                ref minX,
                ref minY,
                ref maxX,
                ref maxY,
                ref found
            );

        foreach (Transform child in current)
        {
            AccumulateRectBounds(
                root,
                child,
                corners,
                ref minX,
                ref minY,
                ref maxX,
                ref maxY,
                ref found
            );
        }
    }

    private static Transform? FindDescendant(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name)
                return child;

            var descendant = FindDescendant(child, name);
            if (descendant != null)
                return descendant;
        }

        return null;
    }

    private static bool IsUsableNativeSize(float width, float height) =>
        width > 0.01f
        && height > 0.01f
        && !float.IsNaN(width)
        && !float.IsNaN(height)
        && !float.IsInfinity(width)
        && !float.IsInfinity(height);

    private readonly struct NativeVisualBounds
    {
        public NativeVisualBounds(float width, float height, Vector2 center)
        {
            Width = width;
            Height = height;
            Center = center;
        }

        public float Width { get; }
        public float Height { get; }
        public Vector2 Center { get; }
    }

    // Push the visible window's board-local cell rects to the slot layer. Driven by the layout
    // window (not the realized-card set), so every visible cell shows its display-case slot
    // immediately, before its native card art has loaded. Board space is top-left origin, y
    // increasing downward, so content Y is offset by the current scroll.
    private void SyncSlots(int firstIdx, int lastIdx)
    {
        if (_slots == null)
            return;
        _slotRects.Clear();
        for (var idx = firstIdx; idx <= lastIdx; idx++)
        {
            var r = _layout.ContentRectFor(idx, _unit, _gap, _originX, _originY);
            _slotRects.Add(new CollectionGridRect(r.X, r.Y - _scrollY, r.Width, r.Height));
        }
        _slots.Sync(_slotRects);
    }

    private static void EnsureHitTarget(GameObject host)
    {
        var hit = host.GetComponent<Image>();
        if (hit == null)
        {
            hit = host.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            // Push the hit image to the back of the sibling list so it does not paint over
            // the card frame/art; raycastTarget is unaffected by sibling order.
            hit.transform.SetAsFirstSibling();
        }
        else
        {
            hit.raycastTarget = true;
        }
    }

    private void ShowCell(RealizedCell cell, int generationSnapshot)
    {
        if (generationSnapshot != _generation)
            return;

        var root = cell.Session.Root;
        if (root == null)
            return;
        try
        {
            root.SetActive(true);
            var show = cell.Session.Show();
            if (show.Status == NativePreviewActionStatus.Failed)
                return;
            ApplyCellScale(cell.Index, cell);
            Reposition(cell.Index, cell);
            // Show(true) re-activates _cardImage / _frameContainer; the CanvasGroup at the
            // root was zeroed on Take, so the card still renders transparent. Hand the cell
            // off to TickFades to ramp it up.
            cell.FadeAlpha = 0f;
            cell.FadeActive = true;
            cell.IsShown = true;
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                CollectionPanelLogEvents.CardDisplayFailed,
                ex,
                () =>
                    [
                        CollectionPanelLogEvents.CardDisplayFailedStage.Bind(
                            CollectionCardDisplayStage.Show
                        ),
                        CollectionPanelLogEvents.CardDisplayFailedTemplateId.Bind(cell.Vm.Id),
                    ]
            );
        }
    }

    // Advance the per-cell fade-in animation. Called from CollectionPanel.Update each frame
    // while the panel is visible. Acquisition has completed native SetUp before cells reach here.
    public void TickFades(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
            return;
        var t = 1f - Mathf.Exp(-deltaSeconds / CollectionGridConstants.CardFadeInSeconds);
        foreach (var pair in _realized)
        {
            var cell = pair.Value;
            var root = cell.Session.Root;
            if (!cell.FadeActive || root == null)
                continue;
            cell.FadeAlpha = Mathf.Lerp(cell.FadeAlpha, 1f, t);
            if (cell.FadeAlpha >= 0.995f)
            {
                cell.FadeAlpha = 1f;
                cell.FadeActive = false;
            }
            var canvasGroup = root.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
                canvasGroup.alpha = cell.FadeAlpha;
        }
    }

    private void RecycleCell(RealizedCell cell)
    {
        cell.HoverRelay?.Clear();
        cell.Session.Dispose();
    }

    private void RecycleAll()
    {
        foreach (var pair in _realized)
            RecycleCell(pair.Value);
        _realized.Clear();
    }

    private IReadOnlyDictionary<int, int> BuildRetentionPlan(
        IReadOnlyList<CollectionCardVm>? nextVisible
    )
    {
        var realizedCardIdsByIndex = new Dictionary<int, Guid>(_realized.Count);
        foreach (var pair in _realized)
        {
            var cell = pair.Value;
            if (!cell.IsShown)
                continue;
            realizedCardIdsByIndex[pair.Key] = cell.Vm.Id;
        }

        nextVisible ??= Array.Empty<CollectionCardVm>();
        var nextVisibleCardIds = new Guid[nextVisible.Count];
        for (var index = 0; index < nextVisible.Count; index++)
            nextVisibleCardIds[index] = nextVisible[index].Id;

        var plannedRetention = CollectionGridRetentionPlan.Build(
            realizedCardIdsByIndex,
            nextVisibleCardIds
        );
        var retention = new Dictionary<int, int>(plannedRetention.Count);
        foreach (var pair in plannedRetention)
        {
            var cell = _realized[pair.Key];
            var newIndex = pair.Value;
            if (ReferenceEquals(cell.Vm, nextVisible[newIndex]))
                retention[pair.Key] = newIndex;
        }

        return retention;
    }

    private void RetainShownCells(IReadOnlyDictionary<int, int> retention)
    {
        DispatchHoverOut();
        var previousCells = new List<KeyValuePair<int, RealizedCell>>(_realized);
        _realized.Clear();

        foreach (var pair in previousCells)
        {
            var cell = pair.Value;
            // A retained card can be destroyed out from under us; recycle it like the
            // pre-retention path did
            // instead of dereferencing a dead GameObject and orphaning the rest of the loop.
            if (!retention.TryGetValue(pair.Key, out var newIndex) || cell.Session.Root == null)
            {
                RecycleCell(cell);
                continue;
            }

            cell.HoverRelay?.Clear();
            cell.HoverRelay?.Bind(cell.Session);
            cell.Index = newIndex;
            cell.Vm = _visible[newIndex];
            _sourceMatchesByCardId.TryGetValue(cell.Vm.Id, out var sourceMatches);
            CollectionSourceAttributionBadge.Bind(cell.Session.Root, sourceMatches);
            _realized[newIndex] = cell;
            ApplyCellScale(newIndex, cell);
            Reposition(newIndex, cell);
        }
    }

    private void CancelPendingBind(int index)
    {
        if (!_pendingBinds.Remove(index, out var pending))
            return;

        pending.Cancel();
    }

    private void CancelPendingBinds()
    {
        foreach (var pair in _pendingBinds)
            pair.Value.Cancel();
        _pendingBinds.Clear();
    }

    private void BumpGeneration()
    {
        _generation++;
        CancelPendingBinds();
        _failedBindGuids.Clear();
    }

    // Derive the per-viewport base unit and display-case origin. The grid width is shared across
    // tabs first, then the active tab's column count maps that envelope to a unit size; once the
    // shared width clamps, any surplus width becomes centering margin rather than extra columns.
    // originY is the fixed top padding. Marks realized cards for rescale on the next reposition.
    private void RecomputePixelization()
    {
        _scaleDirty = true;
        var columns = _layout.Columns;
        var pixels = CollectionGridPixelization.ForViewport(
            _viewportWidth,
            columns,
            _layout.MaxUnitWidth
        );
        _unit = pixels.Unit;
        _originX = pixels.OriginX;
        _originY = pixels.OriginY;
    }

    private sealed class RealizedCell
    {
        public RealizedCell(
            int index,
            CollectionCardVm vm,
            INativeCardPreviewSession session,
            int generation,
            CollectionCardHoverRelay hoverRelay,
            RectTransform cachedRect
        )
        {
            Index = index;
            Vm = vm;
            Session = session;
            Generation = generation;
            HoverRelay = hoverRelay;
            CachedRect = cachedRect;
        }

        public int Index { get; set; }
        public CollectionCardVm Vm { get; set; }
        public INativeCardPreviewSession Session { get; }
        public int Generation { get; }
        public CollectionCardHoverRelay HoverRelay { get; }
        public RectTransform CachedRect { get; }
        public bool IsShown { get; set; }

        // Fade state. ShowCell sets FadeActive=true with FadeAlpha=0 right after the
        // card's Show(true); TickFades ramps FadeAlpha → 1 and writes it to the CanvasGroup.
        public bool FadeActive { get; set; }
        public float FadeAlpha { get; set; }
    }

    private sealed class PendingBind
    {
        private readonly CancellationTokenSource _cancellation;

        public PendingBind(
            int index,
            CollectionCardVm vm,
            int generation,
            long bindStartedAt,
            CancellationTokenSource cancellation,
            PendingBindTracker.PendingBindOperation operation
        )
        {
            Index = index;
            Vm = vm;
            Generation = generation;
            BindStartedAt = bindStartedAt;
            _cancellation = cancellation;
            Operation = operation;
            Token = cancellation.Token;
        }

        public int Index { get; }
        public CollectionCardVm Vm { get; }
        public int Generation { get; }
        public long BindStartedAt { get; }
        public CancellationToken Token { get; }
        public bool IsCanceled => Volatile.Read(ref _canceled) != 0;
        private PendingBindTracker.PendingBindOperation Operation { get; }
        private int _canceled;

        public void Cancel()
        {
            Interlocked.Exchange(ref _canceled, 1);
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Completion can race with scroll-window cancellation.
            }
        }

        public void Dispose()
        {
            _cancellation.Dispose();
            Operation.Complete();
        }
    }

    private sealed class PendingBindTracker
    {
        private readonly object _syncRoot = new();
        private TaskCompletionSource<bool>? _settled;
        private int _pendingCount;

        public bool HasPendingBinds
        {
            get
            {
                lock (_syncRoot)
                    return _pendingCount > 0;
            }
        }

        public Task WhenSettled
        {
            get
            {
                lock (_syncRoot)
                    return _pendingCount == 0 ? Task.CompletedTask : _settled!.Task;
            }
        }

        public PendingBindOperation Register()
        {
            lock (_syncRoot)
            {
                if (_pendingCount == 0)
                    _settled = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
                _pendingCount++;
            }

            return new PendingBindOperation(this);
        }

        private void Complete()
        {
            TaskCompletionSource<bool>? settled = null;
            lock (_syncRoot)
            {
                if (_pendingCount <= 0)
                    return;

                _pendingCount--;
                if (_pendingCount == 0)
                    settled = _settled;
            }

            settled?.TrySetResult(true);
        }

        public sealed class PendingBindOperation
        {
            private readonly PendingBindTracker _tracker;
            private int _completed;

            public PendingBindOperation(PendingBindTracker tracker)
            {
                _tracker = tracker;
            }

            public void Complete()
            {
                if (Interlocked.Exchange(ref _completed, 1) == 0)
                    _tracker.Complete();
            }
        }
    }

    private sealed class FirstWindowBindDiagnostics
    {
        private readonly int _generation;
        private readonly HashSet<int> _completedIndices = new();

        private long _startedAt;
        private int _firstIndex = -1;
        private int _lastIndex = -1;
        private int _visibleCount;
        private int _shelfCount;
        private int _attempts;
        private int _bound;
        private int _failed;
        private double _bindMs;
        private bool _bindingLogged;

        public FirstWindowBindDiagnostics(int generation)
        {
            _generation = generation;
        }

        public void EnsureWindow(int firstIndex, int lastIndex, int visibleCount, int shelfCount)
        {
            if (_startedAt != 0L || firstIndex < 0 || lastIndex < firstIndex)
                return;

            _startedAt = Stopwatch.GetTimestamp();
            _firstIndex = firstIndex;
            _lastIndex = lastIndex;
            _visibleCount = visibleCount;
            _shelfCount = shelfCount;
        }

        public long StartBind(int index)
        {
            return Covers(index) ? Stopwatch.GetTimestamp() : 0L;
        }

        public void RecordBind(int index, long startedAt, bool bound)
        {
            if (startedAt == 0L || !Covers(index) || !_completedIndices.Add(index))
                return;

            _attempts++;
            _bindMs += ElapsedMs(startedAt, Stopwatch.GetTimestamp());
            if (bound)
                _bound++;
            else
                _failed++;
        }

        public void TryLogBindingPhase(int currentGeneration)
        {
            if (
                _bindingLogged
                || currentGeneration != _generation
                || _startedAt == 0L
                || _completedIndices.Count < WindowSize
            )
            {
                return;
            }

            _bindingLogged = true;
            var elapsedMs = ElapsedMs(_startedAt, Stopwatch.GetTimestamp());
            BppLog.DebugEvent(
                CollectionPanelLogEvents.GridPerformanceObserved,
                () =>
                    [
                        CollectionPanelLogEvents.GridPerformancePhase.Bind(
                            CollectionGridPerformancePhase.FirstWindowBind
                        ),
                        CollectionPanelLogEvents.GridPerformanceFirstIndex.Bind(_firstIndex),
                        CollectionPanelLogEvents.GridPerformanceLastIndex.Bind(_lastIndex),
                        CollectionPanelLogEvents.GridPerformanceWindowCount.Bind(WindowSize),
                        CollectionPanelLogEvents.GridPerformanceVisibleCount.Bind(_visibleCount),
                        CollectionPanelLogEvents.GridPerformanceShelfCount.Bind(_shelfCount),
                        CollectionPanelLogEvents.GridPerformanceAttemptCount.Bind(_attempts),
                        CollectionPanelLogEvents.GridPerformanceBoundCount.Bind(_bound),
                        CollectionPanelLogEvents.GridPerformanceFailedBindCount.Bind(_failed),
                        CollectionPanelLogEvents.GridPerformanceBindDurationMs.Bind(_bindMs),
                        CollectionPanelLogEvents.GridPerformanceElapsedMs.Bind(elapsedMs),
                        CollectionPanelLogEvents.GridPerformanceFaultedCount.Bind(null),
                        CollectionPanelLogEvents.GridPerformanceCanceledCount.Bind(null),
                    ]
            );
        }

        private bool Covers(int index) =>
            !_bindingLogged && _startedAt != 0L && index >= _firstIndex && index <= _lastIndex;

        private int WindowSize => _lastIndex >= _firstIndex ? _lastIndex - _firstIndex + 1 : 0;

        private static double ElapsedMs(long start, long end) =>
            (end - start) * 1000.0 / Stopwatch.Frequency;
    }
}
