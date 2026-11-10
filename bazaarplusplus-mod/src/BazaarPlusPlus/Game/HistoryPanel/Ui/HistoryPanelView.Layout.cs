#nullable enable
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.GameInterop.Ranks;
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    // A pooled archive row and the record it last showed.
    private sealed class RowSlot
    {
        internal KitRunRow Row = null!;
        internal string? Hero;
        internal bool Bound;
    }

    // A pooled day of the selected run's strip.
    private sealed class DaySlot
    {
        internal KitDay Day = null!;
        internal string? Hero;
        internal bool Bound;
    }

    private readonly RowSlot[] _rows = new RowSlot[Slots];
    private readonly DaySlot[] _days = new DaySlot[Slots];
    private readonly List<KitButton> _shownRows = new();
    private ScrollRect _list = null!;
    private KitDayStrip _dayStrip = null!;
    private GameObject _dayLine = null!;
    private TextMeshProUGUI _title = null!,
        _pageLabel = null!,
        _emptyLabel = null!,
        _statusLabel = null!,
        _opponentOwner = null!,
        _opponentCaption = null!,
        _playerOwner = null!;
    private Image _opponentRank = null!;
    private KitButton[] _tabs = Array.Empty<KitButton>();
    private KitButton[] _outcomes = Array.Empty<KitButton>();
    private KitButton _heroSelect = null!,
        _dayFilter = null!,
        _newer = null!,
        _older = null!,
        _latest = null!,
        _battleOlder = null!,
        _battleNewer = null!;
    private GameObject _ghostFilters = null!,
        _pager = null!,
        _opponentHead = null!,
        _playerHead = null!;
    private HistorySectionMode? _boundSection;
    private string? _revealedDay;
    private int _revealFrames;

    private static readonly HistorySectionMode[] Sections =
    [
        HistorySectionMode.Runs,
        HistorySectionMode.Ghost,
        HistorySectionMode.Favorites,
    ];

    private static readonly GhostBattleFilter[] Outcomes =
    [
        GhostBattleFilter.All,
        GhostBattleFilter.IWon,
        GhostBattleFilter.ILost,
    ];

    private void CreateLayout()
    {
        var kit = _kit!;
        _layout = DesktopKit.Fill(DesktopKit.Rect(_root!.transform, "Layout"));
        _layout.gameObject.AddComponent<Image>().color = InGameTheme.Backdrop;
        var columns = Line(_layout, 0);
        columns.childForceExpandHeight = true;
        BuildRail(kit);
        BuildDetail(kit);
        CreateMoreDialog(kit);
        CreateNoteDialog(kit);
        CreateHeroMenu(kit);
    }

    private void BuildRail(DesktopKit kit)
    {
        var rail = DesktopKit.Rect(_layout!, "Rail");
        DesktopKit.Size(rail, RailWidth, -1);
        kit.Surface(rail, KitSurface.Ground);
        Column(rail, InGameSizes.Space12).padding = new RectOffset(22, 22, 24, 24);

        var head = Line(rail, "Head", InGameSizes.Space8);
        _title = Flexible(kit.Text(head.transform, string.Empty, KitTextRole.Title));
        kit.Button(
            head.transform,
            HistoryPanelText.More(),
            KitButtonKind.Secondary,
            KitSize.Sm,
            () => SetMoreVisible(true)
        );
        kit.IconButton(head.transform, KitIcon.Close, KitButtonKind.Secondary, KitSize.Sm, _close);

        _supporters = new BPPSupporterNativeAttributionRow(rail, kit);

        _tabs = kit.Segmented(
            rail,
            [
                HistoryPanelText.RunsTab(),
                HistoryPanelText.GhostTab(),
                HistoryPanelText.FavoritesTab(),
            ],
            KitSize.Md,
            index => _section(Sections[index])
        ).Options;

        _heroSelect = kit.Select(rail, string.Empty, OpenHeroMenu);
        var ghost = DesktopKit.Rect(rail, "GhostFilters");
        Column(ghost, InGameSizes.Space8);
        _outcomes = kit.Segmented(
            ghost,
            Outcomes.Select(HistoryPanelText.OutcomeFilter).ToArray(),
            KitSize.Sm,
            index => _ghostFilter(Outcomes[index])
        ).Options;
        _dayFilter = kit.Checkbox(ghost, HistoryPanelText.FilterDayMin10(), _ghostDay);
        _ghostFilters = ghost.gameObject;

        _list = kit.ScrollList(rail, out var content);
        _list.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        for (var i = 0; i < Slots; i++)
        {
            var index = i;
            var row = kit.RunRow(
                content,
                RowHeight,
                string.Empty,
                string.Empty,
                () =>
                {
                    if (_model?.SectionMode == HistorySectionMode.Runs)
                        _selectRun(index);
                    else
                        _selectBattle(index);
                }
            );
            row.Button.gameObject.SetActive(false);
            _rows[i] = new RowSlot { Row = row };
        }

        // The list is newest first, so the left arrow goes to newer runs.
        var pager = Line(rail, "Pager", InGameSizes.Space4);
        pager.padding = new RectOffset(0, -(int)DesktopKit.LabelInset(KitSize.Sm), 0, 0);
        _newer = kit.PageArrow(pager.transform, false, () => NavigateArchive(-1));
        _older = kit.PageArrow(pager.transform, true, () => NavigateArchive(1));
        DesktopKit.Size(DesktopKit.Rect(pager.transform, "Gap"), InGameSizes.Space4, -1);
        _pageLabel = Flexible(kit.Text(pager.transform, string.Empty, KitTextRole.Caption));
        _latest = kit.Button(
            pager.transform,
            HistoryPanelText.Latest(),
            KitButtonKind.Quiet,
            KitSize.Sm,
            () => NavigateArchive(0)
        );
        _pager = pager.gameObject;
    }

    private void BuildDetail(DesktopKit kit)
    {
        var detail = DesktopKit.Rect(_layout!, "Detail");
        detail.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        var column = Column(detail, InGameSizes.Space8);
        column.padding = new RectOffset(24, 24, 24, 20);

        // The owners' titles frame the two boards from outside: the opponent's above the
        // upper board, yours under the lower one. With one board only the upper title shows.
        var opponent = Line(detail, "OpponentHead", InGameSizes.Space8);
        DesktopKit.Size(opponent, -1, DesktopKit.RowArt);
        _opponentRank = DesktopKit.Art(
            opponent.transform,
            null,
            DesktopKit.RowArt,
            DesktopKit.RowArt
        );
        _opponentOwner = kit.Text(opponent.transform, string.Empty, KitTextRole.Section);
        _opponentOwner.richText = false;
        _opponentCaption = Flexible(kit.Text(opponent.transform, string.Empty, KitTextRole.Meta));
        _opponentHead = opponent.gameObject;

        _opponentPreview = Board(kit, detail, "OpponentBoardBounds", out _opponentStatus);
        _preview = Board(kit, detail, "PlayerBoardBounds", out _previewStatus);

        var player = Line(detail, "PlayerHead", InGameSizes.Space8);
        // A Section line is higher than its glyphs: a shorter box makes the ellipsis clear it.
        DesktopKit.Size(player, -1, DesktopKit.RowArt);
        _playerOwner = Flexible(kit.Text(player.transform, string.Empty, KitTextRole.Section));
        _playerHead = player.gameObject;

        // The run's battles, oldest at the left. The model lists them newest first, so the
        // strip arranges its cells in reverse and the left arrow pages to older battles.
        var days = Line(detail, "Days", InGameSizes.Space8);
        days.childAlignment = TextAnchor.MiddleCenter;
        _battleOlder = kit.PageArrow(days.transform, false, () => _pageBattles?.Invoke(1));
        _dayStrip = kit.DayStrip(days.transform, newestFirst: true);
        Flexible(_dayStrip.Track);
        _battleNewer = kit.PageArrow(days.transform, true, () => _pageBattles?.Invoke(-1));
        for (var i = 0; i < Slots; i++)
        {
            var slot = i;
            var day = kit.Day(_dayStrip.Days, string.Empty, KitMark.Won, () => _selectBattle(slot));
            day.Button.gameObject.SetActive(false);
            _days[i] = new DaySlot { Day = day };
        }
        _dayLine = days.gameObject;

        CreateActions(kit, detail);

        _emptyLabel = Wrapped(
            kit.Text(detail, string.Empty, KitTextRole.Section, Muted, TextAlignmentOptions.Center)
        );
        DesktopKit.Fill(_emptyLabel.rectTransform, InGameSizes.Space24);
        _emptyLabel.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
    }

    // The rect the game draws one board into, with the message that stands in for the board.
    private static RectTransform Board(
        DesktopKit kit,
        Transform parent,
        string name,
        out TextMeshProUGUI status
    )
    {
        var rect = DesktopKit.Rect(parent, name);
        rect.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        status = Wrapped(
            kit.Text(rect, string.Empty, KitTextRole.Body, Muted, TextAlignmentOptions.Center)
        );
        DesktopKit.Fill(status.rectTransform, InGameSizes.Space24);
        return rect;
    }

    private void NavigateArchive(int direction)
    {
        _list.StopMovement();
        _list.content.anchoredPosition = Vector2.zero;
        _pageArchive?.Invoke(direction);
    }

    private void UpdateContent()
    {
        if (_layout == null || _model is not { } m)
            return;
        var runs = m.SectionMode == HistorySectionMode.Runs;
        var ghost = m.SectionMode == HistorySectionMode.Ghost;
        var both = m.ShowsBothBoards;
        if (_boundSection != m.SectionMode)
        {
            _boundSection = m.SectionMode;
            _list.StopMovement();
            _list.content.anchoredPosition = Vector2.zero;
        }
        _title.text = m.Title;
        _supporters!.Bind(m.Supporters);
        for (var i = 0; i < _tabs.Length; i++)
            _tabs[i].On = Sections[i] == m.SectionMode;
        _tabs[2].gameObject.SetActive(m.FavoritesTabVisible);
        _heroSelect.gameObject.SetActive(runs);
        _heroSelect.Label = string.IsNullOrEmpty(m.SelectedRunHero)
            ? HistoryPanelText.AllHeroes()
            : HistoryPanelHeroNames.DisplayName(m.SelectedRunHero);
        _ghostFilters.SetActive(ghost);
        for (var i = 0; i < _outcomes.Length; i++)
            _outcomes[i].On = Outcomes[i] == m.GhostBattleFilter;
        _dayFilter.On = m.GhostDayMin10;
        BindHeroMenu(m);

        var hasRows = runs ? m.Runs.Count > 0 : m.VisibleBattles.Count > 0;
        var hasBattle = hasRows && m.VisibleBattles.Count > 0;
        var boards = hasBattle && _boardsAvailable;
        _opponentHead.SetActive(boards);
        _opponentPreview!.gameObject.SetActive(boards && both);
        _preview!.gameObject.SetActive(boards);
        _playerHead.SetActive(boards && both);
        _dayLine.SetActive(runs && hasBattle);
        BindTitles(m, runs);

        _pager.SetActive(hasRows);
        _newer.interactable = m.HasNewer && !m.PageLoading;
        _older.interactable = m.HasOlder && !m.PageLoading;
        _latest.interactable = !m.PageLoading;
        _pageLabel.text = m.PageLabel.Split('\n')[0];
        _battleNewer.interactable = m.BattleHasNewer;
        _battleOlder.interactable = m.BattleHasOlder;
        UpdateEmptyDetail();
        BindRows(m.ArchiveRows, runs ? m.SelectedRunIndex : m.SelectedBattleIndex, m.PageLoading);
        if (runs)
            BindDays(m.Timeline, m.SelectedBattleIndex, m.PageLoading);
        _statusLabel.text = m.StatusMessage ?? string.Empty;
        _statusLabel.color = StatusColor(m.StatusSeverity);
        RefreshActions();
    }

    // The upper title names the opponent (or the challenger of a single board) with their
    // rank, and what the day strip's cell has no room for: the day and the rating.
    private void BindTitles(HistoryPanelViewModel m, bool runs)
    {
        var titles = m.BoardTitles;
        _opponentOwner.text = titles.Upper;
        _playerOwner.text =
            titles.OutcomeText.Length == 0
                ? titles.Lower
                : $"{titles.Lower} · {Tinted(titles.OutcomeText, OutcomeColor(titles.Outcome))}";
        var selected = m.SelectedBattleIndex;
        string? rank = null;
        var caption = string.Empty;
        if (runs && selected >= 0 && selected < m.Timeline.Count)
        {
            var battle = m.Timeline[selected];
            rank = battle.Rank;
            caption = Joined(battle.DayText, battle.Rating);
        }
        else if (!runs && selected >= 0 && selected < m.ArchiveRows.Count)
        {
            var row = m.ArchiveRows[selected];
            rank = row.Rank;
            caption = row.Rating;
        }
        _opponentCaption.text = caption;
        var badge = _rankBadges.Get(rank);
        DesktopKit.Show(_opponentRank, badge);
        _opponentRank.gameObject.SetActive(badge != null);
    }

    // A player's name or note inside rich text: its own tags are shown, not read.
    private static string Plain(string text) =>
        $"<noparse>{text.Replace("</noparse>", string.Empty)}</noparse>";

    private static string Joined(params string[] parts) =>
        string.Join(" · ", parts.Where(part => part.Length > 0));

    private void BindRows(IReadOnlyList<HistoryArchiveRow> rows, int selected, bool loading)
    {
        _shownRows.Clear();
        for (var i = 0; i < _rows.Length; i++)
        {
            var slot = _rows[i];
            var row = slot.Row;
            var shown = i < rows.Count;
            if (row.Button.gameObject.activeSelf != shown)
                row.Button.gameObject.SetActive(shown);
            if (!shown)
                continue;
            var fields = rows[i];
            _shownRows.Add(row.Button);
            row.Button.On = i == selected;
            row.Button.interactable = !loading;
            // The name leads and the stamp follows it, small; a long stamp (a favorite's note)
            // is cut at the row's art. The meta line keeps what the run was and a Legendary
            // rating with its change.
            row.Title.text =
                fields.Stamp.Length == 0
                    ? Plain(fields.Name)
                    : $"{Plain(fields.Name)}  <size={InGameSizes.TextCaption}>{Tinted(Plain(fields.Stamp), Muted)}</size>";
            row.Meta.text = Joined(
                fields.Meta,
                fields.RatingDelta.Length == 0 ? fields.Rating
                    : fields.RatingTone == HistoryOutcomeTone.None
                        ? $"{fields.Rating} {fields.RatingDelta}"
                    : $"{fields.Rating} {Tinted(fields.RatingDelta, OutcomeColor(fields.RatingTone))}"
            );
            DesktopKit.Show(row.Rank, _rankBadges.Get(fields.Rank));
            DesktopKit.Show(row.Mark, MarkSprite(fields.Badge));
            if (slot.Bound && slot.Hero == fields.HeroId)
                continue;
            slot.Bound = true;
            slot.Hero = fields.HeroId;
            ShowHero(row.Portrait, fields.HeroId);
        }
        DesktopKit.Dividers(_shownRows);
    }

    // A finished run shows its trophy; every other row shows one of the kit's painted marks.
    private Sprite? MarkSprite(HistoryRowBadge badge) =>
        badge switch
        {
            HistoryRowBadge.Bronze => _trophies.Get(NativeTrophy.Bronze),
            HistoryRowBadge.Silver => _trophies.Get(NativeTrophy.Silver),
            HistoryRowBadge.Gold => _trophies.Get(NativeTrophy.Gold),
            HistoryRowBadge.Diamond => _trophies.Get(NativeTrophy.Diamond),
            HistoryRowBadge.StateCompleted => DesktopKit.Mark(KitMark.Completed),
            HistoryRowBadge.StateAbandoned => DesktopKit.Mark(KitMark.Abandoned),
            HistoryRowBadge.StateActive => DesktopKit.Mark(KitMark.Active),
            HistoryRowBadge.BattleWon => DesktopKit.Mark(KitMark.Won),
            HistoryRowBadge.BattleLost => DesktopKit.Mark(KitMark.Lost),
            _ => null,
        };

    private void BindDays(IReadOnlyList<HistoryTimelineItem> battles, int selected, bool loading)
    {
        for (var i = 0; i < _days.Length; i++)
        {
            var slot = _days[i];
            var day = slot.Day;
            var shown = i < battles.Count;
            if (day.Button.gameObject.activeSelf != shown)
                day.Button.gameObject.SetActive(shown);
            if (!shown)
                continue;
            var battle = battles[i];
            day.Button.On = i == selected;
            day.Button.interactable = !loading;
            day.Caption.text = battle.DayText;
            DesktopKit.Show(
                day.Mark,
                battle.Outcome switch
                {
                    HistoryOutcomeTone.Won => DesktopKit.Mark(KitMark.Won),
                    HistoryOutcomeTone.Lost => DesktopKit.Mark(KitMark.Lost),
                    _ => null,
                }
            );
            if (slot.Bound && slot.Hero == battle.HeroId)
                continue;
            slot.Bound = true;
            slot.Hero = battle.HeroId;
            ShowHero(day.Portrait, battle.HeroId);
        }
        var id = selected >= 0 && selected < battles.Count ? battles[selected].Id : null;
        if (id == _revealedDay)
            return;
        _revealedDay = id;
        // The strip finds its width over the next frames; the selected day follows it.
        _revealFrames = 3;
    }

    private void RevealSelectedDay()
    {
        if (_revealFrames <= 0 || _model is not { } m)
            return;
        _revealFrames--;
        var selected = m.SelectedBattleIndex;
        if (
            _dayLine.activeInHierarchy
            && selected >= 0
            && selected < Math.Min(Slots, m.Timeline.Count)
        )
            _dayStrip.Reveal((RectTransform)_days[selected].Day.Button.transform);
    }

    private void UpdateEmptyDetail()
    {
        if (_model is not { } model || _emptyLabel == null)
            return;
        var empty = model.ArchiveEmptyMessage.Length > 0;
        _emptyLabel.text = empty ? model.ArchiveEmptyMessage : _detailMessage;
        _emptyLabel.gameObject.SetActive(
            (empty || !_boardsAvailable) && _emptyLabel.text.Length > 0
        );
    }
}
