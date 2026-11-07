#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    // Hidden in Ghost and Favorites, where the same archive rows carry battles.
    private sealed class RunFields
    {
        internal TextMeshProUGUI Name = null!;
        internal TextMeshProUGUI Meta = null!;
        internal TextMeshProUGUI Stamp = null!;

        internal void SetActive(bool active)
        {
            Name.gameObject.SetActive(active);
            Meta.gameObject.SetActive(active);
            Stamp.gameObject.SetActive(active);
        }
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
        internal RunFields Run = null!;
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
    private readonly List<string> _archiveIds = new(40);
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
            T("Newer", "较新"),
            0,
            0,
            .32f,
            1,
            () => NavigateArchive(-1),
            size: 14
        );
        _older = Button(
            archivePager,
            T("Older", "较早"),
            .34f,
            0,
            .32f,
            1,
            () => NavigateArchive(1),
            size: 14
        );
        _latest = Button(
            archivePager,
            T("Latest", "最新"),
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
            var run = new RunFields
            {
                Name = Wrapped(Text(rect, "", .245f, .07f, .425f, .28f, 15)),
                Meta = Wrapped(Text(rect, "", .245f, .39f, .425f, .22f, 11, Muted)),
                Stamp = Wrapped(Text(rect, "", .245f, .68f, .425f, .22f, 11, Muted)),
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
                Run = run,
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

    private void BindDayStrip(IReadOnlyList<HistoryBattleRecord> battles, int selected)
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
                battle.OpponentRank,
                battle.OpponentRating
            );
            Anchor(
                chip.Rank.rectTransform,
                HistoryPanelLayout.Anchors(.77f, hasRating ? .46f : .52f, .175f, .36f)
            );
            chip.Day.text = HistoryPanelText.DayBadge(battle.Day);
            chip.Name.text = string.IsNullOrWhiteSpace(battle.OpponentName)
                ? HistoryPanelText.UnknownOpponent()
                : battle.OpponentName;
            BindBattleResult(chip.Outcome, battle);
            var hero = battle.OpponentHero;
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
            hasSelection ? battles[selected].BattleId : null,
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
    private static TextMeshProUGUI Wrapped(TextMeshProUGUI text)
    {
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    // Archive rows carry runs in Runs and battles in Ghost and Favorites. Runs get the
    // laid-out fields; battles keep the single wrapped label, and the shared badges move
    // to the slots that suit whichever is showing.
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
            row.Run.SetActive(runs);
            row.Label.gameObject.SetActive(!runs);
            row.OutcomeLabel.gameObject.SetActive(!runs);
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

    private void BindRows(
        PageList list,
        IReadOnlyList<string> ids,
        Func<int, string> label,
        Func<int, string?> hero,
        Func<int, Sprite?> badge,
        Func<int, string?> rank,
        Func<int, int?> rating,
        Func<int, int?> ratingDelta,
        int selected,
        Func<int, HistoryRunRowFields>? runFields = null
    )
    {
        var offset = Mathf.Max(0, list.Scroll.content.anchoredPosition.y);
        var oldTop = Math.Min(39, (int)(offset / list.Height));
        var topId = list.Rows[oldTop].Id;
        var newTop = -1;
        for (var i = 0; i < list.Rows.Length; i++)
        {
            var row = list.Rows[i];
            row.Button.gameObject.SetActive(i < ids.Count);
            if (i >= ids.Count)
            {
                row.Id = null;
                continue;
            }
            row.Id = ids[i];
            if (row.Id == topId)
                newTop = i;
            SetButton(
                row.Button,
                runFields == null ? label(i) : string.Empty,
                i == selected,
                !_model!.PageLoading
            );
            if (runFields != null)
            {
                var fields = runFields(i);
                row.Run.Name.text = fields.Name;
                row.Run.Meta.text = fields.Meta;
                row.Run.Stamp.text = fields.Stamp;
            }
            row.Badge.sprite = badge(i);
            row.Badge.color = row.Badge.sprite != null ? Color.white : Color.clear;
            row.UnknownResult.gameObject.SetActive(runFields != null && row.Badge.sprite == null);
            var hasRating = BindRank(
                row.RankBadge,
                row.Rating,
                row.UnknownRank,
                rank(i),
                rating(i),
                ratingDelta(i)
            );
            Anchor(
                row.RankBadge.rectTransform,
                runFields != null
                    ? HistoryPanelLayout.Anchors(.68f, hasRating ? .14f : .265f, .13f, .47f)
                    : HistoryPanelLayout.Anchors(.80f, .05f, .18f, .45f)
            );
            var name = hero(i);
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
        list.Scroll.content.sizeDelta = new Vector2(0, ids.Count * list.Height);
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

    private List<string> ArchiveIds<T>(IReadOnlyList<T> rows, Func<T, string> id)
    {
        _archiveIds.Clear();
        for (var i = 0; i < rows.Count; i++)
            _archiveIds.Add(id(rows[i]));
        return _archiveIds;
    }

    private Sprite? RunBadge(HistoryRunRecord run) =>
        HistoryPanelFormatter.GetRunOutcomeTier(run) switch
        {
            RunOutcomeTier.Bronze => _skin?[1],
            RunOutcomeTier.Silver => _skin?[2],
            RunOutcomeTier.Gold => _skin?[3],
            RunOutcomeTier.Diamond => _skin?[4],
            _ => _stateBadges.Length == 0
                ? null
                : _stateBadges[
                    run.RawStatus == "active" ? 2
                    : run.RawStatus == "abandoned" ? 1
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
                    ? T("All heroes", "全部英雄")
                    : HistoryPanelHeroNames.DisplayName(m.SelectedRunHero)
            )
        );
        foreach (var pair in _heroes)
            SetButton(
                pair.Value,
                string.IsNullOrEmpty(pair.Key)
                    ? T("All heroes", "全部英雄")
                    : HistoryPanelHeroNames.DisplayName(pair.Key),
                string.IsNullOrEmpty(pair.Key)
                    ? string.IsNullOrEmpty(m.SelectedRunHero)
                    : HistoryPanelHeroPresentation.IsSelected(m.SelectedRunHero, pair.Key)
            );
        foreach (var pair in _outcomes)
            SetButton(
                pair.Value,
                pair.Key == GhostBattleFilter.All ? T("All", "全部")
                    : pair.Key == GhostBattleFilter.IWon ? T("Won", "我赢了")
                    : T("Lost", "我输了"),
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
        _opponentOwner.text = HistoryPanelText.BoardOpponentTitle();
        // Two boards show yours below the opponent's; one board is the challenger's.
        _playerOwner.text = both
            ? HistoryPanelText.BoardYouTitle()
            : HistoryPanelText.BoardChallengerTitle();
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
        if (runs)
            BindRows(
                _archiveList,
                ArchiveIds(m.Runs, r => r.RunId),
                _ => string.Empty,
                i => m.Runs[i].Hero,
                i => RunBadge(m.Runs[i]),
                i => m.Runs[i].PlayerRank,
                i => m.Runs[i].PlayerRating,
                i => m.Runs[i].PlayerRatingDelta,
                m.SelectedRunIndex,
                i =>
                    HistoryPanelFormatter.RunRowFields(
                        m.Runs[i],
                        HistoryPanelHeroNames.DisplayName(m.Runs[i].Hero)
                    )
            );
        else
        {
            BindRows(
                _archiveList,
                ArchiveIds(m.VisibleBattles, b => b.BattleId),
                i =>
                    ghost
                        ? HistoryPanelFormatter.GhostListText(m.VisibleBattles[i])
                        : m.FavoriteRowLabels[i],
                i => m.VisibleBattles[i].OpponentHero,
                i => BattleBadge(m.VisibleBattles[i]),
                i => m.VisibleBattles[i].OpponentRank,
                i => m.VisibleBattles[i].OpponentRating,
                _ => null,
                m.SelectedBattleIndex
            );
            for (var i = 0; i < m.VisibleBattles.Count; i++)
                BindBattleResult(_archiveList.Rows[i].OutcomeLabel, m.VisibleBattles[i]);
        }
        if (runs)
            BindDayStrip(m.VisibleBattles, m.SelectedBattleIndex);
        _statusLabel.text = m.StatusMessage ?? "";
        _statusLabel.color = StatusColor(m.StatusSeverity);
        BindSupporters(m.Supporters);
        RefreshActions();
    }

    private void UpdateEmptyDetail()
    {
        if (_model is not { } model || _archiveEmptyLabel == null)
            return;
        var runs = model.SectionMode == HistorySectionMode.Runs;
        var noRows = runs ? model.Runs.Count == 0 : model.VisibleBattles.Count == 0;
        var noBattles = runs && !noRows && model.VisibleBattles.Count == 0 && !model.PageLoading;
        _archiveEmptyLabel.text =
            noBattles ? HistoryPanelText.NoBattlesInRun()
            : noRows
                ? model.ArchiveEmptyMessage.Length > 0 ? model.ArchiveEmptyMessage
                    : model.PageLoading ? HistoryPanelText.LoadingPreview()
                    : !string.IsNullOrEmpty(model.SelectedRunHero)
                        ? HistoryPanelText.NoRunFilterMatches()
                    : HistoryPanelText.NoRuns()
            : _detailMessage;
        _archiveEmptyLabel.gameObject.SetActive(
            (noRows || noBattles || !_boardsAvailable) && _archiveEmptyLabel.text.Length > 0
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

    private Sprite? BattleBadge(HistoryBattleRecord? battle) =>
        battle == null ? null
        : HistoryPanelFormatter.IsBattleWin(battle) ? _winBadge
        : HistoryPanelFormatter.IsBattleLoss(battle) ? _lossBadge
        : null;

    private static void BindBattleResult(TextMeshProUGUI label, HistoryBattleRecord battle)
    {
        var won = HistoryPanelFormatter.IsBattleWin(battle);
        var lost = HistoryPanelFormatter.IsBattleLoss(battle);
        label.text =
            won ? HistoryPanelText.BattleWon()
            : lost ? HistoryPanelText.BattleLost()
            : HistoryPanelText.Unknown();
        label.color =
            won ? InGameTheme.Success
            : lost ? InGameTheme.Failure
            : Muted;
    }

    private bool BindRank(
        Image badge,
        TextMeshProUGUI ratingLabel,
        TextMeshProUGUI unknown,
        string? rank,
        int? rating,
        int? ratingDelta = null
    )
    {
        badge.sprite = _rankBadges.Get(rank);
        badge.color = badge.sprite != null ? Color.white : Color.clear;
        unknown.gameObject.SetActive(badge.sprite == null);
        var hasRating =
            string.Equals(rank?.Trim(), "Legendary", StringComparison.OrdinalIgnoreCase)
            && rating.HasValue;
        ratingLabel.text = hasRating
            ? HistoryPanelFormatter.RatingWithDelta(rating.GetValueOrDefault(), ratingDelta)
            : string.Empty;
        return hasRating;
    }
}
