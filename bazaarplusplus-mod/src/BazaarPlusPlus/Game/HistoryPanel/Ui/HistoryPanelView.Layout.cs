#nullable enable
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    // A row's three text lines, filled from Core's HistoryArchiveRow in every section.
    private sealed class RowFields
    {
        internal TextMeshProUGUI Name = null!;
        internal TextMeshProUGUI Meta = null!;
        internal TextMeshProUGUI Stamp = null!;
    }

    private sealed class Row
    {
        internal Button Button = null!;
        internal Image Portrait = null!;
        internal Image Badge = null!;
        internal Image RankBadge = null!;
        internal TextMeshProUGUI Rating = null!;
        internal TextMeshProUGUI UnknownResult = null!;
        internal TextMeshProUGUI OutcomeLabel = null!;
        internal TextMeshProUGUI UnknownRank = null!;
        internal TextMeshProUGUI Label = null!;
        internal RowFields Fields = null!;
        internal string? Hero;
        internal string? Id;
    }

    // One selectable opponent in the run's timeline.
    private sealed class DayChip
    {
        internal Button Button = null!;
        internal Image Rank = null!;
        internal TextMeshProUGUI Unknown = null!;
        internal TextMeshProUGUI Day = null!;
        internal TextMeshProUGUI Name = null!;
        internal TextMeshProUGUI Rating = null!;
        internal TextMeshProUGUI Outcome = null!;
        internal Image Portrait = null!;
        internal string? Hero;
    }

    private sealed class DayStrip
    {
        internal ScrollRect Scroll = null!;
        internal DayChip[] Chips = new DayChip[40];
        internal readonly HistoryTimelineScroll Position = new();
    }

    private sealed class PageList
    {
        internal ScrollRect Scroll = null!;
        internal Row[] Rows = new Row[40];
        internal float Height;
        internal bool ResetScroll;
    }

    private PageList _archiveList = null!;

    // The archive's record ids, refilled on every refresh instead of a new array each time.
    private TextMeshProUGUI _archiveEmptyLabel = null!;
    private DayStrip _dayStrip = null!;
    private TextMeshProUGUI _title = null!,
        _statusLabel = null!,
        _pageLabel = null!;
    private Button _runsTab = null!,
        _ghostTab = null!,
        _favoritesTab = null!,
        _older = null!,
        _newer = null!,
        _latest = null!,
        _battleOlder = null!,
        _battleNewer = null!,
        _dayFilter = null!;
    private readonly Dictionary<string, Button> _heroes = new();
    private readonly Dictionary<GhostBattleFilter, Button> _outcomes = new();
    private Button _heroFilterButton = null!;
    private GameObject? _filterRoot;
    private GameObject _heroFilters = null!,
        _ghostFilters = null!;

    // The section and board count the archive rows and board rects were last laid out for.
    private (HistorySectionMode Section, bool BothBoards)? _boundLayout;
    private RectTransform _opponentTitle = null!,
        _playerTitle = null!;
    private TextMeshProUGUI _opponentOwner = null!,
        _playerOwner = null!;

    private void CreateLayout()
    {
        _layout = CreateRect("Layout", _root!.transform, 0, 0, 1, 1);
        _layout.gameObject.AddComponent<Image>().color = InGameTheme.Backdrop;
        Panel(_layout, .014f, .018f, .972f, .962f);
        _title = Text(_layout, "", HistoryPanelLayout.Title, 28);
        Button(
            _layout,
            HistoryPanelText.Close(),
            HistoryPanelLayout.CloseButton,
            _close,
            style: ButtonStyle.Standard
        );
        BuildSupporterHeader();
        var tabsTray = CreateRect("SectionTabs", _layout, HistoryPanelLayout.TabsTray);
        ControlSurface(tabsTray);
        _runsTab = Button(
            _layout,
            "",
            HistoryPanelLayout.RunsTab,
            () => _section(HistorySectionMode.Runs),
            style: ButtonStyle.GroupedOption
        );
        _ghostTab = Button(
            _layout,
            "",
            HistoryPanelLayout.GhostTab,
            () => _section(HistorySectionMode.Ghost),
            style: ButtonStyle.GroupedOption
        );
        _favoritesTab = Button(
            _layout,
            "",
            HistoryPanelLayout.FavoritesTab,
            () => _section(HistorySectionMode.Favorites),
            style: ButtonStyle.GroupedOption
        );
        _heroFilters = CreateRect(
            "HeroFilters",
            _layout,
            HistoryPanelLayout.FilterStrip
        ).gameObject;
        _heroFilterButton = Button(
            _heroFilters.transform,
            "",
            0,
            0,
            1,
            1,
            () => _filterRoot?.SetActive(true),
            size: 15
        );
        CreateFilterChevron(_heroFilterButton.transform);
        var filterLabel = ButtonLabel(_heroFilterButton);
        filterLabel.alignment = TextAlignmentOptions.MidlineLeft;
        _ghostFilters = CreateRect(
            "GhostFilters",
            _layout,
            HistoryPanelLayout.FilterStrip
        ).gameObject;
        foreach (
            var filter in new[]
            {
                GhostBattleFilter.All,
                GhostBattleFilter.IWon,
                GhostBattleFilter.ILost,
            }
        )
            _outcomes[filter] = Button(
                _ghostFilters.transform,
                "",
                (int)filter / 3f,
                0,
                .32f,
                .48f,
                () => _ghostFilter(filter)
            );
        _dayFilter = Button(
            _ghostFilters.transform,
            HistoryPanelText.FilterDayMin10(),
            0,
            .52f,
            1,
            .48f,
            _ghostDay
        );
        _archiveList = CreatePageList(
            "Archive",
            HistoryPanelLayout.ArchiveList,
            HistoryPanelLayout.RunRowHeight
        );
        _archiveEmptyLabel = Text(
            _layout,
            "",
            HistoryPanelLayout.DetailLeft,
            HistoryPanelLayout.DetailTop,
            HistoryPanelLayout.DetailWidth,
            HistoryPanelLayout.DetailBottom - HistoryPanelLayout.DetailTop,
            18,
            Muted,
            true
        );
        _archiveEmptyLabel.textWrappingMode = TextWrappingModes.Normal;
        _archiveEmptyLabel.overflowMode = TextOverflowModes.Overflow;
        _archiveEmptyLabel.alignment = TextAlignmentOptions.Center;
        _dayStrip = CreateDayStrip();
        var archivePager = CreateRect("ArchivePager", _layout, HistoryPanelLayout.ArchivePager);
        _newer = Button(
            archivePager,
            HistoryPanelText.Newer(),
            0,
            0,
            .32f,
            1,
            () => NavigateArchive(-1),
            size: 14
        );
        _older = Button(
            archivePager,
            HistoryPanelText.Older(),
            .34f,
            0,
            .32f,
            1,
            () => NavigateArchive(1),
            size: 14
        );
        _latest = Button(
            archivePager,
            HistoryPanelText.Latest(),
            .68f,
            0,
            .32f,
            1,
            () => NavigateArchive(0),
            size: 14
        );
        _pageLabel = Text(_layout, "", HistoryPanelLayout.PageLabel, 12, Muted);
        _pageLabel.textWrappingMode = TextWrappingModes.Normal;
        _battleNewer = Button(
            _layout,
            "↑",
            HistoryPanelLayout.TimelineNewer,
            () => _pageBattles?.Invoke(-1)
        );
        _battleOlder = Button(
            _layout,
            "↓",
            HistoryPanelLayout.TimelineOlder,
            () => _pageBattles?.Invoke(1)
        );
        // Battle identity, rank and outcome live in the timeline; the title names the owner.
        _opponentTitle = CreateRect(
            "OpponentBoardTitle",
            _layout,
            HistoryPanelLayout.DetailLeft,
            HistoryPanelLayout.OpponentTitleTop,
            HistoryPanelLayout.DetailWidth,
            HistoryPanelLayout.BoardTitleHeight
        );
        _opponentOwner = Text(_opponentTitle, "", 0, 0, 1, 1, 16, InGameTheme.InkSoft, true);
        _opponentPreview = CreateRect(
            "OpponentBoardBounds",
            _layout,
            HistoryPanelLayout.DetailLeft,
            HistoryPanelLayout.OpponentBoardTop,
            HistoryPanelLayout.DetailWidth,
            HistoryPanelLayout.OpponentBoardHeight
        );
        _opponentStatus = Text(_opponentPreview, "", 0, .4f, 1, .2f, 17, Muted, true);
        _opponentStatus.textWrappingMode = TextWrappingModes.Normal;
        _opponentStatus.overflowMode = TextOverflowModes.Overflow;
        _preview = CreateRect(
            "PlayerBoardBounds",
            _layout,
            HistoryPanelLayout.DetailLeft,
            HistoryPanelLayout.PlayerBoardTop,
            HistoryPanelLayout.DetailWidth,
            HistoryPanelLayout.PlayerBoardHeight
        );
        _previewStatus = Text(_preview, "", 0, .35f, 1, .3f, 17, Muted, true);
        _previewStatus.textWrappingMode = TextWrappingModes.Normal;
        _previewStatus.overflowMode = TextOverflowModes.Overflow;
        // The lower board's ownership title. In Ghost this is the ONLY board and it belongs
        // to the challenger, so the wording is section-dependent, never hardcoded.
        _playerTitle = CreateRect(
            "PlayerBoardTitle",
            _layout,
            HistoryPanelLayout.DetailLeft,
            HistoryPanelLayout.PlayerTitleTop,
            HistoryPanelLayout.DetailWidth,
            HistoryPanelLayout.BoardTitleHeight
        );
        _playerOwner = Text(_playerTitle, "", 0, 0, 1, 1, 16, InGameTheme.InkSoft, true);
        _statusLabel = Text(_layout, "", HistoryPanelLayout.StatusLabel, 12, Muted);
        CreateActions();
        CreateHeroFilterMenu();
    }

    private void NavigateArchive(int direction)
    {
        _archiveList.ResetScroll = true;
        _pageArchive?.Invoke(direction);
    }

    private const int ListEdgeSoftness = 24;

    private static void AddScrollMask(ScrollRect scroll)
    {
        var mask = scroll.viewport.gameObject.AddComponent<RectMask2D>();
        mask.softness = new Vector2Int(0, ListEdgeSoftness);
        scroll.onValueChanged.AddListener(_ => UpdateScrollMask(scroll));
        UpdateScrollMask(scroll);
    }

    private static void UpdateScrollMask(ScrollRect scroll)
    {
        var offset = scroll.content.anchoredPosition.y;
        var end = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
        // Fade only where more rows continue. Extending an exhausted edge keeps the
        // first/last card's frame intact without painting a strip over the list.
        scroll.viewport.GetComponent<RectMask2D>().padding = new Vector4(
            0,
            offset >= end - .5f ? -ListEdgeSoftness : 0,
            0,
            offset <= .5f ? -ListEdgeSoftness : 0
        );
    }

    private PageList CreatePageList(string name, HistoryRect bounds, float rowHeight)
    {
        var viewport = CreateRect(name, _layout!, bounds);
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 45;
        var content = CreateRect("Rows", viewport, 0, 0, 1, 1);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        scroll.content = content;
        AddScrollMask(scroll);
        var list = new PageList { Scroll = scroll, Height = rowHeight };
        for (var i = 0; i < list.Rows.Length; i++)
        {
            var index = i;
            var button = Button(
                content,
                "",
                0,
                0,
                1,
                1,
                () =>
                {
                    if (_model?.SectionMode == HistorySectionMode.Runs)
                        _selectRun(index);
                    else
                        _selectBattle(index);
                },
                size: 14,
                style: ButtonStyle.Record
            );
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 1);
            rect.sizeDelta = new Vector2(0, rowHeight - 5);
            rect.anchoredPosition = new Vector2(0, -i * rowHeight);
            var label = ButtonLabel(button);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.rectTransform.anchorMin = new Vector2(.26f, .06f);
            label.rectTransform.anchorMax = new Vector2(.77f, .94f);
            var portrait = CreateRect("Hero", rect, .025f, .11f, .21f, .70f)
                .gameObject.AddComponent<Image>();
            portrait.raycastTarget = false;
            portrait.preserveAspect = true;
            portrait.color = Color.clear;
            var badge = Badge(rect, "Outcome", .82f, .71f, .14f, .25f);
            var rank = Badge(rect, "Rank", .80f, .05f, .18f, .45f);
            var unknownResult = Text(badge.transform, "?", 0, 0, 1, 1, 14, Muted, true);
            var outcomeLabel = Text(rect, "", .845f, .69f, .135f, .29f, 14, Ink, true);
            var unknownRank = Text(rank.transform, "?", 0, 0, 1, 1, 14, Muted, true);
            var rating = Text(rect, "", .77f, .49f, .22f, .18f, 10, Ink, true);
            // Identity leads; mode, duration and timestamp stay in the text column.
            var fields = new RowFields
            {
                Name = OneLine(Text(rect, "", .245f, .07f, .425f, .28f, 15)),
                Meta = OneLine(Text(rect, "", .245f, .39f, .425f, .22f, 11, Muted)),
                Stamp = OneLine(Text(rect, "", .245f, .68f, .425f, .22f, 11, Muted)),
            };
            list.Rows[i] = new Row
            {
                Button = button,
                Portrait = portrait,
                Badge = badge,
                RankBadge = rank,
                Rating = rating,
                UnknownResult = unknownResult,
                OutcomeLabel = outcomeLabel,
                UnknownRank = unknownRank,
                Label = label,
                Fields = fields,
            };
            button.gameObject.SetActive(false);
        }
        return list;
    }

    private DayStrip CreateDayStrip()
    {
        var viewport = CreateRect(
            "BattleTimeline",
            _layout!,
            HistoryPanelLayout.TimelineLeft,
            HistoryPanelLayout.TimelineTop,
            HistoryPanelLayout.TimelineWidth,
            HistoryPanelLayout.TimelineHeight
        );
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        // A run has no hard battle cap: the win target ends it at 10 wins, but losses only
        // drain prestige, so the rail scrolls and pages rather than assuming a fixed count.
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 45;
        var content = CreateRect("Chips", viewport, 0, 0, 1, 1);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        scroll.content = content;
        AddScrollMask(scroll);
        var strip = new DayStrip { Scroll = scroll };
        for (var i = 0; i < strip.Chips.Length; i++)
        {
            var slot = i;
            var button = Button(
                content,
                "",
                0,
                0,
                1,
                1,
                () => _selectBattle(slot),
                size: 11,
                style: ButtonStyle.Record
            );
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 1);
            rect.sizeDelta = new Vector2(0, HistoryPanelLayout.DayChipHeight - 5);
            rect.anchoredPosition = new Vector2(0, -i * HistoryPanelLayout.DayChipHeight);
            // The button's own label is unused; each field has its own slot.
            ButtonLabel(button).gameObject.SetActive(false);
            var portrait = CreateRect("OpponentHero", rect, .055f, .48f, .19f, .40f)
                .gameObject.AddComponent<Image>();
            portrait.raycastTarget = false;
            portrait.preserveAspect = true;
            portrait.color = Color.clear;
            // The result and day share the first baseline; identity occupies the second.
            var outcome = Text(rect, "", .08f, .08f, .50f, .35f, 15, Ink);
            // The native font's line box is taller than its visible glyphs. Keep the
            // compact row without TMP's vertical ellipsis suppressing the result.
            outcome.overflowMode = TextOverflowModes.Overflow;
            var day = Wrapped(Text(rect, "", .64f, .08f, .28f, .35f, 10, Muted));
            day.alignment = TextAlignmentOptions.MidlineRight;
            var rank = Badge(rect, "OpponentRank", .77f, .46f, .175f, .36f);
            var unknown = Text(rank.transform, "?", 0, 0, 1, 1, 9, Muted, true);
            var rating = Wrapped(Text(rect, "", .72f, .80f, .26f, .16f, 8, Ink, true));
            // TMP's vertical ellipsis path clears the entire number when the native
            // font's line metrics exceed the slot, even if every digit fits horizontally.
            rating.overflowMode = TextOverflowModes.Overflow;
            var name = Wrapped(Text(rect, "", .285f, .51f, .43f, .34f, 9, Ink));
            strip.Chips[i] = new DayChip
            {
                Button = button,
                Rank = rank,
                Unknown = unknown,
                Day = day,
                Name = name,
                Rating = rating,
                Outcome = outcome,
                Portrait = portrait,
            };
            button.gameObject.SetActive(false);
        }
        return strip;
    }

    private void BindDayStrip(IReadOnlyList<HistoryTimelineItem> battles, int selected)
    {
        var strip = _dayStrip;
        var step = HistoryPanelLayout.DayChipHeight;
        for (var i = 0; i < strip.Chips.Length; i++)
        {
            var chip = strip.Chips[i];
            chip.Button.gameObject.SetActive(i < battles.Count);
            if (i >= battles.Count)
                continue;
            // Keep the page's newest-first order, matching the archive and pager arrows.
            var battle = battles[i];
            SetButton(chip.Button, string.Empty, i == selected, !_model!.PageLoading);
            var hasRating = BindRank(
                chip.Rank,
                chip.Rating,
                chip.Unknown,
                battle.Rank,
                "?",
                battle.Rating
            );
            Anchor(
                chip.Rank.rectTransform,
                HistoryPanelLayout.Anchors(.77f, hasRating ? .46f : .52f, .175f, .36f)
            );
            chip.Day.text = battle.DayText;
            chip.Name.text = battle.OpponentName;
            BindOutcome(chip.Outcome, battle.Outcome, battle.OutcomeText);
            var hero = battle.HeroId;
            if (chip.Hero != hero)
            {
                chip.Hero = hero;
                chip.Portrait.color = Color.clear;
                _portraits.RemoveAll(p => p.Target == chip.Portrait);
                if (TheDragonsHeroIdentity.TryResolve(hero, out var identity))
                    _portraits.Add(
                        (
                            chip.Portrait,
                            HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(identity),
                            _generation
                        )
                    );
            }
        }
        strip.Scroll.content.sizeDelta = new Vector2(0, battles.Count * step);
        var hasSelection = selected >= 0 && selected < battles.Count;
        var position = strip.Scroll.content.anchoredPosition;
        var offset = strip.Position.Bind(
            hasSelection ? battles[selected].Id : null,
            hasSelection ? selected : -1,
            battles.Count,
            position.y,
            strip.Scroll.viewport.rect.height
        );
        if (position.y != offset)
        {
            strip.Scroll.StopMovement();
            strip.Scroll.content.anchoredPosition = new Vector2(position.x, offset);
        }
        UpdateScrollMask(strip.Scroll);
    }

    // MEMORY gotcha: NoWrap + Ellipsis at a scaled font can hit TMP's m_characterCount == 0
    // branch and drop the whole block. Every row label opts out, as the existing ones do.
    // A row field: one line, cut with an ellipsis rather than spilling onto the next field.
    private static TextMeshProUGUI OneLine(TextMeshProUGUI text)
    {
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static TextMeshProUGUI Wrapped(TextMeshProUGUI text)
    {
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    // Archive rows carry runs in Runs and battles in Ghost and Favorites. Both fill the same
    // fields; the fields and the shared badges move to the slots that suit whichever is showing.
    private void ApplyArchiveRowMode(bool runs)
    {
        _archiveList.Height = runs
            ? HistoryPanelLayout.RunRowHeight
            : HistoryPanelLayout.GhostRowHeight;
        for (var i = 0; i < _archiveList.Rows.Length; i++)
        {
            var row = _archiveList.Rows[i];
            var rect = (RectTransform)row.Button.transform;
            rect.sizeDelta = new Vector2(0, _archiveList.Height - 5);
            rect.anchoredPosition = new Vector2(0, -i * _archiveList.Height);
            row.Label.gameObject.SetActive(false);
            row.OutcomeLabel.gameObject.SetActive(!runs);
            // The same three single-line fields as a touch row: the text column ends at the
            // rank badge, so a battle row's is wider than a run row's.
            var width = runs ? .425f : .52f;
            Anchor(
                row.Fields.Name.rectTransform,
                HistoryPanelLayout.Anchors(.245f, runs ? .07f : .1f, width, runs ? .28f : .26f)
            );
            Anchor(
                row.Fields.Meta.rectTransform,
                HistoryPanelLayout.Anchors(.245f, runs ? .39f : .41f, width, .22f)
            );
            Anchor(
                row.Fields.Stamp.rectTransform,
                HistoryPanelLayout.Anchors(.245f, runs ? .68f : .67f, width, .22f)
            );
            Anchor(
                row.Portrait.rectTransform,
                runs
                    ? HistoryPanelLayout.Anchors(.025f, .14f, .20f, .72f)
                    : HistoryPanelLayout.Anchors(.025f, .11f, .21f, .70f)
            );
            Anchor(
                (RectTransform)row.Badge.transform,
                runs
                    ? HistoryPanelLayout.Anchors(.84f, .25f, .13f, .48f)
                    : HistoryPanelLayout.Anchors(.775f, .73f, .065f, .21f)
            );
            Anchor(
                row.Rating.rectTransform,
                runs
                    ? HistoryPanelLayout.Anchors(.66f, .66f, .17f, .20f)
                    : HistoryPanelLayout.Anchors(.77f, .49f, .22f, .18f)
            );
            row.Rating.alignment = TextAlignmentOptions.Center;
        }
    }

    // Every row fills the name, meta and stamp fields; a battle row adds its outcome word.
    private void BindRows(
        PageList list,
        IReadOnlyList<HistoryArchiveRow> rows,
        int selected,
        bool runs
    )
    {
        var offset = Mathf.Max(0, list.Scroll.content.anchoredPosition.y);
        var oldTop = Math.Min(39, (int)(offset / list.Height));
        var topId = list.Rows[oldTop].Id;
        var newTop = -1;
        for (var i = 0; i < list.Rows.Length; i++)
        {
            var row = list.Rows[i];
            row.Button.gameObject.SetActive(i < rows.Count);
            if (i >= rows.Count)
            {
                row.Id = null;
                continue;
            }
            var fields = rows[i];
            row.Id = fields.Id;
            if (row.Id == topId)
                newTop = i;
            SetButton(row.Button, string.Empty, i == selected, !_model!.PageLoading);
            row.Fields.Name.text = fields.Name;
            row.Fields.Meta.text = fields.Meta;
            row.Fields.Stamp.text = fields.Stamp;
            if (!runs)
                BindOutcome(row.OutcomeLabel, fields.Outcome, fields.OutcomeText);
            row.Badge.sprite = BadgeSprite(fields.Badge);
            row.Badge.color = row.Badge.sprite != null ? Color.white : Color.clear;
            row.UnknownResult.text = fields.BadgeText;
            row.UnknownResult.gameObject.SetActive(runs && row.Badge.sprite == null);
            var hasRating = BindRank(
                row.RankBadge,
                row.Rating,
                row.UnknownRank,
                fields.Rank,
                fields.RankText,
                fields.Rating,
                fields.RatingDelta,
                fields.RatingTone
            );
            Anchor(
                row.RankBadge.rectTransform,
                runs
                    ? HistoryPanelLayout.Anchors(.68f, hasRating ? .14f : .265f, .13f, .47f)
                    : HistoryPanelLayout.Anchors(.80f, .05f, .18f, .45f)
            );
            var name = fields.HeroId;
            if (row.Hero != name)
            {
                row.Hero = name;
                row.Portrait.color = Color.clear;
                _portraits.RemoveAll(p => p.Target == row.Portrait);
                if (TheDragonsHeroIdentity.TryResolve(name, out var identity))
                    _portraits.Add(
                        (
                            row.Portrait,
                            HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(identity),
                            _generation
                        )
                    );
            }
        }
        list.Scroll.content.sizeDelta = new Vector2(0, rows.Count * list.Height);
        var nextOffset =
            list.ResetScroll ? 0
            : newTop >= 0 ? newTop * list.Height + offset % list.Height
            : Math.Max(0, selected) * list.Height;
        list.Scroll.content.anchoredPosition = new Vector2(
            0,
            Mathf.Clamp(
                nextOffset,
                0,
                Mathf.Max(0, list.Scroll.content.sizeDelta.y - list.Scroll.viewport.rect.height)
            )
        );
        list.ResetScroll = false;
        UpdateScrollMask(list.Scroll);
    }

    private Sprite? BadgeSprite(HistoryRowBadge badge) =>
        badge switch
        {
            HistoryRowBadge.Bronze => _skin?[1],
            HistoryRowBadge.Silver => _skin?[2],
            HistoryRowBadge.Gold => _skin?[3],
            HistoryRowBadge.Diamond => _skin?[4],
            HistoryRowBadge.BattleWon => _winBadge,
            HistoryRowBadge.BattleLost => _lossBadge,
            HistoryRowBadge.None => null,
            _ => _stateBadges.Length == 0
                ? null
                : _stateBadges[
                    badge == HistoryRowBadge.StateActive ? 2
                    : badge == HistoryRowBadge.StateAbandoned ? 1
                    : 0
                ],
        };

    private void UpdateContent()
    {
        if (_layout == null || _model is not { } m)
            return;
        var runs = m.SectionMode == HistorySectionMode.Runs;
        var ghost = m.SectionMode == HistorySectionMode.Ghost;
        var both = m.ShowsBothBoards;
        if (_boundLayout != (m.SectionMode, both))
        {
            // Selecting a favorite of the other origin changes only the board count.
            if (_boundLayout?.Section != m.SectionMode)
            {
                _archiveList.ResetScroll = true;
                ApplyArchiveRowMode(runs);
            }
            _boundLayout = (m.SectionMode, both);
            _layoutFrames = 2;
            // One board (Ghost, a Ghost-origin favorite) drops the upper one, so the surviving
            // title follows the board up.
            Anchor(
                _playerTitle,
                HistoryPanelLayout.Anchors(
                    HistoryPanelLayout.DetailLeft,
                    both ? HistoryPanelLayout.PlayerTitleTop : HistoryPanelLayout.GhostTitleTop,
                    HistoryPanelLayout.DetailWidth,
                    HistoryPanelLayout.BoardTitleHeight
                )
            );
            // Authored top-down like every other rect, so the two y conventions no longer
            // have to be reconciled by hand at this one call site.
            Anchor(
                _preview!,
                HistoryPanelLayout.Anchors(
                    HistoryPanelLayout.DetailLeft,
                    both ? HistoryPanelLayout.PlayerBoardTop : HistoryPanelLayout.GhostBoardTop,
                    HistoryPanelLayout.DetailWidth,
                    both
                        ? HistoryPanelLayout.PlayerBoardHeight
                        : HistoryPanelLayout.GhostBoardHeight
                )
            );
        }
        _title.text = m.Title;
        SetButton(_runsTab, HistoryPanelText.RunsTab(), runs);
        SetButton(_ghostTab, HistoryPanelText.GhostTab(), ghost);
        _favoritesTab.gameObject.SetActive(m.FavoritesTabVisible);
        SetButton(
            _favoritesTab,
            HistoryPanelText.FavoritesTab(),
            m.SectionMode == HistorySectionMode.Favorites
        );
        _heroFilters.SetActive(runs);
        _ghostFilters.SetActive(ghost);
        Anchor(
            (RectTransform)_ghostFilters.transform,
            HistoryPanelLayout.Anchors(.033f, .175f, .204f, .09f)
        );
        var archiveTop =
            ghost ? .275f
            : runs ? HistoryPanelLayout.ArchiveList.Y
            : .175f;
        Anchor(
            _archiveList.Scroll.GetComponent<RectTransform>(),
            HistoryPanelLayout.Anchors(
                HistoryPanelLayout.ArchiveContentLeft,
                archiveTop,
                HistoryPanelLayout.ArchiveContentWidth,
                HistoryPanelLayout.ListBottom - archiveTop
            )
        );
        SetButton(
            _heroFilterButton,
            (
                string.IsNullOrEmpty(m.SelectedRunHero)
                    ? HistoryPanelText.AllHeroes()
                    : HistoryPanelHeroNames.DisplayName(m.SelectedRunHero)
            )
        );
        foreach (var pair in _heroes)
            SetButton(
                pair.Value,
                string.IsNullOrEmpty(pair.Key)
                    ? HistoryPanelText.AllHeroes()
                    : HistoryPanelHeroNames.DisplayName(pair.Key),
                string.IsNullOrEmpty(pair.Key)
                    ? string.IsNullOrEmpty(m.SelectedRunHero)
                    : HistoryPanelHeroPresentation.IsSelected(m.SelectedRunHero, pair.Key)
            );
        foreach (var pair in _outcomes)
            SetButton(
                pair.Value,
                HistoryPanelText.OutcomeFilter(pair.Key),
                pair.Key == m.GhostBattleFilter
            );
        SetButton(_dayFilter, HistoryPanelText.FilterDayMin10(), m.GhostDayMin10);
        var hasRows = runs ? m.Runs.Count > 0 : m.VisibleBattles.Count > 0;
        var hasBattle = hasRows && m.VisibleBattles.Count > 0;
        _dayStrip.Scroll.gameObject.SetActive(runs && hasBattle);
        _opponentPreview!.gameObject.SetActive(both && hasBattle && _boardsAvailable);
        _opponentTitle.gameObject.SetActive(both && hasBattle && _boardsAvailable);
        _playerTitle.gameObject.SetActive(hasBattle && _boardsAvailable);
        _preview!.gameObject.SetActive(hasBattle && _boardsAvailable);
        // Two boards show yours below the opponent's, with the outcome; one board is the
        // challenger's, titled by the upper title.
        var titles = m.BoardTitles;
        _opponentOwner.text = titles.Upper;
        _playerOwner.text = both
            ? titles.OutcomeText.Length == 0
                ? titles.Lower
                : $"{titles.Lower} · {Tinted(titles.OutcomeText, OutcomeColor(titles.Outcome))}"
            : titles.Upper;
        _battleNewer.gameObject.SetActive(runs && hasBattle);
        _battleOlder.gameObject.SetActive(runs && hasBattle);
        SetButtonEnabled(_newer, m.HasNewer && !m.PageLoading);
        SetButtonEnabled(_older, m.HasOlder && !m.PageLoading);
        SetButtonEnabled(_latest, !m.PageLoading);
        SetButtonEnabled(_battleNewer, m.BattleHasNewer);
        SetButtonEnabled(_battleOlder, m.BattleHasOlder);
        _newer.gameObject.SetActive(hasRows);
        _older.gameObject.SetActive(hasRows);
        _latest.gameObject.SetActive(hasRows);
        _pageLabel.gameObject.SetActive(hasRows);
        _pageLabel.text = m.PageLabel.Split('\n')[0];
        UpdateEmptyDetail();
        BindRows(
            _archiveList,
            m.ArchiveRows,
            runs ? m.SelectedRunIndex : m.SelectedBattleIndex,
            runs
        );
        if (runs)
            BindDayStrip(m.Timeline, m.SelectedBattleIndex);
        _statusLabel.text = m.StatusMessage ?? "";
        _statusLabel.color = StatusColor(m.StatusSeverity);
        BindSupporters(m.Supporters);
        RefreshActions();
    }

    private void UpdateEmptyDetail()
    {
        if (_model is not { } model || _archiveEmptyLabel == null)
            return;
        var empty = model.ArchiveEmptyMessage.Length > 0;
        _archiveEmptyLabel.text = empty ? model.ArchiveEmptyMessage : _detailMessage;
        _archiveEmptyLabel.gameObject.SetActive(
            (empty || !_boardsAvailable) && _archiveEmptyLabel.text.Length > 0
        );
    }

    private static Image Badge(
        Transform parent,
        string name,
        float x,
        float y,
        float width,
        float height
    )
    {
        var image = CreateRect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.color = Color.clear;
        return image;
    }

    private static Color OutcomeColor(HistoryOutcomeTone tone) =>
        tone switch
        {
            HistoryOutcomeTone.Won => InGameTheme.Success,
            HistoryOutcomeTone.Lost => InGameTheme.Failure,
            _ => Muted,
        };

    private static string Tinted(string text, Color color) =>
        $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";

    private static void BindOutcome(TextMeshProUGUI label, HistoryOutcomeTone tone, string text)
    {
        label.text = text;
        label.color = OutcomeColor(tone);
    }

    // The rank's art, else its name; a Legendary rank also carries its rating and change.
    private bool BindRank(
        Image badge,
        TextMeshProUGUI ratingLabel,
        TextMeshProUGUI unknown,
        string? rank,
        string rankText,
        string rating,
        string ratingDelta = "",
        HistoryOutcomeTone ratingTone = HistoryOutcomeTone.None
    )
    {
        badge.sprite = _rankBadges.Get(rank);
        badge.color = badge.sprite != null ? Color.white : Color.clear;
        unknown.text = rankText;
        unknown.gameObject.SetActive(badge.sprite == null);
        ratingLabel.text =
            ratingDelta.Length == 0 ? rating
            : ratingTone == HistoryOutcomeTone.None ? $"{rating} {ratingDelta}"
            : $"{rating} {Tinted(ratingDelta, OutcomeColor(ratingTone))}";
        return rating.Length > 0;
    }
}
