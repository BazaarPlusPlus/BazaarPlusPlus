#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.GameInterop.AssetLoading;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using BazaarPlusPlus.GameInterop.MonsterBoardPreview;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.UiTokens;
using BazaarPlusPlus.Localization;
using TheBazaar.AppFramework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Ui;

internal sealed class LiveBuildPanelView : IDisposable
{
    private const string Art = "Assets/TheBazaar/Art/UI/Buttons/Btn_Rectangle/";
    private static readonly string[] Addresses =
    [
        Art + "Btn_Rct_Frame_S_TUI.png",
        Art + "Btn_Rct_Brown_S_Center_Active_TUI.png",
        Art + "Btn_Rct_Blue_S_Center_Active_TUI.png",
    ];
    private static readonly Color Ink = new(.96f, .89f, .73f);
    private static readonly Color Muted = new(.69f, .63f, .53f);

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
    private readonly Dictionary<BppItemBoardId, Row> _rows = new();
    private readonly List<(Image Image, int Index)> _art = new();
    private NativeGameTypography.OwnedTextPreparation? _font;
    private GameObject? _root;
    private RectTransform? _layout;
    private RectTransform? _markerRoot;
    private Vector2 _screenSize;
    private int _layoutFrames = 2;
    private RectTransform? _supporterHost;
    private RectTransform? _sidebar;
    private RectTransform? _corpusGroup;
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
    private readonly TextMeshProUGUI[] _metricLabels = new TextMeshProUGUI[
        LiveBuildPanelLayout.MetricCells
    ];
    private readonly TextMeshProUGUI[] _metricValues = new TextMeshProUGUI[
        LiveBuildPanelLayout.MetricCells
    ];
    private TextMeshProUGUI? _guidance;
    private TextMeshProUGUI? _hint;
    private Button? _refreshButton;
    private Button? _closeButton;
    private Button? _previousButton;
    private Button? _nextButton;
    private Image? _portrait;
    private EHero? _portraitHero;
    private Task<HeroPortraitLoadOutcome?>? _portraitLoad;
    private Task<Sprite?[]>? _skinLoad;
    private Sprite?[]? _skin;
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
        Action refresh
    )
    {
        _parent = parent;
        _close = close;
        _previous = previous;
        _next = next;
        _refresh = refresh;
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
        _layout.gameObject.AddComponent<Image>().color = new Color(.16f, .105f, .065f, 1);
        _backdrop = Rect("NativeBackdrop", _layout, 0, 0, 1, 1).gameObject.AddComponent<RawImage>();
        _backdrop.raycastTarget = false;
        _backdrop.color = Color.clear;
        _backdrop.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter
            .AspectMode
            .EnvelopeParent;
        var shade = Rect("BackgroundShade", _layout, 0, 0, 1, 1).gameObject.AddComponent<Image>();
        shade.color = new Color(.055f, .038f, .028f, .64f);
        shade.raycastTarget = false;
        var frame = Rect("PanelFrame", _layout, 0, 0, 1, 1);
        frame.offsetMin = new Vector2(7, 7);
        frame.offsetMax = new Vector2(-7, -7);
        Layer(frame, 0);
        _sidebar = Rect("Overview", _layout, 0, 0, 0, 0);
        var sidebarRule = Rect("SidebarRule", _sidebar, 0, 0, 0, 1);
        sidebarRule.sizeDelta = new Vector2(1, 0);
        var sidebarRuleImage = sidebarRule.gameObject.AddComponent<Image>();
        sidebarRuleImage.color = new Color(.58f, .43f, .20f, .35f);
        sidebarRuleImage.raycastTarget = false;
        foreach (var dividerY in new[] { .11f, .70f })
        {
            var divider = Rect("SectionDivider", _sidebar, .08f, dividerY, .84f, 0);
            divider.sizeDelta = new Vector2(0, 1);
            divider.gameObject.AddComponent<Image>().color = new Color(.58f, .43f, .20f, .5f);
        }
        _title = Text(_layout, string.Empty, .02f, .008f, .49f, .05f, 32);
        _title.textWrappingMode = TextWrappingModes.Normal;
        _supporterHost = Rect("SupporterAttribution", _layout, .02f, .048f, .65f, .027f);
        _supporters = new BPPSupporterNativeAttributionRow(
            _supporterHost,
            _font!,
            TextAnchor.UpperLeft,
            stacked: true
        );
        _corpusGroup = Rect("BuildLibrary", _layout, 0, 0, 0, 0);
        _corpusTitle = Text(_corpusGroup, "", 0, 0, 0, 0, 15);
        _corpus = Text(_corpusGroup, "", 0, 0, 0, 0, 14, Muted);
        _corpus.textWrappingMode = TextWrappingModes.Normal;
        _refreshButton = Button(_corpusGroup, "", 0, 0, 0, 0, _refresh, size: 14);
        _closeButton = Button(_layout, "", .943f, .027f, .044f, .045f, _close);
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
                i == 0 ? new Color(.72f, .54f, .24f, .8f) : new Color(.58f, .43f, .20f, .3f);
            divider.raycastTarget = false;
            row.Header = Rect("RowHeader", _layout, .02f, y, .96f, .024f);
            row.Title = Text(row.Header, "", 0, 0, .50f, 1, 18);
            row.Region = Rect(ids[i].ToString(), _layout, .02f, y + .033f, .96f, .17f);
            row.Status = Text(row.Header, "", .4f, 0, .60f, 1, 14, Muted);
            row.Status.alignment = TextAlignmentOptions.MidlineRight;
            _rows.Add(ids[i], row);
        }
        var recommendationHeader = _rows[BppItemBoardId.FinalBuild].Header;
        _portrait = Rect("HeroPortrait", recommendationHeader, 0, 0, .03f, 1)
            .gameObject.AddComponent<Image>();
        _portrait.preserveAspect = true;
        _portrait.raycastTarget = false;
        _portrait.color = Color.clear;
        _navigationLabel = Text(_layout, "", 0, 0, 0, 0, 17);
        _previousButton = NavigationButton(_previous, forward: false);
        _pager = Text(_layout, "", 0, 0, 0, 0, 17, Ink, true);
        _nextButton = NavigationButton(_next, forward: true);
        // One fixed cell per metric: a run-on line silently ellipsized the last one away.
        _metrics = Rect("MatchMetrics", _layout, 0, 0, 0, 0);
        for (var cell = 0; cell < LiveBuildPanelLayout.MetricCells; cell++)
        {
            var slot = Rect(
                "Metric",
                _metrics,
                cell / (float)LiveBuildPanelLayout.MetricCells,
                0,
                1f / LiveBuildPanelLayout.MetricCells,
                1
            );
            _metricLabels[cell] = Text(slot, "", 0, 0, 1, .42f, 11, Muted);
            _metricValues[cell] = Text(slot, "", 0, .42f, 1, .58f, 16);
        }
        _guidance = Text(_layout, "", 0, 0, 0, 0, 14, Muted);
        _hint = Text(_layout, "", .02f, .969f, .96f, .022f, 12, Muted, true);
        _markerRoot = Rect("CandidateMarkers", _root.transform, 0, 0, 1, 1);
        var foreground = _markerRoot.gameObject.AddComponent<Canvas>();
        foreground.overrideSorting = true;
        foreground.sortingOrder = BppOverlaySorting.PanelForeground;
        var detail = Rect("CorpusDetail", _markerRoot, .48f, .091f, .49f, .10f);
        var detailBackground = detail.gameObject.AddComponent<Image>();
        detailBackground.color = new Color(.10f, .065f, .02f, .99f);
        detailBackground.raycastTarget = false;
        _corpusDetail = Text(detail, "", 0, 0, 1, 1, 17);
        _corpusDetail.textWrappingMode = TextWrappingModes.Normal;
        _corpusDetail.overflowMode = TextOverflowModes.Overflow;
        _corpusDetail.margin = new Vector4(12, 7, 12, 7);
        _corpusDetail.transform.parent.gameObject.SetActive(false);
        _root.SetActive(_visible);
        if (_snapshot != null)
            Refresh(_snapshot);
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
        _hint!.text = LiveBuildPanelText.SelectionHint();
        SetButtonText(_refreshButton!, snapshot.FinalBuildRefreshButtonText);
        _refreshButton!.interactable = snapshot.FinalBuildRefreshButtonEnabled;
        SetButtonText(_closeButton!, LiveBuildPanelText.Close());
        _supporters!.Bind(snapshot.Supporters, LiveBuildPanelText.Subtitle());
        _corpus!.text =
            snapshot.CorpusState == LiveBuildCorpusState.Summary
                ? snapshot.CorpusFreshnessText
                : snapshot.CorpusStatusText;
        _corpus.color = StatusColor(
            snapshot.CorpusState == LiveBuildCorpusState.Summary
                ? snapshot.CorpusFreshnessSeverity
                : snapshot.CorpusStatusSeverity
        );
        _corpusDetail!.text = string.Join(
            "\n",
            new[]
            {
                snapshot.CorpusStatusText,
                snapshot.CorpusFreshnessText,
                snapshot.CorpusStatusTooltip,
            }.Where(value => !string.IsNullOrWhiteSpace(value))
        );
        _pager!.text =
            snapshot.RecommendationCount > 0
                ? $"{snapshot.RecommendationIndex + 1} / {snapshot.RecommendationCount}"
                : "0 / 0";
        _previousButton!.interactable = _nextButton!.interactable =
            snapshot.RecommendationCount > 1;
        var hasRecommendation = snapshot.MatchesState == LiveBuildMatchesState.HasRecommendation;
        _metrics!.gameObject.SetActive(hasRecommendation);
        _guidance!.gameObject.SetActive(!hasRecommendation);
        _guidance.text = hasRecommendation ? string.Empty : snapshot.MatchesGuidance;
        if (hasRecommendation)
        {
            SetMetric(
                0,
                LiveBuildPanelText.MatchRateLabel(),
                LiveBuildPanelText.MatchRateValue(snapshot.MatchTenWinRateBps)
            );
            SetMetric(
                1,
                LiveBuildPanelText.MatchSampleLabel(),
                LiveBuildPanelText.MatchSampleValue(snapshot.MatchTenWinRunCount)
            );
            SetMetric(
                2,
                LiveBuildPanelText.MatchFinalDayLabel(),
                LiveBuildPanelText.MatchFinalDayValue(snapshot.MatchP75FinalDay)
            );
            SetMetric(
                3,
                LiveBuildPanelText.MatchMatchedLabel(),
                LiveBuildPanelText.MatchMatchedValue(
                    snapshot.MatchMatchedCardCount,
                    snapshot.CandidateTemplateIds.Count
                )
            );
        }
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
            _portrait!.color = Color.clear;
            _portraitLoad = snapshot.Hero is { } hero
                ? HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(hero)
                : null;
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
            if ((_skin == null || _skin.Any(sprite => sprite == null)) && _skinLoad == null)
                _skinLoad = Task.WhenAll(
                    Addresses.Select(address =>
                        NativeGlobalAssetLoader.LoadByAddressAsync<Sprite>(loader, address)
                    )
                );
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
        if (_skinLoad?.IsCompleted == true)
        {
            if (_skinLoad.Status == TaskStatus.RanToCompletion)
            {
                _skin = _skinLoad.Result;
                foreach (var art in _art)
                    ApplyArt(art.Image, art.Index);
                _supporters!.SetSkin(_skin?[0], _skin?[1]);
            }
            else
                _ = _skinLoad.Exception;
            _skinLoad = null;
        }
        if (_portraitLoad?.IsCompleted == true)
        {
            if (
                _portraitLoad.Status == TaskStatus.RanToCompletion
                && _portraitLoad.Result?.Sprite is { } sprite
            )
            {
                _portrait!.sprite = sprite;
                _portrait.color = Color.white;
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
        _corpusDetail!.transform.parent.gameObject.SetActive(
            Mouse.current != null
                && RectTransformUtility.RectangleContainsScreenPoint(
                    _corpus!.rectTransform,
                    Mouse.current.position.ReadValue()
                )
        );
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
            NativeMonsterBoardStatus.Partial => LocalizedTextHelpers.Resolve(
                new LocalizedTextSet(
                    $"{unavailableCount} cards are unavailable in this game version.",
                    $"{unavailableCount} 张卡牌在当前版本不可用。"
                )
            ),
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
        Place(_portrait!.rectTransform, 0, 2, 24, 24);
        Place(_metrics!, left, _geometry.MetricsTop, width, LiveBuildPanelLayout.MetricsHeight);
        Place(
            _guidance!.rectTransform,
            left,
            _geometry.MetricsTop,
            width,
            LiveBuildPanelLayout.MetricsHeight
        );
    }

    private void ApplyHeaderRail(float left, float width)
    {
        var sidebarLeft = _geometry.SidebarLeft(left, width);
        var contentLeft = sidebarLeft + 24;
        var contentWidth = LiveBuildPanelLayout.SidebarWidth - 48;
        Place(_sidebar!, sidebarLeft, 20, LiveBuildPanelLayout.SidebarWidth, _geometry.Height - 40);
        Place(_title!.rectTransform, contentLeft, 42, contentWidth - 80, 64);
        Place((RectTransform)_closeButton!.transform, contentLeft + contentWidth - 68, 58, 68, 32);
        Place(_corpusGroup!, contentLeft, 156, contentWidth, 128);
        Place(_corpusTitle!.rectTransform, 0, 14, contentWidth - 108, 28);
        Place((RectTransform)_refreshButton!.transform, contentWidth - 94, 14, 94, 28);
        Place(_corpus!.rectTransform, 0, 56, contentWidth, 60);
        _corpus.alignment = TextAlignmentOptions.TopLeft;
        Place(_navigationLabel!.rectTransform, contentLeft, 492, contentWidth - 116, 28);
        Place(_pager!.rectTransform, contentLeft + contentWidth - 104, 492, 104, 28);
        _pager.alignment = TextAlignmentOptions.MidlineRight;
        var navigationButtonWidth = (contentWidth - 12) / 2;
        Place(
            (RectTransform)_previousButton!.transform,
            contentLeft,
            536,
            navigationButtonWidth,
            56
        );
        Place(
            (RectTransform)_nextButton!.transform,
            contentLeft + navigationButtonWidth + 12,
            536,
            navigationButtonWidth,
            56
        );
        Place(_supporterHost!, contentLeft, _geometry.Height - 286, contentWidth, 240);
        // Anchored under the line it explains, not at a fixed offset over the boards.
        Place((RectTransform)_corpusDetail!.transform.parent, contentLeft, 290, contentWidth, 110);
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
            Layer(marker, selectable ? 2 : 1);
            Layer(marker, 0);
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
            LiveBuildRefreshSeverity.Failure => new Color(.92f, .62f, .34f),
            LiveBuildRefreshSeverity.Success => new Color(.62f, .8f, .54f),
            LiveBuildRefreshSeverity.Pending => new Color(.59f, .76f, .89f),
            _ => Muted,
        };

    private void SetMetric(int cell, string label, string value)
    {
        _metricLabels[cell].text = label;
        _metricValues[cell].text = value;
    }

    private static void SetButtonText(Button button, string value) =>
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

    private Image Layer(Transform parent, int index)
    {
        var image = Rect("NativeArt", parent, 0, 0, 1, 1).gameObject.AddComponent<Image>();
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2.5f;
        image.raycastTarget = false;
        _art.Add((image, index));
        ApplyArt(image, index);
        return image;
    }

    private void ApplyArt(Image image, int index)
    {
        image.sprite = _skin?[index];
        image.color = image.sprite != null ? Color.white : Color.clear;
    }

    private Button Button(
        Transform parent,
        string? title,
        float x,
        float y,
        float width,
        float height,
        Action click,
        int size = 17
    )
    {
        var rect = Rect("Action", parent, x, y, width, height);
        var background = Layer(rect, 1);
        background.raycastTarget = true;
        Layer(rect, 0);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => click());
        if (title != null)
            Text(rect, title, .03f, .04f, .94f, .92f, size, Ink, true);
        return button;
    }

    private Button NavigationButton(Action click, bool forward)
    {
        var button = Button(_layout!, null, 0, 0, 0, 0, click);
        var rect = Rect("Chevron", button.transform, .5f, .5f, 0, 0);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(20, 32);
        rect.localRotation = Quaternion.Euler(0, 0, forward ? 0 : 180);
        var icon = rect.gameObject.AddComponent<LiveBuildChevronGraphic>();
        icon.color = Ink;
        icon.raycastTarget = false;
        return button;
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
