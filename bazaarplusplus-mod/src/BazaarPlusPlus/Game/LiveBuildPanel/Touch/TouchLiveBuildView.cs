#nullable enable
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.LiveBuildPanel.Ui;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Game.Touch;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Infrastructure.UiTokens;
using BazaarPlusPlus.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Touch;

/// <summary>
/// The Live Build panel's touch view for one <see cref="LiveBuildTouchLayout"/>: the full sheet,
/// the pinned recommended build, one fixed board switched among current, stash and shop, and the overview
/// sidebar. It renders a <see cref="LiveBuildPanelSnapshot"/> as the desktop's
/// <c>LiveBuildPanelView</c> maps one, and hands taps to the panel. A rented card carries its own
/// canvas, which the list's <c>RectMask2D</c> does not clip, so the list draws first and opaque
/// bands above and below it hide the cards scrolled past its edges, as the Collection grid does.
/// Stacked (<see cref="LiveBuildTouchLayout.Stacked"/>), the final build's cards and the overview
/// are children of the list's content, so they scroll with the selected board; the final build's title band,
/// the pager, and the close button stay over the list.
/// </summary>
internal sealed class TouchLiveBuildView
{
    private readonly TouchNativeSkin _skin = new();
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
    private readonly TextMeshProUGUI? _hero;
    private readonly TextMeshProUGUI _boardSelector;
    private readonly TextMeshProUGUI _sponsorLabel;
    private readonly GameObject _metrics;
    private readonly (TextMeshProUGUI Label, TextMeshProUGUI Value)[] _metricCells;
    private readonly GameObject _corpusDetailRoot;
    private readonly TouchInspectionText _corpusDetail;
    private bool _showCorpusDetail;
    private bool _hasRecommendation;

