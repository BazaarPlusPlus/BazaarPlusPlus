using System.Globalization;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Supporters;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The Android twin of the desktop's <c>HistoryPanelView</c>, a full-screen sheet laid out from
/// Core's <see cref="HistoryTouchLayout"/> for the screen it was built on
/// (<see cref="ScreenKey"/>) and bound from Core's <see cref="HistoryPanelViewModel"/>. The header
/// holds the section tabs, the supporter action, More, and Close; the archive list, the run's
/// battle rail (in Ghost, the cloud note in its place), and the detail column with the two board regions and the action row (delete, favorite, skills, replay) stand under it.
/// Lists drag-scroll with inertia; each finished gesture logs <c>android.history.scrolled</c>.
/// Hero portraits share the Collection framing; rank and trophy details render as text;
/// the two BPP badge atlases ship in the assembly. The panel rents the native boards into
/// <see cref="UpperBoard"/> and <see cref="LowerBoard"/> and lays its overlays over
/// <see cref="Surface"/>; a new screen size builds a new view.
/// </summary>
internal sealed class AndroidHistoryPanelView : IDisposable
{
    public const int SortingOrder = 29000;
    private const int Rows = 40;

    private readonly TMP_FontAsset _font;
    private readonly ViewActions _actions;
    private readonly HistoryTouchLayout _l;
    private readonly GameObject _root;
    private readonly AndroidHistoryBadges? _badges;
    private readonly List<AndroidScrollTracker> _trackers = new();
    private HistoryPanelViewModel? _model;
    private bool _visible;
    private bool _disposed;

    private HistoryButton _runsTab = null!,
        _ghostTab = null!,
        _favoritesTab = null!,
        _more = null!,
        _newer = null!,
        _older = null!,
        _latest = null!,
        _railNewer = null!,
        _railOlder = null!,
        _delete = null!,
        _favorite = null!,
        _skills = null!,
        _replay = null!;
    private readonly AndroidSupporterData? _supporters;
    private IReadOnlyList<BPPSupporterSample> _headerSupporters = Array.Empty<BPPSupporterSample>();
    private TextMeshProUGUI _supporterLabel = null!;
    private GameObject _cloudNote = null!;
    private PageList _archive = null!;
    private TextMeshProUGUI _archiveEmpty = null!;
    private Rail _rail = null!;
    private TextMeshProUGUI _pageLabel = null!,
        _status = null!,
        _upperTitle = null!,
        _lowerTitle = null!,
        _upperStatus = null!,
        _lowerStatus = null!;
    private GameObject _lowerTitleObject = null!,
        _lowerBoardObject = null!,
        _upperStatusBand = null!,
        _lowerStatusBand = null!;
    private HistorySectionMode? _boundSection;

    public AndroidHistoryPanelView(
        TMP_FontAsset font,
        ViewActions actions,
        AndroidSupporterData? supporters
    )
    {
        _supporters = supporters;
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        ScreenKey = AndroidTouchScreen.Key();
        _l = HistoryTouchLayout.For(AndroidTouchScreen.Read());
        _badges = AndroidHistoryBadges.TryLoad();
        _root = AndroidUiKit.CreateOverlayCanvas("BPP.HistoryPanel", SortingOrder);
        Surface = AndroidUiKit.Rect(
            "Layout",
            _root.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        AndroidHistoryUi.RefreshSkin();
        Build();
        _root.SetActive(false);
        var dp = _l.Metrics.UnitsPerDp;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.history.view_created"),
            ("screen", ScreenKey),
            ("canvas_width", Math.Round(_l.Metrics.CanvasWidth)),
            ("units_per_dp", Math.Round(dp, 3)),
            ("socket_dp", Math.Round(_l.SocketWidth / dp, 1)),
            ("card_height_dp", Math.Round(_l.CardHeight / dp, 1)),
            ("archive_dp", Math.Round(_l.Archive.Width / dp, 1)),
            ("target_dp", Math.Round(_l.Metrics.MinTarget / dp, 1)),
            ("body_sp", Math.Round(_l.BodyFont / dp, 1)),
            ("badges", _badges != null)
        );
    }

    /// <summary>The screen the view was laid out for (<see cref="AndroidTouchScreen.Key"/>).</summary>
    public string ScreenKey { get; }

    public HistoryTouchLayout Layout => _l;

    /// <summary>The canvas-wide parent the panel's overlays are laid over.</summary>
    public RectTransform Surface { get; }

    /// <summary>The opponent's board with two boards, the challenger's with one.</summary>
    public RectTransform UpperBoard { get; private set; } = null!;

    public RectTransform LowerBoard { get; private set; } = null!;

