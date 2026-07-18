#nullable enable
using System.Collections;
using BazaarPlusPlus.GameInterop.CardPreview;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal sealed class ItemBoardPreviewSurface : IDisposable
{
    private readonly INativeCardPreviewHost _previewHost;
    private readonly ItemBoardPreviewGenerationGuard _generation = new();
    private readonly List<ActiveCard> _active = new();

    private ItemBoardPreviewOptions _options = new();
    private CancellationTokenSource? _loadCancellation;
    private GameObject? _root;
    private Canvas? _canvas;
    private CanvasGroup? _canvasGroup;
    private RectTransform? _rootRect;
    private RectTransform? _clipRect;
    private RectTransform? _boardRect;
    private RectTransform[]? _sockets;
    private INativeCardPreviewScope? _scope;
    private INativeCardPreviewSession? _hoveredSession;
    private string? _renderedSignature;
    private Vector2 _position = Vector2.zero;
    private Vector2 _clipSize = new(
        ItemBoardSocketLayout.NativeBoardWidth,
        ItemBoardSocketLayout.NativeBoardHeight
    );
    private float _cardScale = 1f;
    private int _runtimeLayer = int.MinValue;

    internal ItemBoardPreviewSurface(INativeCardPreviewHost previewHost) =>
        _previewHost = previewHost ?? throw new ArgumentNullException(nameof(previewHost));

    public bool EnsureInitialized()
    {
        return EnsureInitialized(_options);
    }

    public void SetPosition(Vector2 screenBottomLeft)
    {
        var rounded = new Vector2(Mathf.Round(screenBottomLeft.x), Mathf.Round(screenBottomLeft.y));
        if (
            Mathf.Approximately(_position.x, rounded.x)
            && Mathf.Approximately(_position.y, rounded.y)
        )
        {
            return;
        }

        _position = rounded;
        ApplyTransform();
    }

    public void SetClipSize(Vector2 pixels)
    {
        var rounded = new Vector2(
            Mathf.Max(1f, Mathf.Round(pixels.x)),
            Mathf.Max(1f, Mathf.Round(pixels.y))
        );
        if (
            Mathf.Approximately(_clipSize.x, rounded.x)
            && Mathf.Approximately(_clipSize.y, rounded.y)
        )
        {
            return;
        }

        _clipSize = rounded;
        ApplyTransform();
        _renderedSignature = null;
    }

    public bool SetCardScale(float scale)
    {
        var clamped = Mathf.Max(0.05f, scale);
        if (Mathf.Approximately(_cardScale, clamped))
            return false;

        _cardScale = clamped;
        ApplyTransform();
        _renderedSignature = null;
        return true;
    }

    public IEnumerator Render(
        IReadOnlyList<NativeCardPreviewSubject>? cards,
        ItemBoardPreviewOptions options,
        string? signature = null,
        Action<ItemBoardPreviewPhase>? onPhase = null,
        Action? onComplete = null
    )
    {
        _options = options ?? new ItemBoardPreviewOptions();
        CancelPending();

        if (cards == null || cards.Count == 0)
        {
            onPhase?.Invoke(ItemBoardPreviewPhase.Empty);
            Hide();
            onComplete?.Invoke();
            yield break;
        }

        if (!EnsureInitialized(_options))
        {
            onPhase?.Invoke(ItemBoardPreviewPhase.InitFailed);
            onComplete?.Invoke();
            yield break;
        }

        if (
            !string.IsNullOrEmpty(_renderedSignature)
            && !string.IsNullOrEmpty(signature)
            && string.Equals(_renderedSignature, signature, StringComparison.Ordinal)
        )
        {
            onPhase?.Invoke(ItemBoardPreviewPhase.Done);
            onComplete?.Invoke();
            yield break;
        }

        var snapshot = _generation.Bump();
        var loadCancellation = new CancellationTokenSource();
        _loadCancellation = loadCancellation;
        var token = loadCancellation.Token;

        onPhase?.Invoke(ItemBoardPreviewPhase.Loading);
        _root!.SetActive(true);
        if (_canvasGroup != null)
            _canvasGroup.alpha = 1f;

        ApplyTransform();
        ReturnActiveCardsToPool();
        var scope = _scope!;
        var sockets = _sockets!;
        var aggregate = CreateCardsForRenderAsync(cards, scope, sockets, snapshot, token);
        var claimed = false;
        var completedLoad = false;

        try
        {
            while (
                !aggregate.IsCompleted
                && _generation.IsCurrent(snapshot)
                && !token.IsCancellationRequested
            )
            {
                yield return null;
            }

            if (!_generation.IsCurrent(snapshot) || token.IsCancellationRequested)
                yield break;

            var creation = aggregate.GetAwaiter().GetResult();
            claimed = true;
            _active.AddRange(creation.Cards);
            if (_active.Count == 0)
            {
                onPhase?.Invoke(ItemBoardPreviewPhase.Empty);
                Hide();
                onComplete?.Invoke();
                yield break;
            }

            var showFailed = ShowSetUpCards();
            Canvas.ForceUpdateCanvases();

            yield return null;
            if (!_generation.IsCurrent(snapshot))
                yield break;

            Canvas.ForceUpdateCanvases();
            if (_options.LayoutMode == ItemBoardPreviewLayoutMode.SlotGrid)
                LayoutCardsSlotGrid();
            else if (_options.LayoutMode == ItemBoardPreviewLayoutMode.Packed)
                LayoutCardsPacked();

            if (
                ItemBoardPreviewSignatureGate.ShouldCache(aggregate)
                && !creation.HadFailures
                && !showFailed
            )
                _renderedSignature = signature;

            CompleteLoad(loadCancellation);
            completedLoad = true;
            onPhase?.Invoke(ItemBoardPreviewPhase.Done);
            onComplete?.Invoke();
        }
        finally
        {
            if (!completedLoad)
            {
                if (claimed)
                {
                    CompleteLoad(loadCancellation);
                }
                else
                {
                    CancelAndDisposeLoad(loadCancellation);
                    _ = DisposeCreationWhenReadyAsync(aggregate);
                }
            }
        }
    }

    public void PollHover(Vector2 mousePixels)
    {
        if (!_options.ShowHover || _root == null || !_root.activeSelf || _clipRect == null)
        {
            DispatchHoverOut();
            return;
        }

        var clipBounds = new Rect(_position.x, _position.y, _clipSize.x, _clipSize.y);
        if (!clipBounds.Contains(mousePixels))
        {
            DispatchHoverOut();
            return;
        }

        var next = FindHoveredSession(mousePixels);
        if (ReferenceEquals(next, _hoveredSession))
            return;

        DispatchHoverOut();
        if (next == null)
            return;

        _hoveredSession = next;
        next.HoverEnter();
    }

    public void Hide()
    {
        CancelPending();
        _renderedSignature = null;
        ReturnActiveCardsToPool();
        if (_canvasGroup != null)
            _canvasGroup.alpha = 0f;
        if (_root != null)
            _root.SetActive(false);
    }

    public void CancelPending()
    {
        _generation.Bump();
        var cancellation = _loadCancellation;
        _loadCancellation = null;
        if (cancellation != null)
            CancelAndDispose(cancellation);

        DispatchHoverOut();
    }

    public void Dispose()
    {
        CancelPending();
        _renderedSignature = null;
        DisposeRuntimeObjects();
        _runtimeLayer = int.MinValue;
    }

    private bool EnsureInitialized(ItemBoardPreviewOptions options)
    {
        if (_runtimeLayer != int.MinValue && _runtimeLayer != options.Layer)
            DisposeRuntimeObjects();

        _runtimeLayer = options.Layer;

        if (
            _root != null
            && _canvas != null
            && _rootRect != null
            && _clipRect != null
            && _boardRect != null
            && _sockets != null
        )
        {
            ApplyOptions(options);
            ApplyTransform();
            _scope ??= _previewHost.OpenScope(
                new ItemBoardNativeCardPreviewOwner(_sockets, options)
            );
            return true;
        }

        DisposeRuntimeObjects();
        _runtimeLayer = options.Layer;

        _root = new GameObject("BppItemBoardPreviewSurface", typeof(RectTransform), typeof(Canvas));
        _root.layer = options.Layer;
        _root.SetActive(false);

        _canvas = _root.GetComponent<Canvas>();
        _rootRect = _root.GetComponent<RectTransform>();
        ApplyOptions(options);

        var clipObject = new GameObject(
            "BppItemBoardPreviewClip",
            typeof(RectTransform),
            typeof(RectMask2D)
        );
        clipObject.layer = options.Layer;
        clipObject.transform.SetParent(_root.transform, worldPositionStays: false);
        _clipRect = clipObject.GetComponent<RectTransform>();

        var boardObject = new GameObject("BppItemBoardPreviewBoard", typeof(RectTransform));
        boardObject.layer = options.Layer;
        boardObject.transform.SetParent(_clipRect, worldPositionStays: false);
        _boardRect = boardObject.GetComponent<RectTransform>();
        _sockets = ItemBoardSocketLayout.BuildSockets(
            _boardRect,
            options.Layer,
            "BppItemBoardPreviewSocket"
        );
        _scope = _previewHost.OpenScope(new ItemBoardNativeCardPreviewOwner(_sockets, options));

        ApplyTransform();
        return true;
    }

    private void ApplyOptions(ItemBoardPreviewOptions options)
    {
        if (_canvas != null)
        {
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = options.SortingOrder;
            _canvas.pixelPerfect = false;
        }

        if (!options.UseCanvasGroup)
            return;

        if (_root != null && _canvasGroup == null)
            _canvasGroup = _root.AddComponent<CanvasGroup>();
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = _root?.activeSelf == true ? 1f : 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }
    }

    private void ApplyTransform()
    {
        if (_rootRect == null || _clipRect == null || _boardRect == null)
            return;

        _rootRect.anchorMin = Vector2.zero;
        _rootRect.anchorMax = Vector2.zero;
        _rootRect.pivot = Vector2.zero;
        _rootRect.anchoredPosition = Vector2.zero;
        _rootRect.sizeDelta = new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        _rootRect.localScale = Vector3.one;

        _clipRect.anchorMin = Vector2.zero;
        _clipRect.anchorMax = Vector2.zero;
        _clipRect.pivot = Vector2.zero;
        _clipRect.anchoredPosition = _position;
        _clipRect.sizeDelta = _clipSize;
        _clipRect.localScale = Vector3.one;

        if (_options.LayoutMode == ItemBoardPreviewLayoutMode.SlotGrid)
        {
            _boardRect.anchorMin = Vector2.zero;
            _boardRect.anchorMax = Vector2.one;
            _boardRect.pivot = new Vector2(0.5f, 0.5f);
            _boardRect.anchoredPosition = Vector2.zero;
            _boardRect.sizeDelta = Vector2.zero;
            _boardRect.localScale = Vector3.one;
            return;
        }

        _boardRect.anchorMin = new Vector2(0.5f, 0.5f);
        _boardRect.anchorMax = new Vector2(0.5f, 0.5f);
        _boardRect.pivot = new Vector2(0.5f, 0.5f);
        _boardRect.anchoredPosition = Vector2.zero;
        _boardRect.sizeDelta = new Vector2(
            ItemBoardSocketLayout.NativeBoardWidth,
            ItemBoardSocketLayout.NativeBoardHeight
        );
        _boardRect.localScale = Vector3.one * _cardScale;
    }

    private async Task<CardCreationCollection> CreateCardsForRenderAsync(
        IReadOnlyList<NativeCardPreviewSubject> cards,
        INativeCardPreviewScope scope,
        RectTransform[] sockets,
        int snapshot,
        CancellationToken token
    )
    {
        var creation = await SpawnCardsAsync(cards, scope, sockets, token);
        var staleOrCanceled =
            !_generation.IsCurrent(snapshot)
            || token.IsCancellationRequested
            || !ReferenceEquals(_scope, scope);

        if (!staleOrCanceled)
            return creation;

        DisposeCards(creation.Cards);
        return creation.WithoutSessions();
    }

    private async Task<CardCreationCollection> SpawnCardsAsync(
        IReadOnlyList<NativeCardPreviewSubject> cards,
        INativeCardPreviewScope scope,
        RectTransform[] sockets,
        CancellationToken token
    )
    {
        var subjects = new List<NativeCardPreviewSubject>();
        var fallbackIndex = 0;
        var hadPreflightFailures = false;
        foreach (var subject in cards)
        {
            var measurement = _previewHost.Measure(subject);
            if (measurement.Status != NativeCardMeasureStatus.Measured)
            {
                hadPreflightFailures = true;
                if (measurement.Failure != null)
                    _options.CardPreviewFailureReporter?.Invoke(measurement.Failure);
                else
                {
                    _options.ItemBoardFailureReporter?.Invoke(
                        new ItemBoardPreviewFailure(
                            ItemBoardPreviewOperation.ResolveSpan,
                            ItemBoardPreviewFailureReason.SpanUnavailable,
                            subject.TemplateId
                        )
                    );
                }
                continue;
            }

            var socketIndex = ItemBoardSocketResolver.ResolveIndex(
                sockets.Length,
                subject.SocketId.HasValue ? (int)subject.SocketId.Value : (int?)null,
                fallbackIndex,
                measurement.Span
            );
            if (socketIndex < 0)
            {
                hadPreflightFailures = true;
                _options.ItemBoardFailureReporter?.Invoke(
                    new ItemBoardPreviewFailure(
                        ItemBoardPreviewOperation.ResolvePlacement,
                        ItemBoardPreviewFailureReason.PlacementUnavailable,
                        subject.TemplateId
                    )
                );
                continue;
            }

            subjects.Add(WithSocket(subject, socketIndex));
            fallbackIndex++;
        }

        var aggregate =
            subjects.Count == 0
                ? Task.FromResult(Array.Empty<ItemBoardPreviewAcquireResult>())
                : ItemBoardPreviewBatchAcquirer.AcquireAsync(scope, subjects, token);

        var collection = await CollectCreatedSessionsAsync(aggregate, token);
        return hadPreflightFailures ? collection.WithFailure() : collection;
    }

    private async Task<CardCreationCollection> CollectCreatedSessionsAsync(
        Task<ItemBoardPreviewAcquireResult[]> aggregate,
        CancellationToken token
    )
    {
        var created = new List<ActiveCard>();
        var hadFailures = false;
        ItemBoardPreviewAcquireResult[] results;
        try
        {
            results = await aggregate;
        }
        catch (OperationCanceledException)
        {
            return new CardCreationCollection(created, hadFailures: true);
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
                _options.ItemBoardFailureReporter?.Invoke(
                    new ItemBoardPreviewFailure(
                        ItemBoardPreviewOperation.CreateAggregate,
                        ItemBoardPreviewFailureReason.AggregateException,
                        templateId: null,
                        ex
                    )
                );
            return new CardCreationCollection(created, hadFailures: true);
        }

        foreach (var result in results)
        {
            if (result.Session != null)
            {
                created.Add(new ActiveCard(result.Session, result.Subject));
                continue;
            }

            if (result.Canceled)
                continue;

            hadFailures = true;
            if (result.NativeFailure != null)
                continue;
            _options.ItemBoardFailureReporter?.Invoke(
                new ItemBoardPreviewFailure(
                    ItemBoardPreviewOperation.CreateCard,
                    result.Exception == null
                        ? ItemBoardPreviewFailureReason.SessionUnavailable
                        : ItemBoardPreviewFailureReason.CardException,
                    result.TemplateId,
                    result.Exception
                )
            );
        }

        return new CardCreationCollection(created, hadFailures);
    }

    private static void DisposeCards(IReadOnlyList<ActiveCard> cards)
    {
        foreach (var card in cards)
            card.Session.Dispose();
    }

    private static async Task DisposeCreationWhenReadyAsync(Task<CardCreationCollection> creation)
    {
        try
        {
            DisposeCards((await creation).Cards);
        }
        catch
        {
            // Each acquisition owns its own failure cleanup; there is no delivered session here.
        }
    }

    private static NativeCardPreviewSubject WithSocket(
        NativeCardPreviewSubject subject,
        int socketIndex
    ) =>
        new()
        {
            TemplateId = subject.TemplateId,
            Tier = subject.Tier,
            SocketId = (BazaarGameShared.Domain.Core.Types.EContainerSocketId)socketIndex,
            DisplaySpan = subject.DisplaySpan,
            EnchantmentType = subject.EnchantmentType,
            Attributes = subject.Attributes,
            InstanceIdPrefix = subject.InstanceIdPrefix,
        };

    private void CompleteLoad(CancellationTokenSource cancellation)
    {
        if (!ReferenceEquals(_loadCancellation, cancellation))
            return;

        _loadCancellation = null;
        cancellation.Dispose();
    }

    private void CancelAndDisposeLoad(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_loadCancellation, cancellation))
            _loadCancellation = null;

        CancelAndDispose(cancellation);
    }

    private static void CancelAndDispose(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A caller may have already canceled and disposed this source.
        }

        cancellation.Dispose();
    }

    private bool ShowSetUpCards()
    {
        var hadFailures = false;
        foreach (var card in _active)
        {
            var result = card.Session.Show();
            if (result.Status == NativePreviewActionStatus.Failed)
                hadFailures = true;
        }
        return hadFailures;
    }

    private void LayoutCardsPacked()
    {
        if (_boardRect == null || _active.Count == 0)
            return;

        var corners = new Vector3[4];
        var laid = new List<(Transform card, float frameLeft, float frameWidth)>();
        foreach (var card in _active)
        {
            var root = card.Session.Root;
            if (root == null)
                continue;

            var rect = card.Session.Rect;
            var frame = FindDescendant(rect, "FrameContainer") ?? rect;
            frame.GetWorldCorners(corners);
            laid.Add((root.transform, corners[0].x, corners[2].x - corners[0].x));
        }

        if (laid.Count == 0)
            return;

        laid.Sort((a, b) => a.frameLeft.CompareTo(b.frameLeft));

        var totalFrameWidth = 0f;
        foreach (var entry in laid)
            totalFrameWidth += entry.frameWidth;

        var cursor = _boardRect.position.x - totalFrameWidth * 0.5f;
        foreach (var entry in laid)
        {
            entry.card.position += new Vector3(cursor - entry.frameLeft, 0f, 0f);
            cursor += entry.frameWidth;
        }
    }

    private void LayoutCardsSlotGrid()
    {
        if (_active.Count == 0)
            return;

        var corners = new Vector3[4];
        foreach (var card in _active)
        {
            var root = card.Session.Root;
            if (root == null)
                continue;
            if (!root.activeInHierarchy)
                continue;

            var frame = FindDescendant(card.Session.Rect, "FrameContainer") ?? card.Session.Rect;
            frame.GetWorldCorners(corners);
            var frameWidth = corners[2].x - corners[0].x;
            var frameHeight = corners[2].y - corners[0].y;
            if (frameWidth <= 0f || frameHeight <= 0f)
                continue;

            var socketIndex = card.Subject.SocketId.HasValue ? (int)card.Subject.SocketId.Value : 0;
            var occupied = ItemBoardSlotGridGeometry.ResolveOccupiedRect(
                _clipSize.x,
                _clipSize.y,
                socketIndex,
                card.Subject.DisplaySpan,
                _options.SlotGridHorizontalInsetPixels,
                _options.SlotGridVerticalInsetPixels
            );
            var targetHeight = ItemBoardSlotGridGeometry.ResolveScaledTargetHeight(
                occupied.Height,
                ItemBoardSocketLayout.NativeBoardHeight,
                _cardScale,
                _options.SlotGridMaxHeightRatio
            );
            var scale = ItemBoardSlotGridGeometry.ResolveHeightScale(
                frameHeight,
                targetHeight,
                _options.SlotGridMaxScale
            );

            var cardTransform = root.transform;
            cardTransform.localScale = new Vector3(
                cardTransform.localScale.x * scale,
                cardTransform.localScale.y * scale,
                cardTransform.localScale.z
            );

            frame.GetWorldCorners(corners);
            var frameCenter = new Vector2(
                (corners[0].x + corners[2].x) * 0.5f,
                (corners[0].y + corners[2].y) * 0.5f
            );
            var targetCenter = new Vector2(
                _position.x + occupied.CenterX,
                _position.y + occupied.CenterY
            );
            cardTransform.position += new Vector3(
                targetCenter.x - frameCenter.x,
                targetCenter.y - frameCenter.y,
                0f
            );
        }
    }

    private INativeCardPreviewSession? FindHoveredSession(Vector2 mousePixels)
    {
        var corners = new Vector3[4];
        foreach (var card in _active)
        {
            var root = card.Session.Root;
            if (root == null)
                continue;
            if (!root.activeInHierarchy)
                continue;

            card.Session.Rect.GetWorldCorners(corners);
            var rect = new Rect(
                corners[0].x,
                corners[0].y,
                corners[2].x - corners[0].x,
                corners[2].y - corners[0].y
            );
            if (rect.Contains(mousePixels))
                return card.Session;
        }

        return null;
    }

    private void DispatchHoverOut()
    {
        _hoveredSession?.HoverExit();
        _hoveredSession = null;
    }

    private void ReturnActiveCardsToPool()
    {
        DispatchHoverOut();
        DisposeCards(_active);
        _active.Clear();
    }

    private void DisposeRuntimeObjects()
    {
        ReturnActiveCardsToPool();
        var scope = _scope;
        var root = _root;
        if (root != null)
            root.SetActive(false);
        if (scope != null || root != null)
            _ = DisposeScopeAndRootAsync(scope, root);

        _scope = null;
        _sockets = null;
        _root = null;
        _canvas = null;
        _canvasGroup = null;
        _rootRect = null;
        _clipRect = null;
        _boardRect = null;
    }

    private static async Task DisposeScopeAndRootAsync(
        INativeCardPreviewScope? scope,
        GameObject? root
    )
    {
        try
        {
            if (scope != null)
                await scope.DisposeAsync();
        }
        catch
        {
            // Scope/owner cleanup already reports typed failures. Keep fire-and-forget teardown
            // from publishing an unobserved task exception, and always destroy the canvas root.
        }
        finally
        {
            if (root != null)
                Object.Destroy(root);
        }
    }

    private static RectTransform? FindDescendant(Transform root, string childName)
    {
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt != null && rt.name == childName)
                return rt;
        }
        return null;
    }

    private readonly struct CardCreationCollection
    {
        public CardCreationCollection(IReadOnlyList<ActiveCard> cards, bool hadFailures)
        {
            Cards = cards;
            HadFailures = hadFailures;
        }

        public IReadOnlyList<ActiveCard> Cards { get; }
        public bool HadFailures { get; }

        public CardCreationCollection WithoutSessions() =>
            new(Array.Empty<ActiveCard>(), HadFailures);

        public CardCreationCollection WithFailure() => new(Cards, hadFailures: true);
    }

    private readonly record struct ActiveCard(
        INativeCardPreviewSession Session,
        NativeCardPreviewSubject Subject
    );

    private sealed class ItemBoardNativeCardPreviewOwner : INativeCardPreviewOwner
    {
        private readonly RectTransform[] _sockets;
        private readonly ItemBoardPreviewOptions _options;

        internal ItemBoardNativeCardPreviewOwner(
            RectTransform[] sockets,
            ItemBoardPreviewOptions options
        )
        {
            _sockets = sockets ?? throw new ArgumentNullException(nameof(sockets));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public int Layer => _options.Layer;

        public Transform? ResolveParent(NativeCardPreviewSubject subject)
        {
            if (!subject.SocketId.HasValue)
                return null;

            var index = (int)subject.SocketId.Value;
            return index >= 0 && index < _sockets.Length ? _sockets[index] : null;
        }

        public void PrepareWhileInactive(NativeCardPreviewOwnerContext context) { }

        public void OnAcquired(NativeCardPreviewOwnerContext context) { }

        public void BeforeRelease(NativeCardPreviewOwnerContext context) { }

        public void ReportFailure(NativeCardPreviewFailure failure)
        {
            if (
                failure.Operation
                is NativeCardPreviewOperation.InvokeHover
                    or NativeCardPreviewOperation.InvokeHoverOut
            )
                _options.HoverFailureReporter?.Invoke(failure);
            else
                _options.CardPreviewFailureReporter?.Invoke(failure);
        }
    }
}
