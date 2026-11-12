#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.GameInterop.AssetLoading;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.NativeSkin;
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.UiTokens;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Ui;

internal sealed class LiveBuildPanelView : IDisposable
{
    private static readonly Color Ink = InGameTheme.Ink;
    private static readonly Color Muted = InGameTheme.InkMuted;

    // The match figures of the shown build: two rows of two.
    private const int MetricCells = 4;

    private sealed class Row
    {
        internal RectTransform Region = null!;
        internal RectTransform Header = null!;
        internal RectTransform Divider = null!;
        internal int Index;
        internal TextMeshProUGUI Title = null!;
        internal TextMeshProUGUI Status = null!;
        internal Rect Bounds;
        internal string Message = string.Empty;
        internal string MarkerText = string.Empty;
        internal readonly List<RectTransform> Markers = new();
        internal int VisibleMarkers;
    }

    private readonly Transform _parent;
    private readonly Action _close;
    private readonly Action _previous;
    private readonly Action _next;
    private readonly Action _refresh;
    private readonly Action _toggleIncludeTiny;
    private readonly Dictionary<BppItemBoardId, Row> _rows = new();
    private readonly List<(Image Image, DesktopSkinPart Part, Color Tint)> _art = new();
    private DesktopKit? _kit;
    private NativeGameTypography.OwnedTextPreparation? _font;
    private GameObject? _root;
    private RectTransform? _layout;
    private RectTransform? _markerRoot;
    private Vector2 _screenSize;
    private int _layoutFrames = 2;
    private RectTransform? _sidebar;
    private RectTransform? _corpusDetailCard;
    private RectTransform? _portraitWell;
    private RawImage? _backdrop;
    private Task<Texture2D?>? _backdropLoad;
    private BPPSupporterNativeAttributionRow? _supporters;
    private Vector2 _layoutSize;
    private LiveBuildPanelLayout _geometry;
    private TextMeshProUGUI? _title;
    private TextMeshProUGUI? _corpusTitle;
    private TextMeshProUGUI? _corpus;
    private TextMeshProUGUI? _corpusDetail;
    private TextMeshProUGUI? _navigationLabel;
    private TextMeshProUGUI? _pager;
    private RectTransform? _metrics;
    private readonly TextMeshProUGUI[] _metricLabels = new TextMeshProUGUI[MetricCells];
    private readonly TextMeshProUGUI[] _metricValues = new TextMeshProUGUI[MetricCells];
    private TextMeshProUGUI? _guidance;
    private TextMeshProUGUI? _hint;
    private KitButton? _refreshButton;
    private KitButton? _previousButton;
    private KitButton? _nextButton;
    private KitButton? _includeTinyButton;
    private Image? _portrait;
    private CardHero? _portraitHero;
    private BazaarGameShared.Domain.Core.Types.EHero? _portraitSource;
    private Task<HeroPortraitLoadOutcome?>? _portraitLoad;
    private int _skinGeneration;
    private float _retryAt;
    private LiveBuildPanelSnapshot? _snapshot;
    private readonly Vector3[] _corners = new Vector3[4];
    private bool _visible;
    private bool _disposed;

    internal LiveBuildPanelView(
        Transform parent,
        Action close,
        Action previous,
        Action next,
        Action refresh,
        Action toggleIncludeTiny
    )
    {
        _parent = parent;
        _close = close;
        _previous = previous;
        _next = next;
        _refresh = refresh;
        _toggleIncludeTiny = toggleIncludeTiny;
    }

    internal event Action<BppItemBoardId, Rect>? RowBoundsChanged;
    internal bool IsCreated => _root != null;
    internal float BoardFooterPixels => LiveBuildPanelLayout.BoardFooterHeight * CanvasScale;
    internal float BoardTopPixels => LiveBuildPanelLayout.BoardTopInset * CanvasScale;
    private float CanvasScale => _root != null ? _root.GetComponent<Canvas>().scaleFactor : 1;

