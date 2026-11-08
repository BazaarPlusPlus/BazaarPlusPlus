using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.History;

/// <summary>
/// The touch twin of the desktop's <c>HistoryPanelView</c>, a full-screen sheet laid out from
/// Core's <see cref="HistoryTouchLayout"/> for the screen it was built on and bound from
/// Core's <see cref="HistoryPanelViewModel"/>. The header
/// holds the title, supporter action, More and Close. The archive groups its tabs, filters,
/// records and pager. Beside it are the battle rail and native boards; compact screens switch
/// from archive to one board with a horizontal battle rail and opponent/you selection.
/// Lists drag-scroll with inertia and show a thumb while they overflow; each finished gesture logs
/// <c>touch.history.scrolled</c>. A section without rows says so once, centred in the detail
/// column, with no boards, titles or actions under it.
/// Hero portraits share the Collection framing; rank badges and run trophies are the game's art
/// through the Mobile Platform Port (<see cref="IMobilePlatformPort.RankBadge"/>), with text
/// while it is missing; the two BPP badge atlases ship in each host's assembly. The panel rents the native boards into
/// <see cref="UpperBoard"/> and <see cref="LowerBoard"/> and lays its overlays over
/// <see cref="Surface"/>; a new screen size builds a new view. A view that throws while it builds
/// destroys its canvas, active and kept across scenes until its last lines, before the exception
/// leaves.
/// </summary>
internal sealed partial class TouchHistoryPanelView : IDisposable
{
    public const int SortingOrder = 29000;
    private const int Rows = 40;

    private readonly TMP_FontAsset _font;
    private readonly ViewActions _actions;
    private readonly HistoryTouchLayout _l;
    private readonly GameObject _root;
    private readonly TouchHistoryBadges? _badges;
    private readonly List<ScrollTracker> _trackers = new();
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
    private readonly ISupporterRotation? _supporters;
    private IReadOnlyList<BPPSupporterSample> _headerSupporters = Array.Empty<BPPSupporterSample>();
    private TouchHistoryFilterDialog _filters = null!;
    private readonly HistoryTouchNavigation _navigation;
    private PageList _archive = null!;
    private TextMeshProUGUI _emptyDetail = null!;
    private Rail _rail = null!;
    private readonly List<ScrollThumb> _thumbs = new();
    private bool _empty;
    private bool? _railPaged;
    private string _upperMessage = string.Empty,
        _lowerMessage = string.Empty;
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

    public TouchHistoryPanelView(
        TMP_FontAsset font,
        ViewActions actions,
        ISupporterRotation? supporters,
        HistoryTouchNavigation navigation
    )
    {
        _supporters = supporters;
        _navigation = navigation;
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        var screen = TouchScreen.Key();
        _l = HistoryTouchLayout.For(TouchScreen.Read());
        _badges = TouchHistoryBadges.TryLoad();
        _root = UiKit.CreateOverlayCanvas("BPP.HistoryPanel", SortingOrder);
        try
        {
            Surface = UiKit.Rect(
                "Layout",
                _root.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero
            );
            TouchHistoryUi.RefreshSkin();
            TouchHistoryUi.UseArtwork(
                _badges?.HeaderNormal,
                _badges?.HeaderSelected,
                HeaderPixelsPerUnit
            );
            Build();
            _root.SetActive(false);
        }
        catch
        {
            UnityEngine.Object.Destroy(_root);
            _badges?.Dispose();
            throw;
        }
        var dp = _l.Metrics.UnitsPerDp;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.history.view_created"),
            ("screen", screen),
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

    public HistoryTouchLayout Layout => _l;

    public IHistoryOverlay FilterOverlay => _filters;

    /// <summary>The canvas-wide parent the panel's overlays are laid over.</summary>
    public RectTransform Surface { get; }

    /// <summary>The opponent's board with two boards, the challenger's with one.</summary>
    public RectTransform UpperBoard { get; private set; } = null!;

    public RectTransform LowerBoard { get; private set; } = null!;

    public int RootCount => _disposed ? 0 : 1;

    public void SetVisible(bool visible)
    {
        _visible = visible;
        TouchHistoryUi.SetActive(_root, visible);
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
        _upperMessage = upperMessage;
        _lowerMessage = lowerMessage;
        ApplyBoardStatus();
    }

    // An empty section's one message is the detail column's; the boards then carry none.
    private void ApplyBoardStatus()
    {
        ShowStatus(_upperStatusBand, _upperStatus, _l.UpperBoard, _empty ? "" : _upperMessage);
        ShowStatus(_lowerStatusBand, _lowerStatus, _l.LowerBoard, _empty ? "" : _lowerMessage);
        ApplyNavigation();
    }

    // A board message's band grows to the wrapped text, so a long message is never cut.
    private void ShowStatus(GameObject band, TextMeshProUGUI label, TouchRect board, string text)
    {
        label.text = text;
        TouchHistoryUi.SetActive(band, text.Length > 0);
        if (text.Length == 0)
            return;
        var lines = label.GetPreferredValues(text, board.Width * StatusTextShare, 0f).y;
        var height = Math.Min(board.Height, Math.Max(_l.Metrics.Dp(StatusMinDp), lines + _l.Gap));
        TouchScreen.Place(
            band.GetComponent<RectTransform>(),
            new TouchRect(board.Left, board.Bottom - height, board.Width, height)
        );
    }

    /// <summary>The skills action, available while the selected battle's boards are loaded.</summary>
    public void SetSkillsAvailable(bool available)
    {
        _snapshotsAvailable = available;
        _skills.Set(HistoryPanelText.Skills(), enabled: available, dimmed: !available);
        ApplyNavigation();
    }

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
        var pressed = ScrollTracker.PointerPressed();
        foreach (var tracker in _trackers)
            tracker.Tick(pressed, _l.Metrics.UnitsPerDp);
        foreach (var thumb in _thumbs)
            thumb.Tick(_l.Metrics);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        KeepScroll();
        _disposed = true;
        UnityEngine.Object.Destroy(_root);
        _badges?.Dispose();
    }

