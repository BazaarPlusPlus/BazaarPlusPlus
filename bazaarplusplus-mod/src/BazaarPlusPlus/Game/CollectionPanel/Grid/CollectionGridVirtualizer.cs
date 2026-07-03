#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Recycler virtualizer: instead of laying out every card in the visible set up front, we
// only realize cells inside the scroll window (+ overscan) and keep ~realizedRows*cols
// CardPreviewBase instances alive at a time. Scrolling rewrites anchoredPosition on the
// existing cells (O(visible)); only when a cell index moves out of the window do we destroy
// its native card and request a fresh one for the cell entering it.
//
// Cancellation race: AssetLoader creates native card UI asynchronously. The factory returns
// a binding immediately; ShowWhenReady awaits it and checks the generation before binding
// hover/source adornments or flipping the cell active. If a cell is recycled mid-create,
// the late card is destroyed instead of reused.
//
// Filter/tab changes cancel everything in flight by bumping the global generation guard
// and re-seeding the visible set; in-progress SetUp tasks complete (their continuations
// no-op because the generation has moved), and the realized cells are recycled.
internal sealed class CollectionGridVirtualizer
{
    private readonly CollectionGridOverlay _overlay;
    private readonly CollectionCardFactory _factory;
    private readonly Dictionary<int, RealizedCell> _realized = new();
    private readonly List<int> _recycleScratch = new();
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