    internal void EnsureCreated()
    {
        if (_disposed || _root != null)
            return;
        if (NativeGameTypography.PrepareOwnedText(out _font) != NativeGameTypography.Outcome.Ready)
            return;
        _root = new GameObject(
            "LiveBuildPanelView",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        _root.transform.SetParent(_parent, false);
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = BppOverlaySorting.PanelUiToolkit;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 1000);
        // Four stacked carpets are height-constrained; wider displays must not
        // shrink their content rail relative to the text and header actions.
        scaler.matchWidthOrHeight = 1;
        _layout = Rect("Panel", _root.transform, 0, 0, 1, 1);
        _layout.gameObject.AddComponent<Image>().color = InGameTheme.Backdrop;
        _backdrop = Rect("NativeBackdrop", _layout, 0, 0, 1, 1).gameObject.AddComponent<RawImage>();
        _backdrop.raycastTarget = false;
        _backdrop.color = Color.clear;
        _backdrop.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter
            .AspectMode
            .EnvelopeParent;
        var shade = Rect("BackgroundShade", _layout, 0, 0, 1, 1).gameObject.AddComponent<Image>();
        shade.color = InGameTheme.Veil;
        shade.raycastTarget = false;
        _kit = new DesktopKit(_font!);
        BuildSidebar();
        // Four peers always share the same board dimensions. Native carpet aspect and
        // ten sockets determine card scale; sparse sources never grow individual cards.
        var ids = new[]
        {
            BppItemBoardId.FinalBuild,
            BppItemBoardId.LiveShop,
            BppItemBoardId.LiveStash,
            BppItemBoardId.LiveBoard,
        };
        for (var i = 0; i < ids.Length; i++)
        {
            var y = .103f + i * .205f + (i > 0 ? .033f : 0);
            var row = new Row { Index = i };
            row.Divider = Rect("RowDivider", _layout, 0, 0, 0, 0);
            var divider = row.Divider.gameObject.AddComponent<Image>();
            divider.color =
                i == 0
                    ? InGameTheme.WithAlpha(InGameTheme.RuleStrong, .8f)
                    : InGameTheme.WithAlpha(InGameTheme.Rule, .3f);
            divider.raycastTarget = false;
            row.Header = Rect("RowHeader", _layout, .02f, y, .96f, .024f);
            row.Title = Text(row.Header, "", 0, 0, .50f, 1, 18);
            row.Region = Rect(ids[i].ToString(), _layout, .02f, y + .033f, .96f, .17f);
            row.Status = Text(row.Header, "", .4f, 0, .60f, 1, 14, Muted);
            row.Status.alignment = TextAlignmentOptions.MidlineRight;
            _rows.Add(ids[i], row);
        }
        var recommendationHeader = _rows[BppItemBoardId.FinalBuild].Header;
        _portrait = _kit.Portrait(recommendationHeader, null, 24);
        _portraitWell = (RectTransform)_portrait.transform.parent;
        _hint = Text(_layout, "", .02f, .969f, .96f, .022f, 12, Muted, true);
        _markerRoot = Rect("CandidateMarkers", _root.transform, 0, 0, 1, 1);
        var foreground = _markerRoot.gameObject.AddComponent<Canvas>();
        foreground.overrideSorting = true;
        foreground.sortingOrder = BppOverlaySorting.PanelForeground;
        // The corpus line's detail: a kit tooltip, on the foreground canvas so it is over the
        // native boards.
        _corpusDetailCard = _kit.Floating(_markerRoot, "CorpusDetail", InGameSizes.Space12);
        _corpusDetailCard.anchorMin = _corpusDetailCard.anchorMax = new Vector2(.5f, .5f);
        _corpusDetailCard.pivot = new Vector2(1, 1);
        _corpusDetailCard.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;
        _corpusDetail = _kit.Text(_corpusDetailCard, "", KitTextRole.Meta, InGameTheme.Ink);
        _corpusDetail.textWrappingMode = TextWrappingModes.Normal;
        _corpusDetail.overflowMode = TextOverflowModes.Overflow;
        _corpusDetailCard.gameObject.SetActive(false);
        _root.SetActive(_visible);
        if (_snapshot != null)
            Refresh(_snapshot);
    }

