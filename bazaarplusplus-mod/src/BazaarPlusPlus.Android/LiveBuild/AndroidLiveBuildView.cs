using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.Localization;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.LiveBuild;

/// <summary>
/// The Live Build panel's touch view for one <see cref="LiveBuildTouchLayout"/>: the full sheet,
/// the pinned recommended build, the drag-scrolled shop, stash, and board, and the overview
/// sidebar. It renders a <see cref="LiveBuildPanelSnapshot"/> as the desktop's
/// <c>LiveBuildPanelView</c> maps one, and hands taps to the panel. A rented card carries its own
/// canvas, which the list's <c>RectMask2D</c> does not clip, so the list draws first and opaque
/// bands above and below it hide the cards scrolled past its edges, as the Collection grid does.
/// </summary>
internal sealed class AndroidLiveBuildView
{
    private static readonly LocalizedTextSet TapHint = new(
        "Tap shop, stash, or board items to pick candidates. Tap a build card to inspect it.",
        "点按商店、仓库或场上的物品选为候选；点按构筑中的卡牌查看大图。",
        "點按商店、倉庫或場上的物品選為候選；點按構築中的卡牌查看大圖。"
    );

    private readonly GameObject _root;
    private readonly LiveBuildTouchLayout _layout;
    private readonly Dictionary<BppItemBoardId, Row> _rows = new();
    private readonly ScrollRect _scroll;
    private readonly RectTransform _content;
    private readonly TextMeshProUGUI _pager;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly TextMeshProUGUI _refreshLabel;
    private readonly Button _refresh;
    private readonly TextMeshProUGUI _corpus;
    private readonly TextMeshProUGUI _hero;
    private readonly TextMeshProUGUI _guidance;
    private readonly GameObject _metrics;
    private readonly (TextMeshProUGUI Label, TextMeshProUGUI Value)[] _metricCells;
    private readonly GameObject _corpusDetailRoot;
    private readonly TextMeshProUGUI _corpusDetail;
    private bool _showCorpusDetail;
    private bool _hasRecommendation;