    public CollectionGridVirtualizer(CollectionGridOverlay overlay, CollectionCardFactory factory)
    {
        _overlay = overlay;
        _factory = factory;
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

    // SetVisible swaps in a new ordered visible set (typically after filter change) and
    // recycles everything currently realized. Caller is expected to also reset scrollY to 0.
    public void SetVisible(
        IReadOnlyList<CollectionCardVm> visible,
        ECardType activeType,
        IReadOnlyDictionary<
            Guid,
            IReadOnlyList<CollectionSourceOfferMatch>
        >? sourceMatchesByCardId = null
    )
    {
        BumpGeneration();
        _visible = visible ?? Array.Empty<CollectionCardVm>();
        _sourceMatchesByCardId =
            sourceMatchesByCardId
            ?? new Dictionary<Guid, IReadOnlyList<CollectionSourceOfferMatch>>();
        _gap = CollectionGridConstants.GridGap;
        _layout = CollectionGridLayout.Build(_visible, activeType);
        RecomputePixelization();
        RecycleAll();
        _slots?.Clear();
        _lastScrollY = float.NaN;
        _firstWindowDiagnostics =
            BppBuild.IsDebug && _visible.Count > 0
                ? new FirstWindowBindDiagnostics(_generation, _factory.Stats.Snapshot())
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

        // 2) Realize newly-visible cells, rate-limited by wall-clock budget for cold binds.
        var tickStart = Time.realtimeSinceStartup;
        var coldBudgetSeconds = CollectionGridConstants.ColdBindBudgetMs * 0.001f;
        for (var idx = firstIdx; idx <= lastIdx; idx++)
        {
            if (_realized.ContainsKey(idx))
                continue;
            if (Time.realtimeSinceStartup - tickStart > coldBudgetSeconds)
                break;
            TryRealize(idx);
        }
        _firstWindowDiagnostics?.TryLogBindingPhase(_generation, _factory.Stats.Snapshot());

        foreach (var pair in _realized)
            TryActivateReadyCell(pair.Value);

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
                if (pair.Value.LayoutReady)
                    ApplyCellTransform(pair.Key, pair.Value);
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
    // Dispatch is deferred until the cell's SetUp Task completes successfully — without
    // this gate the cursor could land on a still-loading cell whose SetUp threw before
    // CreateTooltipData ran, and OnHover would NRE reading _tooltipData. _hoverDispatched
    // remembers whether we already fired OnHover for the current cell so subsequent frames
    // with the same idx retry until the cell becomes ready.
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

        // Cell may be realized before AssetLoader has finished creating its native card.
        // Wait for a clean completion before dispatching, and retry on subsequent frames if
        // not yet ready.
        if (_realized.TryGetValue(idx, out var cell) && cell.SetUpTask.IsCompletedSuccessfully)
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
        var bindStartedAt = _firstWindowDiagnostics?.StartBind(index) ?? 0L;
        var binding = _factory.TryBind(vm);
        _firstWindowDiagnostics?.RecordBind(index, bindStartedAt, binding);
        if (binding == null)
            return;

        var cell = new RealizedCell(
            index,
            vm,
            binding,
            ++_perCellGeneration
        );
        _realized[index] = cell;
    }

    // Measure the native card's visible frame once per bind. Scrolling then applies a cheap
    // cached host transform instead of forcing a Canvas rebuild for every visible card.
    private bool EnsureFrameMetrics(RealizedCell cell)
    {
        var host = cell.HostRect;
        var frame = cell.FrameRect;
        if (host == null || frame == null)
            return false;

        if (cell.Binding.FrameMetrics.HasValue)
            return true;

        host.localScale = Vector3.one;
        Canvas.ForceUpdateCanvases();

        var bounds = MeasureLocalBounds(frame, host);
        if (bounds.Width <= 0f || bounds.Height <= 0f)
            return false;

        cell.Binding.SetFrameMetrics(
            new CollectionCardFrameMetrics(
                bounds.Width,
                bounds.Height,
                new Vector2(bounds.CenterX, bounds.CenterY)
            )
        );
        return true;
    }

    private void ApplyCellTransform(int index, RealizedCell cell)
    {
        var host = cell.HostRect;
        var metrics = cell.Binding.FrameMetrics;
        if (host == null || !metrics.HasValue)
            return;

        var cellRect = _layout.ContentRectFor(index, _unit, _gap, _originX, _originY);
        var inset = CollectionGridConstants.CellContentInset;
        var m = metrics.Value;

        // Scale to the visible frame HEIGHT so every card in a shelf renders the same height.
        // Clamp by visible frame width + one gutter, which preserves the old span behavior while
        // removing the stale assumption that the root RectTransform is the card face.
        var targetH = Mathf.Max(1f, cellRect.Height * (1f - 2f * inset));
        var scale = targetH / m.Height;
        var maxWidth = cellRect.Width + _gap;
        if (m.Width * scale > maxWidth)
            scale = maxWidth / m.Width;
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
            scale = 1f;

        host.anchorMin = new Vector2(0f, 1f);
        host.anchorMax = new Vector2(0f, 1f);
        host.pivot = new Vector2(0.5f, 0.5f);
        host.localScale = new Vector3(scale, scale, 1f);

        var screenTop = cellRect.Y - _scrollY;
        host.anchoredPosition = new Vector2(
            cellRect.X + cellRect.Width * 0.5f - m.CenterOffset.x * scale,
            -(screenTop + cellRect.Height * 0.5f) - m.CenterOffset.y * scale
        );
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

    private void TryActivateReadyCell(RealizedCell cell)
    {
        if (cell.ActivationAttempted)
            return;
        if (!cell.SetUpTask.IsCompleted)
            return;
        if (!cell.SetUpTask.IsCompletedSuccessfully)
        {
            cell.ActivationAttempted = true;
            var reason = cell.SetUpTask.Exception?.GetBaseException().Message ?? "not successful";
            BppLog.Debug(
                "CollectionGridVirtualizer",
                $"SetUp task for {cell.Vm.Id} faulted: {reason}"
            );
            return;
        }

        if (cell.Card == null)
            return;
        try
        {
            if (!BindReadyCell(cell))
                return;

            cell.Card.gameObject.SetActive(true);
            NativeCardPreviewRuntime.Show(
                cell.Card,
                show: true,
                logComponent: "CollectionGridVirtualizer"
            );
            if (!EnsureFrameMetrics(cell))
                return;
            ApplyCellTransform(cell.Index, cell);
            cell.ActivationAttempted = true;
            cell.FadeAlpha = 0f;
            cell.FadeActive = true;
            cell.LayoutReady = true;
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CollectionGridVirtualizer",
                $"Show invocation for {cell.Vm.Id} failed: {ex.Message}"
            );
        }
    }

    private bool BindReadyCell(RealizedCell cell)
    {
        var card = cell.Card;
        var rect = cell.CachedRect;
        if (card == null || rect == null || cell.HostRect == null)
            return false;

        var hover = card.gameObject.GetComponent<CollectionCardHoverRelay>();
        if (hover == null)
            hover = card.gameObject.AddComponent<CollectionCardHoverRelay>();
        hover.Bind(card);
        cell.HoverRelay = hover;
        cell.Binding.HoverRelay = hover;

        _sourceMatchesByCardId.TryGetValue(cell.Vm.Id, out var sourceMatches);
        CollectionSourceAttributionBadge.Bind(card.gameObject, sourceMatches);

        if (!CollectionGridConstants.UsePolledHover)
            EnsureHitTarget(card.gameObject);

        return true;
    }

    private void RecycleCell(RealizedCell cell)
    {
        cell.HoverRelay?.Clear();
        CompleteRecycle(cell);
    }

    private void CompleteRecycle(RealizedCell cell)
    {
        cell.HoverRelay?.Clear();
        _factory.Return(cell.Binding);
    }

    public void TickFades(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
            return;

        var t = 1f - Mathf.Exp(-deltaSeconds / CollectionGridConstants.CardFadeInSeconds);
        foreach (var pair in _realized)
        {
            var cell = pair.Value;
            if (!cell.FadeActive)
                continue;

            cell.FadeAlpha = Mathf.Lerp(cell.FadeAlpha, 1f, t);
            if (cell.FadeAlpha >= 0.995f)
            {
                cell.FadeAlpha = 1f;
                cell.FadeActive = false;
            }

            if (cell.Binding.CanvasGroup != null)
                cell.Binding.CanvasGroup.alpha = cell.FadeAlpha;
        }
    }

    private void RecycleAll()
    {
        foreach (var pair in _realized)
            RecycleCell(pair.Value);
        _realized.Clear();
    }

    private void BumpGeneration() => _generation++;

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

    private static LocalBounds MeasureLocalBounds(RectTransform rect, Transform relativeTo)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var first = relativeTo.InverseTransformPoint(corners[0]);
        var minX = first.x;
        var maxX = first.x;
        var minY = first.y;
        var maxY = first.y;

        for (var i = 1; i < corners.Length; i++)
        {
            var local = relativeTo.InverseTransformPoint(corners[i]);
            minX = Mathf.Min(minX, local.x);
            maxX = Mathf.Max(maxX, local.x);
            minY = Mathf.Min(minY, local.y);
            maxY = Mathf.Max(maxY, local.y);
        }

        return new LocalBounds(minX, minY, maxX - minX, maxY - minY);
    }