    // The overview rail: the kit's ground, laid out top to bottom. The view places only the
    // rail itself beside the boards.
    private void BuildSidebar()
    {
        var kit = _kit!;
        _sidebar = Rect("Overview", _layout!, 0, 0, 0, 0);
        kit.Surface(_sidebar, KitSurface.Ground);
        var column = _sidebar.gameObject.AddComponent<VerticalLayoutGroup>();
        column.padding = new RectOffset(22, 22, 24, 24);
        column.spacing = InGameSizes.Space12;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;

        var head = Line(_sidebar, "Head", InGameSizes.Space8);
        _title = kit.Text(head, string.Empty, KitTextRole.Title);
        _title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        kit.IconButton(head, KitIcon.Close, KitButtonKind.Secondary, KitSize.Sm, _close);

        _supporters = new BPPSupporterNativeAttributionRow(_sidebar, kit);

        // The build on show: which one, how it did, and what narrows the choice.
        var browse = kit.SectionHeader(_sidebar, " ", "0 / 0");
        var browseTexts = browse.GetComponentsInChildren<TextMeshProUGUI>(true);
        _navigationLabel = browseTexts[0];
        _pager = browseTexts[1];
        var arrows = Line(_sidebar, "Arrows", InGameSizes.Space8);
        // Paging is this panel's main action: the two arrows share the rail's width.
        _previousButton = kit.PageArrow(arrows, false, _previous, wide: true);
        _nextButton = kit.PageArrow(arrows, true, _next, wide: true);
        _metrics = DesktopKit.Rect(_sidebar, "MatchMetrics");
        var grid = _metrics.gameObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.cellSize = new Vector2(
            (LiveBuildPanelLayout.SidebarWidth - 44 - InGameSizes.Space12) / 2,
            44
        );
        grid.spacing = new Vector2(InGameSizes.Space12, InGameSizes.Space8);
        for (var cell = 0; cell < MetricCells; cell++)
        {
            var slot = DesktopKit.Rect(_metrics, "Metric");
            var stack = slot.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.spacing = InGameSizes.Space2;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            _metricLabels[cell] = kit.Text(slot, string.Empty, KitTextRole.Caption);
            _metricValues[cell] = kit.Text(
                slot,
                string.Empty,
                KitTextRole.Section,
                cell == 0 ? InGameTheme.Accent : InGameTheme.Ink
            );
        }
        _guidance = kit.Text(_sidebar, string.Empty, KitTextRole.Meta);
        _guidance.textWrappingMode = TextWrappingModes.Normal;
        _guidance.overflowMode = TextOverflowModes.Overflow;
        _includeTinyButton = kit.Checkbox(_sidebar, string.Empty, _toggleIncludeTiny);

        DesktopKit
            .Rect(_sidebar, "Spacer")
            .gameObject.AddComponent<LayoutElement>()
            .flexibleHeight = 1;

        // The library the builds come from: read once, so it is at the foot of the rail.
        var library = kit.SectionHeader(_sidebar, " ", " ");
        _corpusTitle = library.GetComponentsInChildren<TextMeshProUGUI>(true)[0];
        library.GetComponentsInChildren<TextMeshProUGUI>(true)[1].text = string.Empty;
        _corpus = kit.Text(_sidebar, string.Empty, KitTextRole.Meta);
        _corpus.textWrappingMode = TextWrappingModes.Normal;
        _corpus.overflowMode = TextOverflowModes.Overflow;
        var refresh = Line(_sidebar, "Refresh", 0);
        _refreshButton = kit.Button(
            refresh,
            string.Empty,
            KitButtonKind.Secondary,
            KitSize.Sm,
            _refresh
        );
        _skinGeneration = DesktopNativeSkin.Generation;
    }

    private static RectTransform Line(Transform parent, string name, float spacing)
    {
        var rect = DesktopKit.Rect(parent, name);
        var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = spacing;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = group.childControlHeight = true;
        group.childForceExpandWidth = group.childForceExpandHeight = false;
        return rect;
    }

    internal void SetVisible(bool visible)
    {
        _visible = visible;
        EnsureCreated();
        if (_root != null)
            _root.SetActive(visible);
        if (!visible)
            ClearMarkers();
    }