    public int RootCount => _disposed ? 0 : 1;

    public void SetVisible(bool visible)
    {
        _visible = visible;
        AndroidHistoryUi.SetActive(_root, visible);
        if (visible)
        {
            _supporters?.Warm();
            _headerSupporters = _supporters?.Next(1) ?? Array.Empty<BPPSupporterSample>();
            RefreshSupporter();
            UpdateContent();
        }
    }

    public void Refresh(HistoryPanelViewModel model)
    {
        _model = model;
        if (_visible)
            UpdateContent();
    }

    /// <summary>The board messages, each under the board it describes.</summary>
    public void SetBoardStatus(string upperMessage, string lowerMessage)
    {
        ShowStatus(_upperStatusBand, _upperStatus, _l.UpperBoard, upperMessage);
        ShowStatus(_lowerStatusBand, _lowerStatus, _l.LowerBoard, lowerMessage);
    }

    // A board message's band grows to the wrapped text, so a long message is never cut.
    private void ShowStatus(GameObject band, TextMeshProUGUI label, TouchRect board, string text)
    {
        label.text = text;
        AndroidHistoryUi.SetActive(band, text.Length > 0);
        if (text.Length == 0)
            return;
        var lines = label.GetPreferredValues(text, board.Width * StatusTextShare, 0f).y;
        var height = Math.Min(board.Height, Math.Max(_l.Metrics.Dp(StatusMinDp), lines + _l.Gap));
        AndroidTouchScreen.Place(
            band.GetComponent<RectTransform>(),
            new TouchRect(board.Left, board.Bottom - height, board.Width, height)
        );
    }

    /// <summary>The skills action, available while the selected battle's boards are loaded.</summary>
    public void SetSkillsAvailable(bool available) =>
        _skills.Set(AndroidHistoryUi.T("Skills", "技能"), enabled: available, dimmed: !available);

    /// <summary>Main thread, every frame while visible: logs each finished drag scroll.</summary>
    public void Tick()
    {
        if (!_visible || _disposed)
            return;
        if (_headerSupporters.Count == 0)
        {
            _headerSupporters = _supporters?.Next(1) ?? Array.Empty<BPPSupporterSample>();
            if (_headerSupporters.Count > 0)
                RefreshSupporter();
        }
        var pressed = AndroidScrollTracker.PointerPressed();
        foreach (var tracker in _trackers)
            tracker.Tick(pressed, _l.Metrics.UnitsPerDp);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        UnityEngine.Object.Destroy(_root);
        _badges?.Dispose();
    }