    private readonly struct LocalBounds
    {
        public LocalBounds(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
        public float CenterX => X + Width * 0.5f;
        public float CenterY => Y + Height * 0.5f;
    }

    private sealed class RealizedCell
    {
        public RealizedCell(
            int index,
            CollectionCardVm vm,
            CollectionCardBinding binding,
            int generation
        )
        {
            Index = index;
            Vm = vm;
            Binding = binding;
            Generation = generation;
        }

        public int Index { get; }
        public CollectionCardVm Vm { get; }
        public CollectionCardBinding Binding { get; }
        public Component? Card => Binding.Card;
        public Task SetUpTask => Binding.SetUpTask;
        public int Generation { get; }
        public CollectionCardHoverRelay? HoverRelay { get; set; }
        public RectTransform? CachedRect => Binding.Rect;
        public RectTransform? HostRect => Binding.Host;
        public RectTransform? FrameRect => Binding.Frame;
        public bool ActivationAttempted { get; set; }
        public bool LayoutReady { get; set; }
        public bool FadeActive { get; set; }
        public float FadeAlpha { get; set; }
    }

    private sealed class FirstWindowBindDiagnostics
    {
        private readonly int _generation;
        private readonly HashSet<int> _completedIndices = new();
        private readonly List<Task> _setUpTasks = new();

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
        private bool _setUpLogStarted;

        private readonly CollectionCardFactoryStatsSnapshot _startedStats;

        public FirstWindowBindDiagnostics(
            int generation,
            CollectionCardFactoryStatsSnapshot startedStats
        )
        {
            _generation = generation;
            _startedStats = startedStats;
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

        public void RecordBind(int index, long startedAt, CollectionCardBinding? binding)
        {
            if (startedAt == 0L || !Covers(index) || !_completedIndices.Add(index))
                return;

            _attempts++;
            _bindMs += ElapsedMs(startedAt, Stopwatch.GetTimestamp());
            if (binding != null)
            {
                _bound++;
                _setUpTasks.Add(binding.SetUpTask);
            }
            else
            {
                _failed++;
            }
        }

        public void TryLogBindingPhase(
            int currentGeneration,
            CollectionCardFactoryStatsSnapshot currentStats
        )
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
            var deltaStats = currentStats.DeltaFrom(_startedStats);
            BppLog.Debug(
                "CollectionGridVirtualizer",
                "firstWindowBind "
                    + $"range={_firstIndex}-{_lastIndex} "
                    + $"window={WindowSize} "
                    + $"visible={_visibleCount} "
                    + $"shelves={_shelfCount} "
                    + $"attempts={_attempts} "
                    + $"bound={_bound} "
                    + $"failed={_failed} "
                    + $"coldCreates={deltaStats.ColdCreates} "
                    + $"poolReuses={deltaStats.PoolReuses} "
                    + $"pendingReturns={deltaStats.PendingReturns} "
                    + $"rebindFaults={deltaStats.RebindFaults} "
                    + $"bindMs={FormatMs(_bindMs)} "
                    + $"elapsedMs={FormatMs(elapsedMs)}"
            );

            if (_setUpTasks.Count > 0 && !_setUpLogStarted)
            {
                _setUpLogStarted = true;
                _ = LogSetUpCompletionAsync(
                    _setUpTasks.ToArray(),
                    _startedAt,
                    WindowSize,
                    _bound,
                    _failed
                );
            }
        }

        private bool Covers(int index) =>
            !_bindingLogged && _startedAt != 0L && index >= _firstIndex && index <= _lastIndex;

        private int WindowSize => _lastIndex >= _firstIndex ? _lastIndex - _firstIndex + 1 : 0;

        private static async Task LogSetUpCompletionAsync(
            Task[] tasks,
            long startedAt,
            int windowSize,
            int bound,
            int failedBinds
        )
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
                // Faulted tasks are counted below; ShowWhenReady owns the per-card debug log.
            }

            var faulted = 0;
            var canceled = 0;
            foreach (var task in tasks)
            {
                if (task.IsFaulted)
                    faulted++;
                else if (task.IsCanceled)
                    canceled++;
            }

            BppLog.Debug(
                "CollectionGridVirtualizer",
                "firstWindowSetUp "
                    + $"window={windowSize} "
                    + $"bound={bound} "
                    + $"failedBinds={failedBinds} "
                    + $"faulted={faulted} "
                    + $"canceled={canceled} "
                    + $"artAndSetupElapsedMs={FormatMs(ElapsedMs(startedAt, Stopwatch.GetTimestamp()))}"
            );
        }

        private static double ElapsedMs(long start, long end) =>
            (end - start) * 1000.0 / Stopwatch.Frequency;

        private static string FormatMs(double value) =>
            value.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