    internal void Refresh(LiveBuildPanelSnapshot snapshot)
    {
        _snapshot = snapshot;
        if (_root == null)
            return;
        _title!.text = LiveBuildPanelText.Title();
        _corpusTitle!.text = LiveBuildPanelText.CorpusLibrary();
        _navigationLabel!.text = LiveBuildPanelText.BrowseBuilds();
        SetButtonText(_includeTinyButton!, LiveBuildPanelText.IncludeTiny());
        _includeTinyButton!.On = snapshot.IncludeTiny;
        _hint!.text = LiveBuildPanelText.SelectionHint();
        SetButtonText(_refreshButton!, snapshot.FinalBuildRefreshButtonText);
        _refreshButton!.interactable = snapshot.FinalBuildRefreshButtonEnabled;
        _supporters!.Bind(snapshot.Supporters);
        _corpus!.text =
            snapshot.CorpusState == LiveBuildCorpusState.Summary
                ? snapshot.CorpusFreshnessText
                : snapshot.CorpusStatusText;
        _corpus.color = StatusColor(
            snapshot.CorpusState == LiveBuildCorpusState.Summary
                ? snapshot.CorpusFreshnessSeverity
                : snapshot.CorpusStatusSeverity
        );
        _corpusDetail!.text = string.Join("\n", snapshot.CorpusDetailLines);
        _pager!.text = snapshot.PagerText;
        _previousButton!.interactable = _nextButton!.interactable = snapshot.CanBrowse;
        var hasRecommendation = snapshot.MatchesState == LiveBuildMatchesState.HasRecommendation;
        _metrics!.gameObject.SetActive(hasRecommendation);
        _guidance!.gameObject.SetActive(!hasRecommendation);
        _guidance.text = hasRecommendation ? string.Empty : snapshot.MatchesGuidance;
        if (hasRecommendation)
            for (var cell = 0; cell < snapshot.Metrics.Count; cell++)
                SetMetric(cell, snapshot.Metrics[cell].Label, snapshot.Metrics[cell].Value);
        foreach (var row in snapshot.Rows)
        {
            var ui = _rows[row.Board.Id];
            ui.Title.text = row.Title;
            ui.MarkerText = row.CanToggleCandidates
                ? LiveBuildPanelText.CandidateTag()
                : LiveBuildPanelText.MatchTag();
            ui.Status.text =
                ui.Message.Length > 0 ? ui.Message
                : row.Board.Cards.Count == 0 ? row.EmptyText
                : string.Empty;
        }
        if (_portraitHero != snapshot.Hero)
        {
            _portraitHero = snapshot.Hero;
            DesktopKit.ShowPortrait(_portrait!, null);
            _portraitSource = null;
            _portraitLoad =
                snapshot.Hero is { } hero
                && DesktopCardFacets.Hero.TryToSource(hero, out var source)
                    ? HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(source)
                    : null;
            if (
                _portraitLoad != null
                && DesktopCardFacets.Hero.TryToSource(snapshot.Hero!.Value, out var shown)
            )
                _portraitSource = shown;
        }
    }