    private void Build()
    {
        AndroidUiKit.Fill(Surface, AndroidHistoryUi.Backdrop);
        AndroidHistoryUi.Skin.Frame(
            AndroidTouchScreen.Child("Sheet", Surface, _l.Sheet),
            AndroidHistoryUi.PanelFill
        );
        var tab = _l.ButtonFont;
        _runsTab = AndroidHistoryUi.Button(
            Surface,
            _font,
            "tab_runs",
            _l.RunsTab,
            tab,
            () => _actions.Section(HistorySectionMode.Runs)
        );
        _ghostTab = AndroidHistoryUi.Button(
            Surface,
            _font,
            "tab_ghost",
            _l.GhostTab,
            tab,
            () => _actions.Section(HistorySectionMode.Ghost)
        );
        _favoritesTab = AndroidHistoryUi.Button(
            Surface,
            _font,
            "tab_favorites",
            _l.FavoritesTab,
            tab,
            () => _actions.Section(HistorySectionMode.Favorites)
        );
        BuildSupporter();
        BuildCloudNote();
        _more = AndroidHistoryUi.Button(
            Surface,
            _font,
            "more",
            _l.More,
            tab,
            () => _actions.More()
        );
        _more.Set(AndroidHistoryUi.T("More", "更多"));
        AndroidHistoryUi
            .Button(Surface, _font, "close", _l.Close, _l.TitleFont, () => _actions.Close())
            .Set(AndroidUiKit.CloseGlyph);

        _archive = BuildArchive();
        _archiveEmpty = AndroidHistoryUi.Label(
            _archive.Scroll.viewport,
            _font,
            "Empty",
            new TouchRect(
                _l.Gap,
                _l.Gap,
                _l.ArchiveList.Width - 2f * _l.Gap,
                _l.ArchiveList.Height - 2f * _l.Gap
            ),
            _l.BodyFont,
            TextAlignmentOptions.TopLeft,
            AndroidHistoryUi.Muted,
            wrap: true
        );
        _archiveEmpty.overflowMode = TextOverflowModes.Truncate;
        _pageLabel = AndroidHistoryUi.Label(
            Surface,
            _font,
            "PageLabel",
            _l.PageLabel,
            _l.BodyFont,
            TextAlignmentOptions.MidlineLeft,
            AndroidHistoryUi.Muted,
            wrap: true
        );
        _newer = AndroidHistoryUi.Button(
            Surface,
            _font,
            "archive_newer",
            _l.PagerButton(0),
            _l.ButtonFont,
            () => NavigateArchive(-1)
        );
        _newer.Set(AndroidHistoryUi.T("Newer", "较新"));
        _newer.UseChevron(180f, _l.Metrics.Dp(18f));
        _older = AndroidHistoryUi.Button(
            Surface,
            _font,
            "archive_older",
            _l.PagerButton(2),
            _l.ButtonFont,
            () => NavigateArchive(1)
        );
        _older.Set(AndroidHistoryUi.T("Older", "较早"));
        _older.UseChevron(0f, _l.Metrics.Dp(18f));
        _latest = AndroidHistoryUi.Button(
            Surface,
            _font,
            "archive_latest",
            _l.PagerButton(1),
            _l.ButtonFont,
            () => NavigateArchive(0)
        );
        _latest.Set(AndroidHistoryUi.T("Latest", "最新"));

        _railNewer = AndroidHistoryUi.Button(
            Surface,
            _font,
            "timeline_newer",
            _l.RailNewer,
            _l.ButtonFont,
            () => _actions.PageBattles(-1)
        );
        _railNewer.UseChevron(90f, _l.Metrics.Dp(18f));
        _railOlder = AndroidHistoryUi.Button(
            Surface,
            _font,
            "timeline_older",
            _l.RailOlder,
            _l.ButtonFont,
            () => _actions.PageBattles(1)
        );
        _railOlder.UseChevron(-90f, _l.Metrics.Dp(18f));
        _rail = BuildRail();

        _upperTitle = AndroidHistoryUi.Label(
            Surface,
            _font,
            "UpperTitle",
            _l.UpperBoardTitle,
            _l.BodyFont,
            color: AndroidHistoryUi.Muted
        );
        UpperBoard = BoardRegion("UpperBoard", _l.UpperBoard);
        (_upperStatusBand, _upperStatus) = StatusBand("UpperStatus", _l.UpperBoard);
        _lowerTitle = AndroidHistoryUi.Label(
            Surface,
            _font,
            "LowerTitle",
            _l.LowerBoardTitle,
            _l.BodyFont,
            color: AndroidHistoryUi.Muted
        );
        _lowerTitleObject = _lowerTitle.gameObject;
        LowerBoard = BoardRegion("LowerBoard", _l.LowerBoard);
        _lowerBoardObject = LowerBoard.gameObject;
        (_lowerStatusBand, _lowerStatus) = StatusBand("LowerStatus", _l.LowerBoard);

        _delete = AndroidHistoryUi.Button(
            Surface,
            _font,
            "delete",
            _l.Delete,
            _l.ButtonFont,
            () => _actions.Delete()
        );
        _favorite = AndroidHistoryUi.Button(
            Surface,
            _font,
            "favorite",
            _l.Favorite,
            _l.ButtonFont,
            () => _actions.Favorite()
        );
        _skills = AndroidHistoryUi.Button(
            Surface,
            _font,
            "skills",
            _l.Skills,
            _l.ButtonFont,
            () => _actions.Skills()
        );
        SetSkillsAvailable(false);
        _replay = AndroidHistoryUi.Button(
            Surface,
            _font,
            "replay",
            _l.Replay,
            _l.RowTitleFont,
            () => _actions.Replay()
        );
        _status = AndroidHistoryUi.Label(
            Surface,
            _font,
            "Status",
            _l.Status,
            _l.BodyFont,
            color: AndroidHistoryUi.Muted
        );
        SetBoardStatus(string.Empty, string.Empty);
    }