    // The backdrop hides the game around the sheet and takes the touches that land there. It
    // covers only the margins, as four bands, because the sheet paints its own opaque backing: a
    // backdrop across the whole canvas would fill every sheet pixel twice. The bands reach the
    // canvas edges through their anchors, so no rounding of the canvas size leaves a seam.
    private void BuildBackdrop()
    {
        var sheet = _l.Sheet;
        var metrics = _l.Metrics;
        if (sheet.Top > 0f)
            Band("BackdropTop", new(0f, 1f), new(1f, 1f), new(0f, -sheet.Top), Vector2.zero);
        if (metrics.CanvasHeight > sheet.Bottom)
            Band("BackdropBottom", Vector2.zero, Vector2.one, Vector2.zero, new(0f, -sheet.Bottom));
        if (sheet.Left > 0f)
            Band(
                "BackdropLeft",
                new(0f, 1f),
                new(0f, 1f),
                new(0f, -sheet.Bottom),
                new(sheet.Left, -sheet.Top)
            );
        if (metrics.CanvasWidth > sheet.Right)
            Band(
                "BackdropRight",
                new(0f, 1f),
                new(1f, 1f),
                new(sheet.Right, -sheet.Bottom),
                new(0f, -sheet.Top)
            );

        void Band(
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax
        ) =>
            UiKit.Fill(
                UiKit.Rect(name, Surface, anchorMin, anchorMax, offsetMin, offsetMax),
                TouchHistoryUi.Backdrop
            );
    }