    internal void Tick()
    {
        if (!_visible || _disposed)
            return;
        EnsureCreated();
        if (_root == null)
            return;
        if (
            Time.unscaledTime >= _retryAt
            && Services.TryGet<AssetLoader>(out var loader)
            && loader != null
        )
        {
            _retryAt = Time.unscaledTime + 5;
            DesktopNativeSkin.Request();
            if (_backdrop!.texture == null && _backdropLoad == null)
                _backdropLoad = NativeGlobalAssetLoader.LoadByAddressAsync<Texture2D>(
                    loader,
                    "Collections_Background_01_TUI"
                );
        }
        if (_backdropLoad?.IsCompleted == true)
        {
            if (
                _backdropLoad.Status == TaskStatus.RanToCompletion
                && _backdropLoad.Result is { } texture
            )
            {
                _backdrop!.texture = texture;
                _backdrop.color = Color.white;
                _backdrop.GetComponent<AspectRatioFitter>().aspectRatio =
                    (float)texture.width / texture.height;
            }
            else
                _ = _backdropLoad.Exception;
            _backdropLoad = null;
        }
        DesktopNativeSkin.Poll();
        _kit!.Tick();
        if (_skinGeneration != DesktopNativeSkin.Generation)
        {
            _skinGeneration = DesktopNativeSkin.Generation;
            foreach (var art in _art)
                DesktopNativeSkin.Paint(art.Image, art.Part, art.Tint);
        }
        if (_portraitLoad?.IsCompleted == true)
        {
            if (
                _portraitLoad.Status == TaskStatus.RanToCompletion
                && _portraitLoad.Result?.Sprite is { } sprite
            )
            {
                DesktopKit.ShowPortrait(
                    _portrait!,
                    sprite,
                    _portraitSource is { } shownHero ? HeroPortraitFocus.For(shownHero) : null,
                    _portraitSource is { } glowHero ? HeroPortraitFocus.Glow(glowHero) : null
                );
            }
            else
                _ = _portraitLoad.Exception;
            _portraitLoad = null;
        }
        var screen = new Vector2(Screen.width, Screen.height);
        if (_screenSize != screen)
        {
            _screenSize = screen;
            _layoutFrames = 2;
        }
        if (_layoutFrames > 0)
        {
            _layoutFrames--;
            Canvas.ForceUpdateCanvases();
            UpdateLayout();
            foreach (var pair in _rows)
            {
                pair.Value.Region.GetWorldCorners(_corners);
                var bounds = UnityEngine.Rect.MinMaxRect(
                    _corners[0].x,
                    _corners[0].y,
                    _corners[2].x,
                    _corners[2].y
                );
                if (bounds.width > 1 && bounds.height > 1 && bounds != pair.Value.Bounds)
                {
                    pair.Value.Bounds = bounds;
                    RowBoundsChanged?.Invoke(pair.Key, bounds);
                }
            }
        }
        var hovered =
            Mouse.current != null
            && RectTransformUtility.RectangleContainsScreenPoint(
                _corpus!.rectTransform,
                Mouse.current.position.ReadValue()
            );
        _corpusDetailCard!.gameObject.SetActive(hovered);
        if (hovered)
        {
            // Under the line it explains, its right edge on the line's right edge.
            _corpus!.rectTransform.GetWorldCorners(_corners);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _markerRoot!,
                new Vector2(_corners[3].x, _corners[3].y),
                null,
                out var corner
            );
            _corpusDetailCard.anchoredPosition = corner - new Vector2(0, InGameSizes.Space6);
            _corpusDetailCard.sizeDelta = new Vector2(
                LiveBuildPanelLayout.SidebarWidth - 44,
                _corpusDetailCard.sizeDelta.y
            );
        }
    }

    internal void SetBoardStatus(
        BppItemBoardId id,
        NativeMonsterBoardStatus status,
        int unavailableCount = 0
    )
    {
        if (!_rows.TryGetValue(id, out var row))
            return;
        row.Message = status switch
        {
            NativeMonsterBoardStatus.Loading => LiveBuildPanelText.LoadingBoard(),
            NativeMonsterBoardStatus.Failed => LiveBuildPanelText.BoardFailed(),
            NativeMonsterBoardStatus.Partial => LiveBuildPanelText.BoardPartial(unavailableCount),
            _ => string.Empty,
        };
        if (_snapshot != null)
            Refresh(_snapshot);
    }

    private void UpdateLayout()
    {
        var size = _layout!.rect.size;
        if (size.x <= 1 || size.y <= 1 || size == _layoutSize)
            return;
        _layoutSize = size;
        _geometry = new LiveBuildPanelLayout(size.x, size.y);
        foreach (var row in _rows.Values)
        {
            Place(
                row.Region,
                _geometry.BoardLeft,
                _geometry.BoardTop(row.Index),
                _geometry.BoardWidth,
                _geometry.BoardHeight
            );
            ApplyBoardRail(row, _geometry.BoardLeft, _geometry.BoardWidth);
        }
    }

    internal void SetBoardRail(BppItemBoardId id, Rect screenBounds)
    {
        if (_layout == null || screenBounds.width <= 1)
            return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _layout,
            screenBounds.min,
            null,
            out var left
        );
        // Only text follows the loaded carpet. Feeding these bounds back into the
        // preview region would shrink the board again on each layout pass.
        ApplyBoardRail(_rows[id], left.x - _layout.rect.xMin, screenBounds.width / CanvasScale);
    }

    private void ApplyBoardRail(Row row, float left, float width)
    {
        Place(row.Divider, left, _geometry.TitleTop(row.Index) - 8, width, row.Index == 0 ? 2 : 1);
        Place(
            row.Header,
            left,
            _geometry.TitleTop(row.Index),
            width,
            LiveBuildPanelLayout.RowTitleHeight
        );
        var titleLeft = row.Index == 0 ? 30 : 0;
        var titleWidth = Mathf.Min(230, width * .38f);
        Place(
            row.Title.rectTransform,
            titleLeft,
            0,
            titleWidth,
            LiveBuildPanelLayout.RowTitleHeight
        );
        var statusLeft = titleLeft + titleWidth + 10;
        Place(
            row.Status.rectTransform,
            statusLeft,
            0,
            Mathf.Max(1, width - statusLeft),
            LiveBuildPanelLayout.RowTitleHeight
        );
        if (row.Index != 0)
            return;
        ApplyHeaderRail(left, width);
        Place(_portraitWell!, 0, 2, 24, 24);
    }

    private void ApplyHeaderRail(float left, float width)
    {
        Place(
            _sidebar!,
            _geometry.SidebarLeft(left, width),
            20,
            LiveBuildPanelLayout.SidebarWidth,
            _geometry.Height - 40
        );
        Place(
            _hint!.rectTransform,
            left,
            _geometry.FooterTop,
            width,
            LiveBuildPanelLayout.FooterHeight
        );
    }

    private static void Place(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(Mathf.Max(1, width), height);
    }

    internal void BeginMarkers(BppItemBoardId id) => _rows[id].VisibleMarkers = 0;

    internal void AddMarker(BppItemBoardId id, Rect bounds, bool selectable)
    {
        if (_markerRoot == null)
            return;
        var row = _rows[id];
        var index = row.VisibleMarkers++;
        if (index == row.Markers.Count)
        {
            var marker = Rect("CandidateTag", _markerRoot, 0, 0, 0, 0);
            marker.anchorMin = marker.anchorMax = new Vector2(.5f, .5f);
            marker.pivot = new Vector2(.5f, 1);
            Layer(marker, selectable ? DesktopSkinPart.BlueCenter : DesktopSkinPart.BrownCenter);
            Layer(marker, DesktopSkinPart.Frame);
            Text(marker, string.Empty, .04f, .02f, .92f, .96f, 12, Ink, true);
            row.Markers.Add(marker);
        }
        var target = row.Markers[index];
        target.GetComponentInChildren<TextMeshProUGUI>(true).text = row.MarkerText;
        var scale = _root!.GetComponent<Canvas>().scaleFactor;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _markerRoot,
            new Vector2(bounds.center.x, bounds.yMin - 1 * scale),
            null,
            out var local
        );
        target.anchoredPosition = local;
        target.sizeDelta = new Vector2(
            Mathf.Clamp(bounds.width / scale * .68f, 42, 72),
            LiveBuildPanelLayout.BadgeHeight
        );
        target.gameObject.SetActive(true);
    }

    internal void EndMarkers(BppItemBoardId id)
    {
        var row = _rows[id];
        for (var i = row.VisibleMarkers; i < row.Markers.Count; i++)
            row.Markers[i].gameObject.SetActive(false);
    }

    private void ClearMarkers()
    {
        foreach (var row in _rows.Values)
        foreach (var marker in row.Markers)
            marker.gameObject.SetActive(false);
    }

    private static Color StatusColor(LiveBuildRefreshSeverity severity) =>
        severity switch
        {
            LiveBuildRefreshSeverity.Failure => InGameTheme.Warning,
            LiveBuildRefreshSeverity.Success => InGameTheme.Success,
            LiveBuildRefreshSeverity.Pending => InGameTheme.Pending,
            _ => Muted,
        };

    private void SetMetric(int cell, string label, string value)
    {
        _metricLabels[cell].text = label;
        _metricValues[cell].text = value;
    }

    private static void SetButtonText(KitButton button, string value) =>
        button.GetComponentInChildren<TextMeshProUGUI>(true).text = value;

    private static RectTransform Rect(
        string name,
        Transform parent,
        float x,
        float y,
        float width,
        float height
    )
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(x, 1 - y - height);
        rect.anchorMax = new Vector2(x + width, 1 - y);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private Image Layer(Transform parent, DesktopSkinPart part, Color? tint = null)
    {
        var color = tint ?? Color.white;
        var image = DesktopNativeSkin.Layer(
            parent,
            "NativeArt",
            part,
            color,
            DesktopNativeSkin.PanelPixelsPerUnit
        );
        _art.Add((image, part, color));
        return image;
    }

    private TextMeshProUGUI Text(
        Transform parent,
        string value,
        float x,
        float y,
        float width,
        float height,
        int size,
        Color? color = null,
        bool center = false
    )
    {
        var text = Rect("Label", parent, x, y, width, height)
            .gameObject.AddComponent<TextMeshProUGUI>();
        _font!.Apply(text);
        text.text = value;
        text.richText = false;
        text.fontSize = size;
        text.fontSizeMin = size * .8f;
        text.fontSizeMax = size;
        text.enableAutoSizing = true;
        text.color = color ?? Ink;
        text.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    public void Dispose()
    {
        _disposed = true;
        _visible = false;
        _portraitLoad = null;
        _backdropLoad = null;
        _snapshot = null;
        _rows.Clear();
        _art.Clear();
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _root = null;
    }
}