    public TouchLiveBuildView(
        Transform parent,
        TMP_FontAsset font,
        LiveBuildTouchLayout layout,
        TouchLiveBuildPanel.Commands commands
    )
    {
        _layout = layout;
        var root = TouchUiKit.Rect(
            "LiveBuild",
            parent,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        // A view that throws while it builds destroys its partial sheet, which is opaque and
        // active until the last line, so a failed build never covers the screen.
        try
        {
            // Opaque: the sheet covers the whole screen, and nothing of the scene shows through.
            var background = TouchUiKit.PanelColor;
            background.a = 1f;
            TouchUiKit.Fill(root, background);
            _root = root.gameObject;
            var metrics = layout.Metrics;

            // The list first, then the bands over its edges, then everything else.
            var scrollArea = TouchScreen.Child("List", root, layout.Scroll);
            (_scroll, _content) = TouchUiKit.VerticalScroll(scrollArea);
            _content.sizeDelta = new Vector2(0f, layout.ScrollContentHeight);
            _scroll.enabled = layout.Stacked;
            foreach (
                var id in new[]
                {
                    BppItemBoardId.LiveBoard,
                    BppItemBoardId.LiveStash,
                    BppItemBoardId.LiveShop,
                }
            )
            {
                var group = TouchUiKit.Rect(
                    id.ToString(),
                    _content,
                    Vector2.zero,
                    Vector2.one,
                    Vector2.zero,
                    Vector2.zero
                );
                var row = CreateRow(
                    group,
                    group,
                    font,
                    id,
                    layout.ScrollRowTitle(0),
                    layout.ScrollRowBoard(0),
                    commands
                );
                row.Title.gameObject.SetActive(false);
                row.Group = group.gameObject;
                _rows[id] = row;
            }
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

            // Side by side the overview and the final build's cards are fixed; stacked, they scroll.
            var scrolled = layout.Stacked ? _content : root;
            (_, _, _boardSelector) = _skin.Button(
                TouchScreen.Child("BoardSelector", scrolled, layout.BoardSelector),
                font,
                "",
                layout.TitleFont,
                layout.LabelInset,
                () => TouchUiActions.Run("live_build.switch_board", commands.CycleBoard)
            );
            _rows[BppItemBoardId.FinalBuild] = CreateRow(
                root,
                scrolled,
                font,
                BppItemBoardId.FinalBuild,
                layout.PinnedTitleText,
                layout.PinnedBoard,
                commands
            );
            _previous = PagerButton(
                root,
                font,
                layout.Previous,
                layout.LabelInset,
                "<",
                "previous",
                commands.Previous
            );
            _next = PagerButton(
                root,
                font,
                layout.Next,
                layout.LabelInset,
                ">",
                "next",
                commands.Next
            );
            _pager = Label(
                root,
                font,
                "Pager",
                layout.Pager,
                layout.TitleFont,
                TextAlignmentOptions.Center
            );

            var close = TouchScreen.Child("Close", root, layout.Close);
            _skin.Button(
                close,
                font,
                TouchUiKit.CloseGlyph,
                layout.TitleFont,
                layout.LabelInset,
                () => TouchUiActions.Run("live_build.close", () => commands.Close("close_button"))
            );

            var refresh = TouchScreen.Child("Refresh", scrolled, layout.Refresh);
            (_refresh, _, _refreshLabel) = _skin.Button(
                refresh,
                font,
                "",
                layout.ButtonFont,
                layout.LabelInset,
                () => TouchUiActions.Run("live_build.refresh", commands.Refresh)
            );

            var corpus = TouchScreen.Child("CorpusStatus", scrolled, layout.Corpus);
            var statusHit = TouchUiKit.Fill(corpus, new Color(0f, 0f, 0f, 0.001f));
            var statusButton = corpus.gameObject.AddComponent<Button>();
            statusButton.targetGraphic = statusHit;
            statusButton.transition = Selectable.Transition.None;
            TouchUiActions.OnTap(statusButton, "live_build.corpus", ToggleCorpusDetail);
            _corpus = TouchUiKit.Text(
                TouchUiKit.Rect(
                    "Status",
                    corpus,
                    Vector2.zero,
                    Vector2.one,
                    new(layout.LabelInset, 0f),
                    new(-layout.LabelInset, 0f)
                ),
                font,
                "",
                layout.BodyFont,
                TextAlignmentOptions.Center,
                TouchUiKit.MutedTextColor
            );
            _corpus.textWrappingMode = TextWrappingModes.NoWrap;
            _corpus.overflowMode = TextOverflowModes.Ellipsis;
            if (layout.Stacked)
            {
                _hero = Label(
                    scrolled,
                    font,
                    "Hero",
                    layout.Hero,
                    layout.RowTitleFont,
                    TextAlignmentOptions.MidlineLeft
                );
                _hero.overflowMode = TextOverflowModes.Ellipsis;
            }

            var metricsRoot = TouchScreen.Child("Metrics", scrolled, layout.Info);
            _metrics = metricsRoot.gameObject;
            _metricCells = new (TextMeshProUGUI, TextMeshProUGUI)[LiveBuildTouchLayout.MetricCount];
            for (var index = 0; index < _metricCells.Length; index++)
            {
                var cellArea = layout.MetricCell(index);
                var cell = TouchScreen.Child(
                    $"Metric{index}",
                    metricsRoot,
                    new TouchRect(
                        cellArea.Left - layout.Info.Left,
                        cellArea.Top - layout.Info.Top,
                        cellArea.Width,
                        cellArea.Height
                    )
                );
                // The metric reads as typography, without a competing solid tile.
                var inset = layout.Gap;
                var label = TouchUiKit.Text(
                    TouchUiKit.Rect(
                        "Label",
                        cell,
                        new(0f, 0.5f),
                        new(1f, 1f),
                        new(inset, 0f),
                        new(-inset, 0f)
                    ),
                    font,
                    "",
                    layout.BodyFont,
                    TextAlignmentOptions.BottomLeft,
                    TouchUiKit.MutedTextColor
                );
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
                var value = TouchUiKit.Text(
                    TouchUiKit.Rect(
                        "Value",
                        cell,
                        new(0f, 0f),
                        new(1f, 0.5f),
                        new(inset, 0f),
                        new(-inset, 0f)
                    ),
                    font,
                    "",
                    layout.ValueFont,
                    TextAlignmentOptions.TopLeft,
                    TouchUiKit.TextColor
                );
                value.textWrappingMode = TextWrappingModes.NoWrap;
                value.enableAutoSizing = true;
                value.fontSizeMin = layout.BodyFont;
                value.fontSizeMax = layout.ValueFont;
                value.overflowMode = TextOverflowModes.Ellipsis;
                _metricCells[index] = (label, value);
            }
            var detail = TouchScreen.Child("CorpusDetail", scrolled, layout.Info);
            _skin.Frame(detail);
            _corpusDetail = new TouchInspectionText(
                TouchUiKit.Rect(
                    "Text",
                    detail,
                    Vector2.zero,
                    Vector2.one,
                    new(layout.Gap, layout.Gap),
                    new(-layout.Gap, -layout.Gap)
                ),
                font,
                layout.BodyFont,
                TouchUiKit.TextColor
            );
            _corpusDetailRoot = detail.gameObject;

            var sponsor = TouchScreen.Child("Sponsor", scrolled, layout.Sponsor);
            (_, _, _sponsorLabel) = _skin.Button(
                sponsor,
                font,
                "",
                layout.ButtonFont,
                layout.LabelInset,
                () =>
                    TouchUiActions.Run(
                        "live_build.sponsor",
                        () =>
                            Application.OpenURL(
                                BPPSupporterLinks.ResolveSponsorUrl(L.CurrentLanguageCode)
                            )
                    )
            );

            _sponsorLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _sponsorLabel.enableAutoSizing = true;
            _sponsorLabel.fontSizeMin = layout.BodyFont;
            _sponsorLabel.fontSizeMax = layout.ButtonFont;
            _sponsorLabel.overflowMode = TextOverflowModes.Ellipsis;

            Inspection = new TouchLiveBuildInspection(root, font, layout, _skin);
            _root.SetActive(false);
        }
        catch
        {
            UnityEngine.Object.Destroy(root.gameObject);
            throw;
        }
    }

    public bool NeedsSkinRefresh => _skin.NeedsRefresh;

    public LiveBuildTouchLayout Layout => _layout;

    public TouchLiveBuildInspection Inspection { get; }

    public bool IsVisible => _root.activeSelf;

    /// <summary>Cards the four boards and the inspection hold, Pending or Ready.</summary>
    public int Leases => _rows.Values.Sum(row => row.Board.Leases) + Inspection.Leases;

    /// <summary>A board card or the inspected one is still being instantiated or set up.</summary>
    public bool SettingUp
    {
        get
        {
            foreach (var row in _rows.Values)
                if (row.Board.Status().Pending > 0)
                    return true;
            return Inspection.SettingUp;
        }
    }

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
    public void Refresh(LiveBuildPanelSnapshot snapshot, bool boards, BppItemBoardId selectedBoard)
    {
        _refreshLabel.text = snapshot.FinalBuildRefreshButtonEnabled
            ? TouchPanelUi.T("Update", "更新")
            : TouchPanelUi.T("Updating…", "更新中…");
        _refresh.interactable = snapshot.FinalBuildRefreshButtonEnabled;
        var summary = snapshot.CorpusState == LiveBuildCorpusState.Summary;
        _corpus.text =
            summary ? TouchPanelUi.T("Library ready", "阵容库已就绪")
            : snapshot.CorpusStatusSeverity == LiveBuildRefreshSeverity.Failure
                ? TouchPanelUi.T("Update failed", "更新失败")
            : snapshot.CorpusStatusSeverity == LiveBuildRefreshSeverity.Pending
                ? TouchPanelUi.T("Loading…", "加载中…")
            : snapshot.CorpusStatusText;
        _corpus.color = summary
            ? TouchUiKit.MutedTextColor
            : StatusColor(snapshot.CorpusStatusSeverity);
        _corpusDetail.Set(
            string.Join(
                "\n",
                new[]
                {
                    LiveBuildPanelText.CorpusLibrary(),
                    snapshot.CorpusStatusText,
                    snapshot.CorpusFreshnessText,
                    snapshot.CorpusStatusTooltip,
                }.Where(value => !string.IsNullOrWhiteSpace(value))
            )
        );
        _pager.text =
            snapshot.RecommendationCount > 0
                ? $"{snapshot.RecommendationIndex + 1} / {snapshot.RecommendationCount}"
                : "0 / 0";
        _previous.interactable = _next.interactable = snapshot.RecommendationCount > 1;
        var heroName = snapshot.Hero is { } hero ? TouchHeroNames.Display(hero) : string.Empty;
        if (_hero != null)
            _hero.text = heroName;
        _hasRecommendation = snapshot.MatchesState == LiveBuildMatchesState.HasRecommendation;
        var language = L.CurrentLanguageCode;
        var credit = BPPSupporterAttribution.FormatRichLine(
            snapshot.Supporters.Take(1).ToArray(),
            language
        );
        _sponsorLabel.text = string.IsNullOrEmpty(credit)
            ? BPPSupporterAttributionText.FormatSponsorAction(language)
            : credit;
        TouchFonts.Cover(_sponsorLabel);
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
            var visible =
                model.Board.Id == BppItemBoardId.FinalBuild || model.Board.Id == selectedBoard;
            row.Group?.SetActive(visible);
            if (!visible)
            {
                row.Board.ReturnAll();
                continue;
            }
            if (model.Board.Id == selectedBoard)
                _boardSelector.text = $"{BoardLabel(selectedBoard)} ({model.Board.Cards.Count})  >";
            row.Title.text =
                model.Board.Id == BppItemBoardId.FinalBuild
                && !_layout.Stacked
                && heroName.Length > 0
                    ? $"{heroName} · {model.Title}"
                    : model.Title;
            row.EmptyText =
                model.Board.Id == BppItemBoardId.FinalBuild
                    ? snapshot.MatchesGuidance
                    : model.EmptyText;
            row.CardCount = model.Board.Cards.Count;
            if (boards)
                row.Board.Render(
                    model.Board,
                    model.CanToggleCandidates,
                    snapshot.CandidateTemplateIds,
                    model.CanToggleCandidates
                        ? LiveBuildPanelText.CandidateTouchTag()
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

    private static string BoardLabel(BppItemBoardId id) =>
        id switch
        {
            BppItemBoardId.LiveStash => TouchPanelUi.T("Stash", "箱子"),
            BppItemBoardId.LiveShop => TouchPanelUi.T("Shop", "商店"),
            _ => TouchPanelUi.T("Board", "场上"),
        };

    private void ToggleCorpusDetail()
    {
        _showCorpusDetail = !_showCorpusDetail;
        ShowInfo();
    }

    private void ShowInfo()
    {
        _corpusDetailRoot.SetActive(_showCorpusDetail);
        _metrics.SetActive(!_showCorpusDetail && _hasRecommendation);
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
            // Compared with what was last written, not read back through the IL2CPP getter.
            if (string.Equals(row.LastStatus, text, StringComparison.Ordinal))
                continue;
            row.LastStatus = text;
            row.Status.text = text;
        }
    }

    private Row CreateRow(
        RectTransform titleParent,
        RectTransform boardParent,
        TMP_FontAsset font,
        BppItemBoardId id,
        TouchRect titleArea,
        TouchRect boardArea,
        TouchLiveBuildPanel.Commands commands
    )
    {
        var title = Label(
            titleParent,
            font,
            $"{id}Title",
            titleArea,
            _layout.RowTitleFont,
            TextAlignmentOptions.MidlineLeft
        );
        title.overflowMode = TextOverflowModes.Ellipsis;
        var frame = TouchScreen.Child($"{id}BoardFrame", boardParent, boardArea);
        _skin.Frame(frame);
        var region = TouchUiKit.Rect(
            $"{id}Board",
            frame,
            Vector2.zero,
            Vector2.one,
            Vector2.one * _layout.Gap,
            Vector2.one * -_layout.Gap
        );
        // The frame takes empty-space drags; cards and tags stay inside its inset.

        var status = TouchUiKit.Text(
            TouchUiKit.Rect(
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
            TouchUiKit.MutedTextColor
        );
        var board = new TouchLiveBuildBoard(
            region,
            font,
            id,
            _skin,
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

    private Button PagerButton(
        RectTransform root,
        TMP_FontAsset font,
        TouchRect area,
        float labelInset,
        string text,
        string control,
        Action onTap
    )
    {
        var rect = TouchScreen.Child(control, root, area);
        var (button, _, _) = _skin.Button(
            rect,
            font,
            text,
            Math.Min(area.Height * 0.5f, _layout.Metrics.Sp(32f)),
            labelInset,
            () => TouchUiActions.Run($"live_build.{control}", onTap)
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
        TouchUiKit.Text(
            TouchScreen.Child(name, parent, area),
            font,
            "",
            size,
            alignment,
            TouchUiKit.TextColor
        );

    private static void Band(RectTransform root, Color color, TouchRect area)
    {
        if (area.Height <= 0f)
            return;
        TouchUiKit.Fill(TouchScreen.Child("ListEdge", root, area), color);
    }

    private static Color StatusColor(LiveBuildRefreshSeverity severity) =>
        severity switch
        {
            LiveBuildRefreshSeverity.Failure => InGameTheme.Warning,
            LiveBuildRefreshSeverity.Success => InGameTheme.Success,
            LiveBuildRefreshSeverity.Pending => InGameTheme.Pending,
            _ => TouchUiKit.MutedTextColor,
        };

    private sealed class Row(
        TextMeshProUGUI title,
        TextMeshProUGUI status,
        TouchLiveBuildBoard board
    )
    {
        public GameObject? Group { get; set; }

        public TextMeshProUGUI Title { get; } = title;

        public TextMeshProUGUI Status { get; } = status;

        public TouchLiveBuildBoard Board { get; } = board;

        public string EmptyText { get; set; } = string.Empty;

        public int CardCount { get; set; }

        /// <summary>The status text last written; the label is created empty.</summary>
        public string LastStatus { get; set; } = string.Empty;
    }
}