    private void Build()
    {
        BuildBackdrop();
        TouchHistoryUi.Skin.Frame(
            TouchScreen.Child("Sheet", Surface, _l.Sheet),
            TouchHistoryUi.Backdrop,
            borderOnly: true
        );
        var title = TouchHistoryUi.Label(Surface, _font, "Title", _l.Title, _l.TitleFont);
        _titleObject = title.gameObject;
        title.text = HistoryPanelText.Title();
        var archiveFill = UiKit.Fill(
            TouchScreen.Child("ArchivePanel", Surface, _l.Archive),
            InGameTheme.Panel,
            false
        );
        var tray = TouchHistoryUi.ControlSurface(
            TouchScreen.Child("SectionTabs", Surface, _l.TabsTray)
        );
        var tab = _l.ButtonFont;
        _runsTab = TouchHistoryUi
            .Button(
                Surface,
                _font,
                "tab_runs",
                _l.RunsTab,
                tab,
                () => SelectSection(HistorySectionMode.Runs)
            )
            .WithHeaderArt(_badges?.HeaderNormal, _badges?.HeaderSelected, HeaderPixelsPerUnit)
            .As(HistoryButtonStyle.Tab);
        _ghostTab = TouchHistoryUi
            .Button(
                Surface,
                _font,
                "tab_ghost",
                _l.GhostTab,
                tab,
                () => SelectSection(HistorySectionMode.Ghost)
            )
            .WithHeaderArt(_badges?.HeaderNormal, _badges?.HeaderSelected, HeaderPixelsPerUnit)
            .As(HistoryButtonStyle.Tab);
        _favoritesTab = TouchHistoryUi
            .Button(
                Surface,
                _font,
                "tab_favorites",
                _l.FavoritesTab,
                tab,
                () => SelectSection(HistorySectionMode.Favorites)
            )
            .WithHeaderArt(_badges?.HeaderNormal, _badges?.HeaderSelected, HeaderPixelsPerUnit)
            .As(HistoryButtonStyle.Tab);
        BuildSupporter();
        _filter = HeaderButton("filters", _l.ArchiveFilter, () => _filters.Open());
        _filter.UseTrailingChevron(_l.Metrics.Dp(10f));
        _more = HeaderButton("more", _l.More, () => _actions.More());
        _more.Set(HistoryPanelText.More());
        HeaderButton("close", _l.Close, () => _actions.Close()).Set(HistoryPanelText.Close());

        _archive = BuildArchive();
        _thumbs.Add(new ScrollThumb(_archive.Scroll, _l.Metrics));
        _pageLabel = TouchHistoryUi.Label(
            Surface,
            _font,
            "PageLabel",
            _l.PageLabel,
            _l.BodyFont,
            TextAlignmentOptions.MidlineLeft,
            TouchHistoryUi.Muted,
            wrap: true
        );
        _newer = TouchHistoryUi.Button(
            Surface,
            _font,
            "archive_newer",
            _l.PagerButton(0),
            _l.ButtonFont,
            () => NavigateArchive(-1)
        );
        _newer.Set(HistoryPanelText.Newer());
        _newer.UseChevron(180f, _l.Metrics.Dp(18f));
        _older = TouchHistoryUi.Button(
            Surface,
            _font,
            "archive_older",
            _l.PagerButton(2),
            _l.ButtonFont,
            () => NavigateArchive(1)
        );
        _older.Set(HistoryPanelText.Older());
        _older.UseChevron(0f, _l.Metrics.Dp(18f));
        _latest = TouchHistoryUi.Button(
            Surface,
            _font,
            "archive_latest",
            _l.PagerButton(1),
            _l.ButtonFont,
            () => NavigateArchive(0)
        );
        _latest.Set(HistoryPanelText.Latest());

        _railNewer = TouchHistoryUi.Button(
            Surface,
            _font,
            "timeline_newer",
            _l.RailNewer,
            _l.ButtonFont,
            () => _actions.PageBattles(-1)
        );
        _railNewer.UseChevron(_l.Compact ? 180f : 90f, _l.Metrics.Dp(18f));
        _railOlder = TouchHistoryUi.Button(
            Surface,
            _font,
            "timeline_older",
            _l.RailOlder,
            _l.ButtonFont,
            () => _actions.PageBattles(1)
        );
        _railOlder.UseChevron(_l.Compact ? 0f : -90f, _l.Metrics.Dp(18f));
        _rail = BuildRail();
        if (!_l.Compact)
            _thumbs.Add(new ScrollThumb(_rail.Scroll, _l.Metrics));

        _upperTitle = TouchHistoryUi.Label(
            Surface,
            _font,
            "UpperTitle",
            _l.UpperBoardTitle,
            _l.BodyFont,
            TextAlignmentOptions.Center,
            TouchHistoryUi.Muted
        );
        UpperBoard = BoardRegion("UpperBoard", _l.UpperBoard);
        (_upperStatusBand, _upperStatus) = StatusBand("UpperStatus", _l.UpperBoard);
        _lowerTitle = TouchHistoryUi.Label(
            Surface,
            _font,
            "LowerTitle",
            _l.LowerBoardTitle,
            _l.BodyFont,
            TextAlignmentOptions.Center,
            TouchHistoryUi.Muted
        );
        _lowerTitleObject = _lowerTitle.gameObject;
        LowerBoard = BoardRegion("LowerBoard", _l.LowerBoard);
        _lowerBoardObject = LowerBoard.gameObject;
        (_lowerStatusBand, _lowerStatus) = StatusBand("LowerStatus", _l.LowerBoard);
        _emptyDetail = TouchHistoryUi.Label(
            Surface,
            _font,
            "EmptyDetail",
            new TouchRect(
                _l.Detail.Left,
                _l.Detail.Top,
                _l.Detail.Width,
                Math.Max(0f, _l.Footer.Top - _l.Gap - _l.Detail.Top)
            ),
            _l.ButtonFont,
            TextAlignmentOptions.Center,
            TouchHistoryUi.Muted,
            wrap: true
        );

        _delete = TouchHistoryUi
            .Button(Surface, _font, "delete", _l.Delete, _l.ButtonFont, () => _actions.Delete())
            .As(HistoryButtonStyle.Danger);
        _favorite = TouchHistoryUi.Button(
            Surface,
            _font,
            "favorite",
            _l.Favorite,
            _l.ButtonFont,
            () => _actions.Favorite()
        );
        _skills = TouchHistoryUi.Button(
            Surface,
            _font,
            "skills",
            _l.Skills,
            _l.ButtonFont,
            () => _actions.Skills()
        );
        SetSkillsAvailable(false);
        _replay = TouchHistoryUi.Button(
            Surface,
            _font,
            "replay",
            _l.Replay,
            _l.RowTitleFont,
            () => _actions.Replay()
        );
        _status = TouchHistoryUi.Label(
            Surface,
            _font,
            "Status",
            _l.Status,
            _l.BodyFont,
            color: TouchHistoryUi.Muted
        );
        BuildNavigation(archiveFill.gameObject, tray.gameObject);
        SetBoardStatus(string.Empty, string.Empty);
        _filters = new TouchHistoryFilterDialog(Surface, _font, _l, _actions);
    }

    private float HeaderPixelsPerUnit =>
        HistoryHeaderArtwork.BorderPixels / _l.Metrics.Dp(HistoryHeaderArtwork.ButtonBorder);

    private HistoryButton HeaderButton(string name, TouchRect area, Action action) =>
        TouchHistoryUi
            .Button(Surface, _font, name, area, _l.ButtonFont, action)
            .WithHeaderArt(_badges?.HeaderNormal, _badges?.HeaderSelected, HeaderPixelsPerUnit);