    private void BuildSupporter()
    {
        (_, _, _supporterLabel) = AndroidHistoryUi.Skin.Button(
            AndroidTouchScreen.Child("Supporter", Surface, _l.Sponsor),
            _font,
            BPPSupporterAttributionText.FormatSponsorAction(L.CurrentLanguageCode),
            _l.ButtonFont,
            _l.Metrics.Dp(TouchChrome.LabelInsetDp),
            () =>
                AndroidUiActions.Run(
                    "history.sponsor",
                    () =>
                        Application.OpenURL(
                            BPPSupporterLinks.ResolveSponsorUrl(L.CurrentLanguageCode)
                        )
                )
        );
        _supporterLabel.enableWordWrapping = false;
        _supporterLabel.enableAutoSizing = true;
        _supporterLabel.fontSizeMin = _l.BodyFont;
        _supporterLabel.fontSizeMax = _l.ButtonFont;
        _supporterLabel.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void RefreshSupporter()
    {
        var credit = BPPSupporterAttribution.FormatRichLine(
            _headerSupporters,
            L.CurrentLanguageCode
        );
        _supporterLabel.text = string.IsNullOrEmpty(credit)
            ? BPPSupporterAttributionText.FormatSponsorAction(L.CurrentLanguageCode)
            : credit;
        AndroidGameFont.Cover(_supporterLabel);
    }

    private void BuildCloudNote()
    {
        var cloudNote = AndroidHistoryUi.Label(
            Surface,
            _font,
            "CloudNote",
            _l.CloudNote,
            _l.BodyFont,
            TextAlignmentOptions.TopLeft,
            AndroidHistoryUi.Muted,
            wrap: true
        );
        cloudNote.text = AndroidHistoryUi.T(
            "Cloud: latest 5 days, up to 200",
            "云端：最近5天，最多200条"
        );
        _cloudNote = cloudNote.gameObject;
    }

    private PageList BuildArchive()
    {
        var (scroll, content) = AndroidHistoryUi.Scroll(
            Surface,
            "Archive",
            _l.ArchiveList,
            horizontal: false
        );
        _trackers.Add(new AndroidScrollTracker(Scrolled(), "archive", scroll));
        var list = new PageList { Scroll = scroll, Height = _l.RunRowHeight };
        for (var i = 0; i < Rows; i++)
        {
            var index = i;
            var rect = AndroidTouchScreen.Child("Row", content, _l.ArchiveRow(i, runs: true));
            var control = AndroidHistoryUi.Button(
                rect,
                _font,
                "archive_row",
                _l.BodyFont,
                () =>
                {
                    if (_model?.SectionMode == HistorySectionMode.Runs)
                        _actions.SelectRun(index);
                    else
                        _actions.SelectBattle(index);
                }
            );
            // Every row reads as name, meta and stamp lines, so the button's own label stays off.
            AndroidHistoryUi.SetActive(control.Label.gameObject, false);
            var row = new Row
            {
                Rect = rect,
                Control = control,
                Name = AndroidHistoryUi.Fraction(
                    rect,
                    _font,
                    .26f,
                    .03f,
                    .46f,
                    .36f,
                    _l.RowTitleFont
                ),
                Meta = AndroidHistoryUi.Fraction(
                    rect,
                    _font,
                    .26f,
                    .38f,
                    .46f,
                    .3f,
                    _l.BodyFont,
                    color: AndroidHistoryUi.Muted
                ),
                Stamp = AndroidHistoryUi.Fraction(
                    rect,
                    _font,
                    .04f,
                    .66f,
                    .68f,
                    .3f,
                    _l.BodyFont,
                    color: AndroidHistoryUi.Muted
                ),
                Badge = BadgeImage(rect),
                Rank = AndroidHistoryUi.Fraction(
                    rect,
                    _font,
                    .73f,
                    .46f,
                    .26f,
                    .26f,
                    _l.BodyFont,
                    TextAlignmentOptions.Center
                ),
                Rating = AndroidHistoryUi.Fraction(
                    rect,
                    _font,
                    .73f,
                    .71f,
                    .26f,
                    .27f,
                    _l.BodyFont,
                    TextAlignmentOptions.Center
                ),
            };
            var portrait = AndroidUiKit.Fixed(
                "HeroPortrait",
                rect,
                new(0f, 1f),
                new(_l.Metrics.Dp(8f), -_l.Metrics.Dp(6f)),
                Vector2.one * _l.Metrics.Dp(36f)
            );
            row.Portrait = new AndroidPortraitView(portrait, AndroidHistoryUi.Skin, _l.Metrics);
            row.Unknown = AndroidHistoryUi.Fraction(
                row.Badge.transform,
                _font,
                0f,
                0f,
                1f,
                1f,
                _l.BodyFont,
                TextAlignmentOptions.Center,
                AndroidHistoryUi.Muted
            );
            rect.gameObject.SetActive(false);
            list.Rows[i] = row;
        }
        return list;
    }

    private Rail BuildRail()
    {
        var (scroll, content) = AndroidHistoryUi.Scroll(
            Surface,
            "Rail",
            _l.RailList,
            horizontal: false
        );
        _trackers.Add(new AndroidScrollTracker(Scrolled(), "rail", scroll));
        var rail = new Rail { Scroll = scroll };
        for (var i = 0; i < Rows; i++)
        {
            var slot = i;
            var rect = AndroidTouchScreen.Child("Chip", content, _l.RailChip(i));
            var control = AndroidHistoryUi.Button(
                rect,
                _font,
                "timeline_chip",
                _l.BodyFont,
                () => _actions.SelectBattle(slot)
            );
            control.Label.gameObject.SetActive(false);
            var outcome = AndroidUiKit.Rect(
                "Outcome",
                rect,
                new Vector2(.08f, .04f),
                new Vector2(.92f, .1f),
                Vector2.zero,
                Vector2.zero
            );
            rail.Chips[i] = new Chip
            {
                Rect = rect,
                Control = control,
                Day = AndroidHistoryUi.Fraction(
                    rect,
                    _font,
                    .04f,
                    .04f,
                    .92f,
                    .42f,
                    _l.BodyFont,
                    TextAlignmentOptions.Center
                ),
                Name = AndroidHistoryUi.Fraction(
                    rect,
                    _font,
                    .04f,
                    .44f,
                    .92f,
                    .42f,
                    _l.BodyFont,
                    TextAlignmentOptions.Center,
                    AndroidHistoryUi.Muted
                ),
                Outcome = AndroidUiKit.Fill(outcome, Color.clear, blocksTouches: false),
            };
            rect.gameObject.SetActive(false);
        }
        return rail;
    }

    private RectTransform BoardRegion(string name, TouchRect area)
    {
        var region = AndroidTouchScreen.Child(name, Surface, area);
        AndroidUiKit.Fill(region, AndroidHistoryUi.ButtonDimmed, blocksTouches: false);
        return region;
    }

    // A shaded band along the board's bottom edge: a later sibling, so it draws over the cards.
    private const float StatusMinDp = 44f;
    private const float StatusTextShare = .96f;

    private (GameObject Band, TextMeshProUGUI Label) StatusBand(string name, TouchRect board)
    {
        var height = Math.Min(board.Height, _l.Metrics.Dp(StatusMinDp));
        var band = AndroidTouchScreen.Child(
            name,
            Surface,
            new TouchRect(board.Left, board.Bottom - height, board.Width, height)
        );
        AndroidUiKit.Fill(band, AndroidHistoryUi.Shade, blocksTouches: false);
        var label = AndroidHistoryUi.Fraction(
            band,
            _font,
            (1f - StatusTextShare) / 2f,
            0f,
            StatusTextShare,
            1f,
            _l.BodyFont,
            TextAlignmentOptions.Center,
            AndroidHistoryUi.Ink,
            wrap: true
        );
        return (band.gameObject, label);
    }

    private Image BadgeImage(RectTransform row)
    {
        var rect = AndroidUiKit.Rect(
            "Outcome",
            row,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero
        );
        Frac(rect, .76f, .05f, .2f, .4f);
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.color = Color.clear;
        return image;
    }

    private void NavigateArchive(int direction)
    {
        _archive.ResetScroll = true;
        _actions.PageArchive(direction);
    }

    private void UpdateContent()
    {
        if (_disposed || _model is not { } m)
            return;
        var runs = m.SectionMode == HistorySectionMode.Runs;
        var ghost = m.SectionMode == HistorySectionMode.Ghost;
        var favorites = m.SectionMode == HistorySectionMode.Favorites;
        var both = m.ShowsBothBoards;
        if (_boundSection != m.SectionMode)
        {
            _boundSection = m.SectionMode;
            _archive.ResetScroll = true;
            ApplyRowMode(runs);
            SyncTrackers();
        }
        _runsTab.Set(HistoryPanelText.RunsTab(), runs);
        _ghostTab.Set(HistoryPanelText.GhostTab(), ghost);
        AndroidHistoryUi.SetActive(_favoritesTab.GameObject, m.FavoritesTabVisible);
        _favoritesTab.Set(HistoryPanelText.FavoritesTab(), favorites);
        AndroidHistoryUi.SetActive(_cloudNote, ghost);

        AndroidHistoryUi.SetActive(_rail.Scroll.gameObject, runs);
        AndroidHistoryUi.SetActive(_railNewer.GameObject, runs);
        AndroidHistoryUi.SetActive(_railOlder.GameObject, runs);
        _railNewer.Set(AndroidHistoryUi.T("Newer", "较新"), enabled: m.BattleHasNewer);
        _railOlder.Set(AndroidHistoryUi.T("Older", "较早"), enabled: m.BattleHasOlder);
        _newer.Set(AndroidHistoryUi.T("Newer", "较新"), enabled: m.HasNewer && !m.PageLoading);
        _older.Set(AndroidHistoryUi.T("Older", "较早"), enabled: m.HasOlder && !m.PageLoading);
        _latest.Set(AndroidHistoryUi.T("Latest", "最新"), enabled: !m.PageLoading);
        _pageLabel.text = m.PageLabel.Split('\n')[0];
        _archiveEmpty.text = m.ArchiveEmptyMessage;
        AndroidHistoryUi.SetActive(_archiveEmpty.gameObject, m.ArchiveEmptyMessage.Length > 0);

        if (runs)
            BindRows(
                m.Runs.Select(r => r.RunId).ToArray(),
                _ => string.Empty,
                i => m.Runs[i].Hero,
                i => RunBadge(m.Runs[i]),
                i => m.Runs[i].PlayerRank,
                i => m.Runs[i].PlayerRating,
                i => m.Runs[i].PlayerRatingDelta,
                m.SelectedRunIndex,
                i => CompactRunFields(m.Runs[i])
            );
        else
            BindRows(
                m.VisibleBattles.Select(b => b.BattleId).ToArray(),
                i =>
                    ghost ? HistoryPanelFormatter.GhostListText(m.VisibleBattles[i])
                    : i < m.FavoriteRowLabels.Count ? m.FavoriteRowLabels[i]
                    : string.Empty,
                i => m.VisibleBattles[i].OpponentHero,
                i => BattleBadge(m.VisibleBattles[i]),
                i => m.VisibleBattles[i].OpponentRank,
                i => m.VisibleBattles[i].OpponentRating,
                _ => null,
                m.SelectedBattleIndex
            );
        if (runs)
            BindRail(m.VisibleBattles, m.SelectedBattleIndex, m.PageLoading);

        // Two boards show yours below the opponent's; one board is the challenger's (ADR-0002).
        _upperTitle.text = both
            ? HistoryPanelText.BoardOpponent()
            : HistoryPanelText.BoardChallenger();
        _lowerTitle.text = HistoryPanelText.BoardYou();
        AndroidHistoryUi.SetActive(_lowerTitleObject, both);
        AndroidHistoryUi.SetActive(_lowerBoardObject, both);
        if (!both)
            AndroidHistoryUi.SetActive(_lowerStatusBand, false);

        AndroidHistoryUi.SetActive(_delete.GameObject, m.DeleteButtonVisible);
        // Dimmed but tappable: the tap puts the reason on the status line.
        _delete.Set(m.DeleteButtonText, enabled: !m.PageLoading, dimmed: !m.DeleteButtonEnabled);
        AndroidHistoryUi.SetActive(_favorite.GameObject, m.FavoriteButtonVisible);
        _favorite.Set(m.FavoriteButtonText, enabled: m.FavoriteButtonEnabled && !m.PageLoading);
        _replay.Set(m.ReplayButtonText, true, m.ReplayButtonEnabled && !m.PageLoading);
        _status.text = m.StatusMessage ?? string.Empty;
        _status.color = AndroidHistoryUi.StatusColor(m.StatusSeverity);
    }

    // Archive rows carry runs in Runs and battles in Ghost and Favorites.
    private void ApplyRowMode(bool runs)
    {
        _archive.Height = runs ? _l.RunRowHeight : _l.BattleRowHeight;
        for (var i = 0; i < Rows; i++)
        {
            var row = _archive.Rows[i];
            AndroidTouchScreen.Place(row.Rect, _l.ArchiveRow(i, runs));
        }
    }

    private void BindRows(
        IReadOnlyList<string> ids,
        Func<int, string> label,
        Func<int, string?> hero,
        Func<int, (Sprite? Sprite, string Fallback)> badge,
        Func<int, string?> rank,
        Func<int, int?> rating,
        Func<int, int?> ratingDelta,
        int selected,
        Func<int, HistoryRunRowFields>? runFields = null
    )
    {
        var list = _archive;
        var offset = Mathf.Max(0, list.Scroll.content.anchoredPosition.y);
        var oldTop = Math.Min(Rows - 1, (int)(offset / list.Height));
        var topId = list.Rows[oldTop].Id;
        var newTop = -1;
        for (var i = 0; i < Rows; i++)
        {
            var row = list.Rows[i];
            var shown = i < ids.Count;
            AndroidHistoryUi.SetActive(row.Rect.gameObject, shown);
            if (!shown)
            {
                row.Id = null;
                continue;
            }
            row.Id = ids[i];
            if (row.Id == topId)
                newTop = i;
            row.Control.Set(string.Empty, i == selected, !_model!.PageLoading);
            // A battle row's three label lines become name, hero with day, and stamp: a separate
            // hero column left the name too narrow on a phone.
            var fields = runFields?.Invoke(i) ?? BattleRowFields(label(i), HeroLabel(hero(i)));
            var portraitHero = ParseHero(hero(i));
            if (row.Hero != portraitHero)
            {
                row.Hero = portraitHero;
                row.Portrait.Bind(
                    portraitHero is { } value ? () => AndroidPortraits.Hero(value) : null,
                    portraitHero
                );
            }
            row.Name.text = fields.Name;
            row.Meta.text = fields.Meta;
            row.Stamp.text = fields.Stamp;
            var outcome = badge(i);
            row.Badge.sprite = outcome.Sprite;
            row.Badge.color = outcome.Sprite != null ? Color.white : Color.clear;
            row.Unknown.text = outcome.Fallback;
            AndroidHistoryUi.SetActive(row.Unknown.gameObject, outcome.Sprite == null);
            row.Rank.text = string.IsNullOrWhiteSpace(rank(i)) ? "?" : rank(i)!.Trim();
            row.Rating.text = RatingText(rank(i), rating(i), ratingDelta(i));
            // The stamp runs under the rating column whenever that column is empty, which keeps
            // "Favorited <date> <time>" whole on a phone.
            var stamp = row.Stamp.rectTransform;
            stamp.anchorMax = new Vector2(
                row.Rating.text.Length == 0 ? .96f : .72f,
                stamp.anchorMax.y
            );
        }
        list.Scroll.content.sizeDelta = new Vector2(_l.ArchiveList.Width, ids.Count * list.Height);
        var nextOffset =
            list.ResetScroll ? 0
            : newTop >= 0 ? newTop * list.Height + offset % list.Height
            : Math.Max(0, selected) * list.Height;
        list.Scroll.StopMovement();
        list.Scroll.content.anchoredPosition = new Vector2(
            0,
            Mathf.Clamp(
                nextOffset,
                0,
                Mathf.Max(0, ids.Count * list.Height - _l.ArchiveList.Height)
            )
        );
        list.ResetScroll = false;
        SyncTrackers();
    }

    private void BindRail(IReadOnlyList<HistoryBattleRecord> battles, int selected, bool loading)
    {
        var rail = _rail;
        for (var i = 0; i < Rows; i++)
        {
            var chip = rail.Chips[i];
            var shown = i < battles.Count;
            AndroidHistoryUi.SetActive(chip.Rect.gameObject, shown);
            if (!shown)
                continue;
            // The page's newest-first order, matching the archive and the pager.
            var battle = battles[i];
            chip.Control.Set(string.Empty, i == selected, !loading);
            chip.Day.text = HistoryPanelText.DayBadge(battle.Day);
            chip.Name.text = string.IsNullOrWhiteSpace(battle.OpponentName)
                ? HistoryPanelText.UnknownOpponent()
                : battle.OpponentName;
            chip.Outcome.color =
                HistoryPanelFormatter.IsBattleWin(battle) ? AndroidHistoryUi.Win
                : HistoryPanelFormatter.IsBattleLoss(battle) ? AndroidHistoryUi.Loss
                : Color.clear;
        }
        rail.Scroll.content.sizeDelta = new Vector2(
            _l.RailList.Width,
            battles.Count * _l.ChipHeight
        );
        var hasSelection = selected >= 0 && selected < battles.Count;
        var position = rail.Scroll.content.anchoredPosition;
        var offset = rail.Position.Bind(
            hasSelection ? battles[selected].BattleId : null,
            hasSelection ? selected : -1,
            battles.Count,
            position.y,
            _l.RailList.Height,
            _l.ChipHeight
        );
        if (position.y != offset)
        {
            rail.Scroll.StopMovement();
            rail.Scroll.content.anchoredPosition = new Vector2(position.x, offset);
        }
        SyncTrackers();
    }

    // A finished run shows its outcome tier as text (the desktop's trophies are native art);
    // unfinished runs keep the BPP state badges.
    private (Sprite? Sprite, string Fallback) RunBadge(HistoryRunRecord run)
    {
        var tier = HistoryPanelFormatter.GetRunOutcomeTier(run);
        if (tier is { } finished && finished != RunOutcomeTier.Misfortune)
            return (null, finished.ToString());
        var index =
            run.RawStatus == "active" ? 2
            : run.RawStatus == "abandoned" ? 1
            : 0;
        return (_badges?.RunState(index), "?");
    }

    private (Sprite? Sprite, string Fallback) BattleBadge(HistoryBattleRecord battle) =>
        HistoryPanelFormatter.IsBattleWin(battle) ? (_badges?.Win, "W")
        : HistoryPanelFormatter.IsBattleLoss(battle) ? (_badges?.Loss, "L")
        : (null, "?");

    // A Legendary rank carries its rating; the desktop's rank badges are native art.
    private static HistoryRunRowFields BattleRowFields(string label, string hero)
    {
        var lines = label.Split(new[] { '\n' }, 3);
        var day = lines.Length > 1 ? lines[1] : string.Empty;
        return new HistoryRunRowFields(
            lines[0],
            hero.Length == 0 ? day
                : day.Length == 0 ? hero
                : $"{hero} · {day}",
            lines.Length > 2 ? lines[2] : string.Empty
        );
    }

    private static string RatingText(string? rank, int? rating, int? ratingDelta) =>
        string.Equals(rank?.Trim(), "Legendary", StringComparison.OrdinalIgnoreCase)
        && rating.HasValue
            ? HistoryPanelFormatter.RatingWithDelta(rating.GetValueOrDefault(), ratingDelta)
            : string.Empty;

    /// <summary>A stored hero id under the game's own hero name.</summary>
    private static CardHero? ParseHero(string? hero) =>
        TheDragonsHeroId.IsAlias(hero) ? CardHero.TheDragons
        : Enum.TryParse<CardHero>(hero, true, out var parsed) ? parsed
        : null;

    private static HistoryRunRowFields CompactRunFields(HistoryRunRecord run)
    {
        var mode = run.GameMode.Trim().ToLowerInvariant() switch
        {
            "ranked" => AndroidHistoryUi.T("Ranked", "排位"),
            "unranked" => AndroidHistoryUi.T("Normal", "普通"),
            _ => HistoryPanelText.Unknown(),
        };
        var title = $"{HistoryPanelText.DayBadge(run.FinalDay)} · {mode}";
        var duration = run.EndedAtUtc is { } ended
            ? HistoryPanelText.DurationMinutes(
                Math.Max(0, (int)(ended - run.StartedAtUtc).TotalMinutes)
            )
            : string.Empty;
        var date = (run.EndedAtUtc ?? run.LastSeenAtUtc).ToLocalTime();
        var stamp = date.ToString(
            date.Year == DateTimeOffset.Now.Year ? "MM/dd HH:mm" : "yyyy/MM/dd HH:mm",
            CultureInfo.CurrentCulture
        );
        return new(title, duration, stamp);
    }

    public static string HeroLabel(string? hero)
    {
        if (TheDragonsHeroId.IsAlias(hero))
            return AndroidHeroNames.TheDragons();
        if (!string.IsNullOrWhiteSpace(hero) && Enum.TryParse<CardHero>(hero, true, out var parsed))
            return AndroidHeroNames.Display(parsed);
        return hero ?? string.Empty;
    }

    private static void Frac(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(x, 1f - y - height);
        rect.anchorMax = new Vector2(x + width, 1f - y);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static BppLogEvent Scrolled() =>
        new(BppLogFeatureScope.Android, "android.history.scrolled");

    private void SyncTrackers()
    {
        foreach (var tracker in _trackers)
            tracker.Sync();
    }

    private sealed class Row
    {
        internal RectTransform Rect = null!;
        internal HistoryButton Control = null!;
        internal Image Badge = null!;
        internal AndroidPortraitView Portrait = null!;
        internal CardHero? Hero;
        internal TextMeshProUGUI Unknown = null!;
        internal TextMeshProUGUI Rank = null!;
        internal TextMeshProUGUI Rating = null!;
        internal TextMeshProUGUI Name = null!;
        internal TextMeshProUGUI Meta = null!;
        internal TextMeshProUGUI Stamp = null!;
        internal string? Id;
    }

    private sealed class PageList
    {
        internal ScrollRect Scroll = null!;
        internal readonly Row[] Rows = new Row[AndroidHistoryPanelView.Rows];
        internal float Height;
        internal bool ResetScroll;
    }

    // One selectable opponent on the run's battle rail.
    private sealed class Chip
    {
        internal RectTransform Rect = null!;
        internal HistoryButton Control = null!;
        internal TextMeshProUGUI Day = null!;
        internal TextMeshProUGUI Name = null!;
        internal Image Outcome = null!;
    }

    private sealed class Rail
    {
        internal ScrollRect Scroll = null!;
        internal readonly Chip[] Chips = new Chip[Rows];
        internal readonly HistoryTimelineScroll Position = new();
    }

    /// <summary>What the view's controls call; each runs inside the main-thread scope.</summary>
    public sealed record ViewActions(
        Action Close,
        Action Replay,
        Action<int> SelectRun,
        Action<int> SelectBattle,
        Action<HistorySectionMode> Section,
        Action<int> PageArchive,
        Action<int> PageBattles,
        Action More,
        Action Delete,
        Action Favorite,
        Action Skills
    );
}