    public AndroidLiveBuildView(
        Transform parent,
        TMP_FontAsset font,
        LiveBuildTouchLayout layout,
        AndroidLiveBuildPanel.Commands commands
    )
    {
        _layout = layout;
        var root = AndroidUiKit.Rect(
            "LiveBuild",
            parent,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        // Opaque: the sheet covers the whole screen, and nothing of the scene shows through.
        var background = AndroidUiKit.PanelColor;
        background.a = 1f;
        AndroidUiKit.Fill(root, background);
        _root = root.gameObject;
        var metrics = layout.Metrics;

        // The list first, then the bands over its edges, then everything else.
        var scrollArea = AndroidTouchScreen.Child("List", root, layout.Scroll);
        (_scroll, _content) = VerticalScroll(scrollArea);
        _content.sizeDelta = new Vector2(0f, layout.ScrollContentHeight);
        var listRows = new[]
        {
            (BppItemBoardId.LiveShop, 0),
            (BppItemBoardId.LiveStash, 1),
            (BppItemBoardId.LiveBoard, 2),
        };
        foreach (var (id, index) in listRows)
            _rows[id] = CreateRow(
                _content,
                font,
                id,
                layout.ScrollRowTitle(index),
                layout.ScrollRowBoard(index),
                commands
            );
        Band(root, background, new TouchRect(0f, 0f, metrics.CanvasWidth, layout.Scroll.Top));
        Band(
            root,
            background,
            new TouchRect(
                0f,
                layout.Scroll.Bottom,
                metrics.CanvasWidth,
                Math.Max(0f, metrics.CanvasHeight - layout.Scroll.Bottom)
            )
        );

        _rows[BppItemBoardId.FinalBuild] = CreateRow(
            root,
            font,
            BppItemBoardId.FinalBuild,
            layout.PinnedTitleText,
            layout.PinnedBoard,
            commands
        );
        _previous = PagerButton(root, font, layout.Previous, "<", "previous", commands.Previous);
        _next = PagerButton(root, font, layout.Next, ">", "next", commands.Next);
        _pager = Label(
            root,
            font,
            "Pager",
            layout.Pager,
            layout.ButtonFont,
            TextAlignmentOptions.Center
        );

        Label(
            root,
            font,
            "Title",
            layout.Title,
            layout.TitleFont,
            TextAlignmentOptions.MidlineLeft
        ).text = LiveBuildPanelText.Title();
        var close = AndroidTouchScreen.Child("Close", root, layout.Close);
        AndroidUiKit.Button(
            close,
            font,
            "X",
            layout.TitleFont,
            () => AndroidUiActions.Run("live_build.close", () => commands.Close("close_button"))
        );

        var refresh = AndroidTouchScreen.Child("Refresh", root, layout.Refresh);
        (_refresh, _, _refreshLabel) = AndroidUiKit.Button(
            refresh,
            font,
            "",
            layout.ButtonFont,
            () => AndroidUiActions.Run("live_build.refresh", commands.Refresh)
        );

        var corpus = AndroidTouchScreen.Child("Corpus", root, layout.Corpus);
        var (_, _, corpusLabel) = AndroidUiKit.Button(
            corpus,
            font,
            "",
            layout.BodyFont,
            () => AndroidUiActions.Run("live_build.corpus", ToggleCorpusDetail)
        );
        _corpus = corpusLabel;
        _corpus.overflowMode = TextOverflowModes.Ellipsis;

        _hero = Label(
            root,
            font,
            "Hero",
            layout.Hero,
            layout.RowTitleFont,
            TextAlignmentOptions.MidlineLeft
        );
        _hero.overflowMode = TextOverflowModes.Ellipsis;

        _guidance = Label(
            root,
            font,
            "Guidance",
            layout.Info,
            layout.BodyFont,
            TextAlignmentOptions.TopLeft
        );
        _guidance.color = AndroidUiKit.MutedTextColor;
        var metricsRoot = AndroidTouchScreen.Child("Metrics", root, layout.Info);
        _metrics = metricsRoot.gameObject;
        _metricCells = new (TextMeshProUGUI, TextMeshProUGUI)[LiveBuildTouchLayout.MetricCount];
        for (var index = 0; index < _metricCells.Length; index++)
        {
            var cellArea = layout.MetricCell(index);
            var cell = AndroidTouchScreen.Child(
                $"Metric{index}",
                metricsRoot,
                new TouchRect(
                    cellArea.Left - layout.Info.Left,
                    cellArea.Top - layout.Info.Top,
                    cellArea.Width,
                    cellArea.Height
                )
            );
            AndroidUiKit.Fill(cell, AndroidUiKit.ButtonColor, blocksTouches: false);
            var inset = layout.Gap;
            var label = AndroidUiKit.Text(
                AndroidUiKit.Rect(
                    "Label",
                    cell,
                    new(0f, 0.5f),
                    new(1f, 1f),
                    new(inset, 0f),
                    new(-inset, -inset / 2f)
                ),
                font,
                "",
                layout.BodyFont,
                TextAlignmentOptions.BottomLeft,
                AndroidUiKit.MutedTextColor
            );
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var value = AndroidUiKit.Text(
                AndroidUiKit.Rect(
                    "Value",
                    cell,
                    new(0f, 0f),
                    new(1f, 0.5f),
                    new(inset, inset / 2f),
                    new(-inset, 0f)
                ),
                font,
                "",
                layout.ValueFont,
                TextAlignmentOptions.TopLeft,
                AndroidUiKit.TextColor
            );
            value.enableWordWrapping = false;
            value.overflowMode = TextOverflowModes.Ellipsis;
            _metricCells[index] = (label, value);
        }
        var detail = AndroidTouchScreen.Child("CorpusDetail", root, layout.Info);
        AndroidUiKit.Fill(detail, AndroidUiKit.ButtonColor, blocksTouches: false);
        _corpusDetail = AndroidUiKit.Text(
            AndroidUiKit.Rect(
                "Text",
                detail,
                Vector2.zero,
                Vector2.one,
                new(layout.Gap, layout.Gap),
                new(-layout.Gap, -layout.Gap)
            ),
            font,
            "",
            layout.BodyFont,
            TextAlignmentOptions.TopLeft,
            AndroidUiKit.TextColor
        );
        _corpusDetail.overflowMode = TextOverflowModes.Ellipsis;
        _corpusDetailRoot = detail.gameObject;

        var hint = Label(
            root,
            font,
            "Hint",
            layout.Hint,
            layout.BodyFont,
            TextAlignmentOptions.BottomLeft
        );
        hint.color = AndroidUiKit.MutedTextColor;
        hint.text = L.Resolve(TapHint);

        Inspection = new AndroidLiveBuildInspection(root, font, layout);
        _root.SetActive(false);
    }

    public LiveBuildTouchLayout Layout => _layout;

    public AndroidLiveBuildInspection Inspection { get; }

    public bool IsVisible => _root.activeSelf;

    /// <summary>Cards the four boards hold, Pending or Ready.</summary>
    public int Leases => _rows.Values.Sum(row => row.Board.Leases);

    public void SetVisible(bool visible)
    {
        if (visible)
        {
            _showCorpusDetail = false;
            _scroll.StopMovement();
            _content.anchoredPosition = Vector2.zero;
            _root.transform.SetAsLastSibling();
        }
        _root.SetActive(visible);
    }

    /// <summary>The list's scroll offset from its top, for the drag evidence.</summary>
    public float ScrollOffset => _content.anchoredPosition.y;

    /// <summary>How far the list can scroll.</summary>
    public float ScrollMax => Math.Max(0f, _layout.ScrollContentHeight - _layout.Scroll.Height);

    /// <summary>The list has come to rest after a drag and its inertia.</summary>
    public bool ScrollSettled => _scroll.velocity.sqrMagnitude < 1f;

    /// <summary>Main thread: maps the snapshot onto the view, as the desktop's view does.</summary>
    public void Refresh(LiveBuildPanelSnapshot snapshot, bool boards)
    {
        _refreshLabel.text = snapshot.FinalBuildRefreshButtonText;
        _refresh.interactable = snapshot.FinalBuildRefreshButtonEnabled;
        var summary = snapshot.CorpusState == LiveBuildCorpusState.Summary;
        _corpus.text = summary ? snapshot.CorpusFreshnessText : snapshot.CorpusStatusText;
        _corpus.color = StatusColor(
            summary ? snapshot.CorpusFreshnessSeverity : snapshot.CorpusStatusSeverity
        );
        _corpusDetail.text = string.Join(
            "\n",
            new[]
            {
                LiveBuildPanelText.CorpusLibrary(),
                snapshot.CorpusStatusText,
                snapshot.CorpusFreshnessText,
                snapshot.CorpusStatusTooltip,
            }.Where(value => !string.IsNullOrWhiteSpace(value))
        );
        _pager.text =
            snapshot.RecommendationCount > 0
                ? $"{snapshot.RecommendationIndex + 1} / {snapshot.RecommendationCount}"
                : "0 / 0";
        _previous.interactable = _next.interactable = snapshot.RecommendationCount > 1;
        _hero.text = snapshot.Hero is { } hero ? AndroidHeroNames.Display(hero) : string.Empty;
        _hasRecommendation = snapshot.MatchesState == LiveBuildMatchesState.HasRecommendation;
        _guidance.text = _hasRecommendation ? string.Empty : snapshot.MatchesGuidance;
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
        ShowInfo();
        foreach (var model in snapshot.Rows)
        {
            if (!_rows.TryGetValue(model.Board.Id, out var row))
                continue;
            row.Title.text = model.Title;
            row.EmptyText = model.EmptyText;
            row.CardCount = model.Board.Cards.Count;
            if (boards)
                row.Board.Render(
                    model.Board,
                    model.CanToggleCandidates,
                    snapshot.CandidateTemplateIds,
                    model.CanToggleCandidates
                        ? LiveBuildPanelText.CandidateTag()
                        : LiveBuildPanelText.MatchTag(),
                    _layout
                );
        }
        UpdateBoardStatus();
    }

    /// <summary>Main thread, every frame while visible: the boards' loading and failure lines.</summary>
    public void Tick() => UpdateBoardStatus();

    /// <summary>Every board returns its cards; the inspection too.</summary>
    public int ReturnAll(string reason)
    {
        Inspection.Close(reason);
        return _rows.Values.Sum(row => row.Board.ReturnAll());
    }

    public void Destroy()
    {
        ReturnAll("rebuilt");
        UnityEngine.Object.Destroy(_root);
    }

    private void ToggleCorpusDetail()
    {
        _showCorpusDetail = !_showCorpusDetail;
        ShowInfo();
    }

    private void ShowInfo()
    {
        _corpusDetailRoot.SetActive(_showCorpusDetail);
        _metrics.SetActive(!_showCorpusDetail && _hasRecommendation);
        _guidance.gameObject.SetActive(!_showCorpusDetail && !_hasRecommendation);
    }

    private void SetMetric(int index, string label, string value)
    {
        _metricCells[index].Label.text = label;
        _metricCells[index].Value.text = value;
    }

    private void UpdateBoardStatus()
    {
        foreach (var row in _rows.Values)
        {
            var (pending, failed) = row.Board.Status();
            var text =
                pending > 0 ? LiveBuildPanelText.LoadingBoard()
                : failed > 0 ? LiveBuildPanelText.BoardFailed()
                : row.CardCount == 0 ? row.EmptyText
                : string.Empty;
            if (!string.Equals(row.Status.text, text, StringComparison.Ordinal))
                row.Status.text = text;
        }
    }

    private Row CreateRow(
        RectTransform parent,
        TMP_FontAsset font,
        BppItemBoardId id,
        TouchRect titleArea,
        TouchRect boardArea,
        AndroidLiveBuildPanel.Commands commands
    )
    {
        var title = Label(
            parent,
            font,
            $"{id}Title",
            titleArea,
            _layout.RowTitleFont,
            TextAlignmentOptions.MidlineLeft
        );
        title.overflowMode = TextOverflowModes.Ellipsis;
        var region = AndroidTouchScreen.Child($"{id}Board", parent, boardArea);
        // Transparent but raycast: a drag on a board's empty sockets scrolls the list too.
        AndroidUiKit.Fill(region, new Color(0f, 0f, 0f, 0.001f));
        var status = AndroidUiKit.Text(
            AndroidUiKit.Rect(
                "Status",
                region,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero
            ),
            font,
            "",
            _layout.BodyFont,
            TextAlignmentOptions.Center,
            AndroidUiKit.MutedTextColor
        );
        var board = new AndroidLiveBuildBoard(
            region,
            font,
            id,
            (card, selectable) =>
            {
                if (selectable)
                    commands.Toggle(card.TemplateId);
                else
                    Inspection.Open(card, id.ToString());
            }
        );
        return new Row(title, status, board);
    }

    private static Button PagerButton(
        RectTransform root,
        TMP_FontAsset font,
        TouchRect area,
        string text,
        string control,
        Action onTap
    )
    {
        var rect = AndroidTouchScreen.Child(control, root, area);
        var (button, _, _) = AndroidUiKit.Button(
            rect,
            font,
            text,
            area.Height * 0.5f,
            () => AndroidUiActions.Run($"live_build.{control}", onTap)
        );
        return button;
    }

    private static TextMeshProUGUI Label(
        RectTransform parent,
        TMP_FontAsset font,
        string name,
        TouchRect area,
        float size,
        TextAlignmentOptions alignment
    ) =>
        AndroidUiKit.Text(
            AndroidTouchScreen.Child(name, parent, area),
            font,
            "",
            size,
            alignment,
            AndroidUiKit.TextColor
        );

    private static void Band(RectTransform root, Color color, TouchRect area)
    {
        if (area.Height <= 0f)
            return;
        AndroidUiKit.Fill(AndroidTouchScreen.Child("ListEdge", root, area), color);
    }

    private static (ScrollRect Scroll, RectTransform Content) VerticalScroll(RectTransform area)
    {
        var viewport = AndroidUiKit.Rect(
            "Viewport",
            area,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        // Transparent but raycast: a drag anywhere in the list scrolls it.
        AndroidUiKit.Fill(viewport, new Color(0f, 0f, 0f, 0.001f));
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = AndroidUiKit.Rect(
            "Content",
            viewport,
            new(0f, 1f),
            new(1f, 1f),
            Vector2.zero,
            Vector2.zero
        );
        content.pivot = new Vector2(0f, 1f);
        var scroll = area.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.12f;
        scroll.scrollSensitivity = 40f;
        return (scroll, content);
    }

    private static Color StatusColor(LiveBuildRefreshSeverity severity) =>
        severity switch
        {
            LiveBuildRefreshSeverity.Failure => new Color(.92f, .62f, .34f),
            LiveBuildRefreshSeverity.Success => new Color(.62f, .8f, .54f),
            LiveBuildRefreshSeverity.Pending => new Color(.59f, .76f, .89f),
            _ => AndroidUiKit.MutedTextColor,
        };

    private sealed class Row(
        TextMeshProUGUI title,
        TextMeshProUGUI status,
        AndroidLiveBuildBoard board
    )
    {
        public TextMeshProUGUI Title { get; } = title;

        public TextMeshProUGUI Status { get; } = status;

        public AndroidLiveBuildBoard Board { get; } = board;

        public string EmptyText { get; set; } = string.Empty;

        public int CardCount { get; set; }
    }
}