    private PageList BuildArchive()
    {
        var (scroll, content) = TouchHistoryUi.Scroll(
            Surface,
            "Archive",
            _l.ArchiveList,
            horizontal: false
        );
        _trackers.Add(new ScrollTracker(Scrolled(), "archive", scroll));
        var list = new PageList { Scroll = scroll, Height = _l.RunRowHeight };
        for (var i = 0; i < Rows; i++)
        {
            var index = i;
            var rect = TouchScreen.Child("Row", content, _l.ArchiveRow(i, runs: true));
            var control = TouchHistoryUi
                .Button(
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
                        _navigation.OpenDetail();
                        UpdateContent();
                    }
                )
                .As(HistoryButtonStyle.Record);
            // Every row reads as name, meta and stamp lines, so the button's own label stays off.
            TouchHistoryUi.SetActive(control.Label.gameObject, false);
            var row = new Row
            {
                Rect = rect,
                Control = control,
                Name = TouchHistoryUi.Fraction(
                    rect,
                    _font,
                    .26f,
                    .03f,
                    .33f,
                    .36f,
                    _l.RowTitleFont
                ),
                Meta = TouchHistoryUi.Fraction(
                    rect,
                    _font,
                    .26f,
                    .38f,
                    .33f,
                    .3f,
                    _l.BodyFont,
                    color: TouchHistoryUi.Muted
                ),
                Stamp = TouchHistoryUi.Fraction(
                    rect,
                    _font,
                    .04f,
                    .66f,
                    .68f,
                    .3f,
                    _l.BodyFont,
                    color: TouchHistoryUi.Muted
                ),
                Rating = TouchHistoryUi.Fraction(
                    rect,
                    _font,
                    0f,
                    0f,
                    0f,
                    0f,
                    _l.BodyFont,
                    TextAlignmentOptions.Center
                ),
            };
            var rankImage = ArtSlot(rect, "Rank");
            row.Rank = TouchHistoryUi.Fraction(
                rankImage.transform,
                _font,
                -.2f,
                0f,
                1.4f,
                1f,
                _l.BodyFont,
                TextAlignmentOptions.Center
            );
            row.RankArt = new ArtImage(
                rankImage,
                shown => TouchHistoryUi.SetActive(row.Rank.gameObject, shown)
            );
            var outcomeImage = ArtSlot(rect, "Outcome");
            var portrait = UiKit.Fixed(
                "HeroPortrait",
                rect,
                new(0f, 1f),
                Vector2.zero,
                Vector2.one * _l.Metrics.Dp(36f)
            );
            row.PortraitRect = portrait;
            row.Portrait = new PortraitView(portrait, TouchHistoryUi.Skin, _l.Metrics);
            row.Unknown = TouchHistoryUi.Fraction(
                outcomeImage.transform,
                _font,
                0f,
                0f,
                1f,
                1f,
                _l.BodyFont,
                TextAlignmentOptions.Center,
                TouchHistoryUi.Muted
            );
            row.Badge = new ArtImage(
                outcomeImage,
                shown => TouchHistoryUi.SetActive(row.Unknown.gameObject, shown)
            );
            rect.gameObject.SetActive(false);
            list.Rows[i] = row;
        }
        return list;
    }

    private Rail BuildRail()
    {
        var (scroll, content) = TouchHistoryUi.Scroll(
            Surface,
            "Rail",
            _l.RailList,
            horizontal: _l.Compact
        );
        _trackers.Add(new ScrollTracker(Scrolled(), "rail", scroll));
        var rail = new Rail { Scroll = scroll };
        for (var i = 0; i < Rows; i++)
        {
            var slot = i;
            var rect = TouchScreen.Child("Chip", content, _l.RailChip(i));
            var control = TouchHistoryUi
                .Button(
                    rect,
                    _font,
                    "timeline_chip",
                    _l.BodyFont,
                    () => _actions.SelectBattle(slot)
                )
                .As(HistoryButtonStyle.Record);
            control.Label.gameObject.SetActive(false);
            var outcome = UiKit.Rect(
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
                Day = TouchHistoryUi.Fraction(
                    rect,
                    _font,
                    .37f,
                    .04f,
                    .37f,
                    .42f,
                    _l.BodyFont,
                    TextAlignmentOptions.Center
                ),
                Rank = new ArtImage(ArtSlot(rect, "Rank", .74f, .06f, .23f, .38f)),
                Name = TouchHistoryUi.Fraction(
                    rect,
                    _font,
                    .04f,
                    .44f,
                    .92f,
                    .42f,
                    _l.BodyFont,
                    TextAlignmentOptions.Center,
                    TouchHistoryUi.Muted
                ),
                Outcome = UiKit.Fill(outcome, Color.clear, blocksTouches: false),
            };
            // The top line reads portrait, day and rank badge, as the desktop chip does.
            var portrait = UiKit.Fixed(
                "HeroPortrait",
                rect,
                new(0f, 1f),
                new(_l.Metrics.Dp(5f), -_l.Metrics.Dp(3f)),
                Vector2.one * _l.Metrics.Dp(22f)
            );
            rail.Chips[i].Portrait = new PortraitView(portrait, TouchHistoryUi.Skin, _l.Metrics);
            if (_l.Compact)
            {
                portrait.gameObject.SetActive(false);
                rail.Chips[i].Rank.Image.gameObject.SetActive(false);
                rail.Chips[i].Name.gameObject.SetActive(false);
                Frac(rail.Chips[i].Day.rectTransform, .04f, .05f, .92f, .8f);
            }
            rect.gameObject.SetActive(false);
        }
        return rail;
    }

    private RectTransform BoardRegion(string name, TouchRect area)
    {
        var region = TouchScreen.Child(name, Surface, area);
        UiKit.Fill(region, TouchHistoryUi.ButtonDimmed, blocksTouches: false);
        return region;
    }

    // A shaded band along the board's bottom edge: a later sibling, so it draws over the cards.
    private const float StatusMinDp = 44f;
    private const float StatusTextShare = .96f;

    private (GameObject Band, TextMeshProUGUI Label) StatusBand(string name, TouchRect board)
    {
        var height = Math.Min(board.Height, _l.Metrics.Dp(StatusMinDp));
        var band = TouchScreen.Child(
            name,
            Surface,
            new TouchRect(board.Left, board.Bottom - height, board.Width, height)
        );
        UiKit.Fill(band, TouchHistoryUi.Shade, blocksTouches: false);
        var label = TouchHistoryUi.Fraction(
            band,
            _font,
            (1f - StatusTextShare) / 2f,
            0f,
            StatusTextShare,
            1f,
            _l.BodyFont,
            TextAlignmentOptions.Center,
            TouchHistoryUi.Ink,
            wrap: true
        );
        return (band.gameObject, label);
    }

    // Rows place their images as they bind: where they sit depends on the row mode and rating.
    private static Image ArtSlot(
        RectTransform parent,
        string name,
        float x = 0f,
        float y = 0f,
        float width = 0f,
        float height = 0f
    )
    {
        var rect = UiKit.Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        Frac(rect, x, y, width, height);
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
            KeepScroll();
            _boundSection = m.SectionMode;
            _restoreArchiveScroll = true;
            _archive.ResetScroll = false;
            ApplyRowMode(runs);
            SyncTrackers();
        }
        var noRows = m.ArchiveRows.Count == 0;
        // A run that recorded no battle has nothing for the boards either; it can still be deleted.
        _empty = m.ArchiveEmptyMessage.Length > 0;
        _emptyDetail.text = m.ArchiveEmptyMessage;
        TouchHistoryUi.SetActive(_emptyDetail.gameObject, _empty);
        _runsTab.Set(HistoryPanelText.RunsTab(), runs);
        _ghostTab.Set(HistoryPanelText.GhostTab(), ghost);
        TouchHistoryUi.SetActive(_favoritesTab.GameObject, m.FavoritesTabVisible);
        _favoritesTab.Set(HistoryPanelText.FavoritesTab(), favorites);
        _filters.Bind(m);
        _filter.Set(
            (
                runs
                    ? string.IsNullOrEmpty(m.SelectedRunHero)
                        ? HistoryPanelText.AllHeroes()
                        : HeroLabel(m.SelectedRunHero)
                    : HistoryPanelText.Filters()
            )
        );
        TouchScreen.Place(
            _archive.Scroll.GetComponent<RectTransform>(),
            favorites ? _l.ArchiveListWithoutFilter : _l.ArchiveList
        );

        // One page of battles needs no rail pager; the list then takes the whole rail.
        var railPaged = m.BattleHasNewer || m.BattleHasOlder;
        if (_railPaged != railPaged)
        {
            _railPaged = railPaged;
            TouchScreen.Place(
                _rail.Scroll.GetComponent<RectTransform>(),
                railPaged ? _l.RailList : _l.RailListFull
            );
        }
        TouchHistoryUi.SetActive(_rail.Scroll.gameObject, runs);
        TouchHistoryUi.SetActive(_railNewer.GameObject, runs && railPaged);
        TouchHistoryUi.SetActive(_railOlder.GameObject, runs && railPaged);
        _railNewer.Set(HistoryPanelText.Newer(), enabled: m.BattleHasNewer);
        _railOlder.Set(HistoryPanelText.Older(), enabled: m.BattleHasOlder);
        _newer.Set(HistoryPanelText.Newer(), enabled: m.HasNewer && !m.PageLoading);
        _older.Set(HistoryPanelText.Older(), enabled: m.HasOlder && !m.PageLoading);
        _latest.Set(HistoryPanelText.Latest(), enabled: !m.PageLoading);
        _pageLabel.text = m.PageLabel.Split('\n')[0];

        BindRows(m.ArchiveRows, runs ? m.SelectedRunIndex : m.SelectedBattleIndex, runs);
        if (runs)
            BindRail(m.Timeline, m.SelectedBattleIndex, m.PageLoading);

        // Two boards show yours below the opponent's; one board is the challenger's (ADR-0002).
        // Each title names its side: the opponent by name, yours with the battle's outcome.
        var titles = m.BoardTitles;
        _upperTitle.text = titles.Upper;
        _lowerTitle.text =
            titles.OutcomeText.Length == 0
                ? titles.Lower
                : $"{titles.Lower} · {Tinted(titles.OutcomeText, OutcomeColor(titles.Outcome))}";
        TouchHistoryUi.SetActive(_upperTitle.gameObject, !_empty);
        TouchHistoryUi.SetActive(_lowerTitleObject, both && !_empty);
        TouchHistoryUi.SetActive(_lowerBoardObject, both);
        if (!both)
            TouchHistoryUi.SetActive(_lowerStatusBand, false);
        ApplyBoardStatus();

        // An empty section offers no action: there is nothing to act on.
        TouchHistoryUi.SetActive(_delete.GameObject, m.DeleteButtonVisible && !noRows);
        // Dimmed but tappable: the tap puts the reason on the status line.
        _delete.Set(
            m.DeleteButtonText,
            enabled: !m.PageLoading && !m.DeleteButtonBusy,
            dimmed: !m.DeleteButtonEnabled
        );
        TouchHistoryUi.SetActive(_favorite.GameObject, m.FavoriteButtonVisible && !_empty);
        _favorite.Set(m.FavoriteButtonText, enabled: m.FavoriteButtonEnabled && !m.PageLoading);
        TouchHistoryUi.SetActive(_skills.GameObject, !_empty);
        TouchHistoryUi.SetActive(_replay.GameObject, !_empty);
        // The button names its action; unavailable reads as dimmed, and the tap explains.
        _replay.Set(
            m.ReplayButtonText,
            true,
            enabled: !m.PageLoading && !m.ReplayButtonBusy,
            dimmed: !m.ReplayButtonEnabled
        );
        _status.text = m.StatusMessage ?? string.Empty;
        _status.color = TouchHistoryUi.StatusColor(m.StatusSeverity);
        ApplyNavigation();
    }

    // Archive rows carry runs in Runs and battles in Ghost and Favorites.
    private void ApplyRowMode(bool runs)
    {
        _archive.Height = runs ? _l.RunRowHeight : _l.BattleRowHeight;
        for (var i = 0; i < Rows; i++)
        {
            var row = _archive.Rows[i];
            TouchScreen.Place(row.Rect, _l.ArchiveRow(i, runs));
        }
    }

    private void BindRows(IReadOnlyList<HistoryArchiveRow> rows, int selected, bool runs)
    {
        var list = _archive;
        var offset = Mathf.Max(0, list.Scroll.content.anchoredPosition.y);
        var oldTop = Math.Min(Rows - 1, (int)(offset / list.Height));
        var topId = list.Rows[oldTop].Id;
        var newTop = -1;
        for (var i = 0; i < Rows; i++)
        {
            var row = list.Rows[i];
            var shown = i < rows.Count;
            TouchHistoryUi.SetActive(row.Rect.gameObject, shown);
            if (!shown)
            {
                row.Id = null;
                continue;
            }
            var fields = rows[i];
            row.Id = fields.Id;
            if (row.Id == topId)
                newTop = i;
            row.Control.Set(string.Empty, i == selected, !_model!.PageLoading);
            var portraitHero = ParseHero(fields.HeroId);
            if (row.Hero != portraitHero)
            {
                row.Hero = portraitHero;
                row.Portrait.Bind(
                    portraitHero is { } value
                        ? () => MobilePlatform.Port.HeroPortrait(value)
                        : null,
                    portraitHero
                );
            }
            row.Name.text = fields.Name;
            row.Meta.text = fields.Meta;
            row.Stamp.text = fields.Stamp;
            row.Unknown.text = fields.BadgeText;
            row.Badge.Bind(BadgeSprite(fields.Badge));
            row.Rank.text = fields.RankText;
            row.RankArt.Bind(MobilePlatform.Port.RankBadge(fields.Rank));
            row.Rating.text = RatingText(fields);
            if (runs)
                PlaceRunRow(row, row.Rating.text.Length > 0);
            else
                PlaceBattleRow(row, fields.Meta.Length > 0, row.Rating.text.Length > 0);
        }
        list.Scroll.content.sizeDelta = new Vector2(_l.ArchiveList.Width, rows.Count * list.Height);
        var nextOffset =
            _restoreArchiveScroll
                ? _navigation.ArchiveScrollDp[(int)_model!.SectionMode] * _l.Metrics.UnitsPerDp
            : list.ResetScroll ? 0
            : newTop >= 0 ? newTop * list.Height + offset % list.Height
            : Math.Max(0, selected) * list.Height;
        list.Scroll.StopMovement();
        list.Scroll.content.anchoredPosition = new Vector2(
            0,
            Mathf.Clamp(
                nextOffset,
                0,
                Mathf.Max(0, rows.Count * list.Height - list.Scroll.viewport.rect.height)
            )
        );
        list.ResetScroll = false;
        _restoreArchiveScroll = false;
        SyncTrackers();
    }

    private void BindRail(IReadOnlyList<HistoryTimelineItem> battles, int selected, bool loading)
    {
        var rail = _rail;
        for (var i = 0; i < Rows; i++)
        {
            var chip = rail.Chips[i];
            var shown = i < battles.Count;
            TouchHistoryUi.SetActive(chip.Rect.gameObject, shown);
            if (!shown)
                continue;
            // The page's newest-first order, matching the archive and the pager.
            var battle = battles[i];
            chip.Control.Set(string.Empty, i == selected, !loading);
            // The chip is too narrow for the outcome word; the bar carries it, and the selected
            // battle's board title says it.
            chip.Day.text = battle.DayText;
            chip.Rank.Bind(MobilePlatform.Port.RankBadge(battle.Rank));
            var hero = ParseHero(battle.HeroId);
            if (chip.Hero != hero)
            {
                chip.Hero = hero;
                chip.Portrait.Bind(
                    hero is { } value ? () => MobilePlatform.Port.HeroPortrait(value) : null,
                    hero
                );
            }
            chip.Name.text = battle.OpponentName;
            chip.Outcome.color =
                battle.Outcome == HistoryOutcomeTone.None
                    ? Color.clear
                    : OutcomeColor(battle.Outcome);
        }
        var horizontal = _l.Compact;
        var step = horizontal ? _l.ChipWidth : _l.ChipHeight;
        rail.Scroll.content.sizeDelta = horizontal
            ? new Vector2(battles.Count * step, _l.Rail.Height)
            : new Vector2(_l.RailList.Width, battles.Count * step);
        var hasSelection = selected >= 0 && selected < battles.Count;
        var position = rail.Scroll.content.anchoredPosition;
        var viewport = _railPaged == false ? _l.RailListFull : _l.RailList;
        var savedOffset = _navigation.BattleScrollSlots;
        var offset = _navigation.BattlePosition.Bind(
            hasSelection ? battles[selected].Id : null,
            hasSelection ? selected : -1,
            battles.Count,
            savedOffset is { } slots ? slots * step
                : horizontal ? -position.x
                : position.y,
            horizontal ? viewport.Width : viewport.Height,
            step
        );
        _navigation.BattleScrollSlots = null;
        var next = horizontal ? new Vector2(-offset, 0) : new Vector2(0, offset);
        if (position != next)
        {
            rail.Scroll.StopMovement();
            rail.Scroll.content.anchoredPosition = next;
        }
        SyncTrackers();
    }

    // The rank badge stands left of the outcome, as on the desktop, with a Legendary rating under
    // it. A run row is four columns: the portrait, the hero over its mode and day over its stamp,
    // then the rank badge and the trophy at the row's full height.
    private void PlaceRunRow(Row row, bool rated)
    {
        PlacePortrait(row, new Vector2(0f, .5f), 0f);
        Frac(row.Name.rectTransform, .26f, .03f, .34f, .36f);
        Frac(row.Meta.rectTransform, .26f, .38f, .34f, .3f);
        Frac(row.Stamp.rectTransform, .26f, .66f, .34f, .3f);
        Frac(row.RankArt.Image.rectTransform, .61f, rated ? .06f : .18f, .16f, .64f);
        Frac(row.Rating.rectTransform, .55f, .66f, .28f, .3f);
        Frac(row.Badge.Image.rectTransform, .79f, .12f, .18f, .76f);
    }

    // A battle row keeps name, hero with day, and stamp lines; both badges sit at its top, and
    // the stamp runs under them whenever the rating is empty, which keeps
    // "Favorited <date> <time>" whole on a phone. Without a meta line the name drops to sit
    // between the row's top and its stamp.
    private void PlaceBattleRow(Row row, bool hasMeta, bool rated)
    {
        PlacePortrait(row, new Vector2(0f, 1f), -_l.Metrics.Dp(6f));
        Frac(row.Name.rectTransform, .26f, hasMeta ? .03f : .17f, .33f, .36f);
        Frac(row.Meta.rectTransform, .26f, .38f, .33f, .3f);
        Frac(row.Stamp.rectTransform, .04f, .66f, rated ? .54f : .92f, .3f);
        Frac(row.RankArt.Image.rectTransform, .61f, .05f, .16f, .52f);
        Frac(row.Rating.rectTransform, .55f, .58f, .28f, .34f);
        Frac(row.Badge.Image.rectTransform, .79f, .05f, .18f, .52f);
    }

    private void PlacePortrait(Row row, Vector2 anchor, float y)
    {
        var portrait = row.PortraitRect;
        portrait.anchorMin = portrait.anchorMax = portrait.pivot = anchor;
        portrait.anchoredPosition = new Vector2(_l.Metrics.Dp(8f), y);
    }

    // A finished run shows the trophy its tier earned; unfinished runs keep the BPP state badges.
    private Task<Sprite?>? BadgeSprite(HistoryRowBadge badge) =>
        badge switch
        {
            HistoryRowBadge.Bronze => MobilePlatform.Port.RunTrophy(RunOutcomeTier.Bronze),
            HistoryRowBadge.Silver => MobilePlatform.Port.RunTrophy(RunOutcomeTier.Silver),
            HistoryRowBadge.Gold => MobilePlatform.Port.RunTrophy(RunOutcomeTier.Gold),
            HistoryRowBadge.Diamond => MobilePlatform.Port.RunTrophy(RunOutcomeTier.Diamond),
            HistoryRowBadge.BattleWon => Task.FromResult(_badges?.Win),
            HistoryRowBadge.BattleLost => Task.FromResult(_badges?.Loss),
            HistoryRowBadge.StateActive => Task.FromResult(_badges?.RunState(2)),
            HistoryRowBadge.StateAbandoned => Task.FromResult(_badges?.RunState(1)),
            HistoryRowBadge.StateCompleted => Task.FromResult(_badges?.RunState(0)),
            _ => null,
        };

    // A Legendary rank carries its rating, and a run its change in the outcome colours.
    private static string RatingText(HistoryArchiveRow row) =>
        row.RatingDelta.Length == 0 ? row.Rating
        : row.RatingTone == HistoryOutcomeTone.None ? $"{row.Rating} {row.RatingDelta}"
        : $"{row.Rating} {Tinted(row.RatingDelta, OutcomeColor(row.RatingTone))}";

    private static Color OutcomeColor(HistoryOutcomeTone tone) =>
        tone switch
        {
            HistoryOutcomeTone.Won => TouchHistoryUi.Win,
            HistoryOutcomeTone.Lost => TouchHistoryUi.Loss,
            _ => TouchHistoryUi.Muted,
        };

    /// <summary>A stored hero id under the game's own hero name.</summary>
    private static CardHero? ParseHero(string? hero) =>
        TheDragonsHeroId.IsAlias(hero) ? CardHero.TheDragons
        : Enum.TryParse<CardHero>(hero, true, out var parsed) ? parsed
        : null;

    private static string Tinted(string text, Color color) =>
        $"<color=#{(int)(color.r * 255f):X2}{(int)(color.g * 255f):X2}{(int)(color.b * 255f):X2}>{text}</color>";

    public static string HeroLabel(string? hero)
    {
        if (TheDragonsHeroId.IsAlias(hero))
            return MobilePlatform.Port.HeroName(CardHero.TheDragons);
        if (!string.IsNullOrWhiteSpace(hero) && Enum.TryParse<CardHero>(hero, true, out var parsed))
            return MobilePlatform.Port.HeroName(parsed);
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
        new(BppLogFeatureScope.Touch, "touch.history.scrolled");

    private void SyncTrackers()
    {
        foreach (var tracker in _trackers)
            tracker.Sync();
    }

    private sealed class Row
    {
        internal RectTransform Rect = null!;
        internal HistoryButton Control = null!;
        internal ArtImage Badge = null!;
        internal ArtImage RankArt = null!;
        internal RectTransform PortraitRect = null!;
        internal PortraitView Portrait = null!;
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
        internal readonly Row[] Rows = new Row[TouchHistoryPanelView.Rows];
        internal float Height;
        internal bool ResetScroll;
    }

    // One selectable opponent on the run's battle rail.
    private sealed class Chip
    {
        internal RectTransform Rect = null!;
        internal HistoryButton Control = null!;
        internal TextMeshProUGUI Day = null!;
        internal PortraitView Portrait = null!;
        internal CardHero? Hero;
        internal ArtImage Rank = null!;
        internal TextMeshProUGUI Name = null!;
        internal Image Outcome = null!;
    }

    // A thin bar in the gutter right of a list while its rows overflow, so a list that scrolls
    // looks it. The bar is written only when its visibility or placement changed, so a list at
    // rest marks nothing on the view's shared canvas dirty. A view build makes new thumbs, so a
    // relayout starts from an empty cache.
    private sealed class ScrollThumb
    {
        private readonly ScrollRect _scroll;
        private readonly RectTransform _scrollRect;
        private readonly RectTransform _bar;
        private bool _shown;
        private TouchRect? _placed;

        internal ScrollThumb(ScrollRect scroll, TouchMetrics metrics)
        {
            _scroll = scroll;
            _scrollRect = scroll.GetComponent<RectTransform>();
            _bar = UiKit.Rect(
                "Thumb",
                scroll.transform,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero
            );
            var fill = UiKit.Fill(_bar, TouchHistoryUi.Accent, blocksTouches: false);
            fill.color = new Color(fill.color.r, fill.color.g, fill.color.b, .7f);
            _bar.gameObject.SetActive(false);
        }

        internal void Tick(TouchMetrics metrics)
        {
            var view = _scroll.viewport.rect.height;
            var content = _scroll.content.sizeDelta.y;
            var show = _scroll.gameObject.activeInHierarchy && content > view + 1f;
            if (show != _shown)
            {
                _shown = show;
                _bar.gameObject.SetActive(show);
            }
            if (!show)
                return;
            var height = Mathf.Max(metrics.Dp(20f), view * view / content);
            var progress = Mathf.Clamp01(_scroll.content.anchoredPosition.y / (content - view));
            var placed = new TouchRect(
                _scrollRect.rect.width + metrics.Dp(2f),
                (view - height) * progress,
                metrics.Dp(3f),
                height
            );
            if (placed == _placed)
                return;
            _placed = placed;
            TouchScreen.Place(_bar, placed);
        }
    }

    private sealed class Rail
    {
        internal ScrollRect Scroll = null!;
        internal readonly Chip[] Chips = new Chip[Rows];
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
        Action Skills,
        Action<string> HeroFilter,
        Action<GhostBattleFilter> OutcomeFilter,
        Action ToggleDayFilter
    );
}
