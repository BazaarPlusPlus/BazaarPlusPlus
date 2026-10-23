using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Localization;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Ui;

internal sealed partial class AndroidHistoryPanelView
{
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
        internal (Button Button, TextMeshProUGUI Label) Control;
        internal Image Badge = null!;
        internal Image RankBadge = null!;
        internal TextMeshProUGUI Rating = null!;
        internal TextMeshProUGUI UnknownResult = null!;
        internal TextMeshProUGUI UnknownRank = null!;
        internal TextMeshProUGUI Hero = null!;
        internal RunFields Run = null!;
        internal string? Id;
    }

    // One selectable opponent in the run's timeline.
    private sealed class DayChip
    {
        internal (Button Button, TextMeshProUGUI Label) Control;
        internal Image Rank = null!;
        internal TextMeshProUGUI Unknown = null!;
        internal TextMeshProUGUI Day = null!;
        internal TextMeshProUGUI Name = null!;
        internal TextMeshProUGUI Rating = null!;
        internal TextMeshProUGUI Hero = null!;
        internal Image Outcome = null!;
    }

    private sealed class DayStrip
    {
        internal ScrollRect Scroll = null!;
        internal DayChip[] Chips = new DayChip[Rows];
        internal readonly HistoryTimelineScroll Position = new();
    }

    private sealed class PageList
    {
        internal ScrollRect Scroll = null!;
        internal Row[] Rows = new Row[AndroidHistoryPanelView.Rows];
        internal float Height;
        internal bool ResetScroll;
    }

    private PageList _archiveList = null!;
    private TextMeshProUGUI _archiveEmptyLabel = null!;
    private DayStrip _dayStrip = null!;
    private TextMeshProUGUI _title = null!,
        _statusLabel = null!,
        _pageLabel = null!,
        _opponentOwner = null!,
        _playerOwner = null!;
    private TextMeshProUGUI? _playerStatus;
    private TextMeshProUGUI? _opponentStatus;
    private (Button Button, TextMeshProUGUI Label) _runsTab,
        _ghostTab,
        _favoritesTab,
        _older,
        _newer,
        _latest,
        _battleOlder,
        _battleNewer,
        _replayButton,
        _recordButton;
    private RectTransform? _playerBoard;
    private RectTransform? _opponentBoard;
    private RectTransform _opponentTitle = null!,
        _playerTitle = null!;
    private (HistorySectionMode Section, bool BothBoards)? _boundLayout;

    private void CreateLayout()
    {
        _layout = AndroidUiKit.Rect(
            "Layout",
            _root!.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        AndroidUiKit.Fill(_layout, Backdrop);
        AndroidUiKit.Fill(Place("Panel", _layout, .014f, .018f, .972f, .962f), PanelFill, false);
        _title = Text(_layout, "", HistoryPanelLayout.Title, 34);
        Button(
            _layout,
            "close",
            HistoryPanelText.Close(),
            Touch(HistoryPanelLayout.CloseButton),
            _actions.Close
        );
        // #399: the More dialog (database status, health probe, supporters, account link).
        Place("MoreButtonSeam", _layout, HistoryPanelLayout.MoreButton);
        _runsTab = Button(
            _layout,
            "tab_runs",
            "",
            Touch(HistoryPanelLayout.RunsTab),
            () => _actions.Section(HistorySectionMode.Runs)
        );
        _ghostTab = Button(
            _layout,
            "tab_ghost",
            "",
            Touch(HistoryPanelLayout.GhostTab),
            () => _actions.Section(HistorySectionMode.Ghost)
        );
        _favoritesTab = Button(
            _layout,
            "tab_favorites",
            "",
            Touch(HistoryPanelLayout.FavoritesTab),
            () => _actions.Section(HistorySectionMode.Favorites)
        );
        // #399: the hero and Ghost outcome/day filters.
        Place("FilterStripSeam", _layout, HistoryPanelLayout.FilterStrip);
        _archiveList = CreatePageList(
            "Archive",
            HistoryPanelLayout.ArchiveList,
            HistoryPanelLayout.RunRowHeight
        );
        _archiveEmptyLabel = Text(
            _archiveList.Scroll.viewport,
            "",
            .04f,
            .04f,
            .92f,
            .8f,
            14,
            Muted
        );
        _archiveEmptyLabel.textWrappingMode = TextWrappingModes.Normal;
        _archiveEmptyLabel.overflowMode = TextOverflowModes.Overflow;
        _archiveEmptyLabel.alignment = TextAlignmentOptions.TopLeft;
        _dayStrip = CreateDayStrip();
        var archivePager = Place("ArchivePager", _layout, Touch(HistoryPanelLayout.ArchivePager));
        _newer = Button(
            archivePager,
            "archive_newer",
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
            "archive_older",
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
            "archive_latest",
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
            "timeline_newer",
            "↑",
            Touch(HistoryPanelLayout.TimelineNewer),
            () => _actions.PageBattles(-1)
        );
        _battleOlder = Button(
            _layout,
            "timeline_older",
            "↓",
            Touch(HistoryPanelLayout.TimelineOlder),
            () => _actions.PageBattles(1)
        );
        _opponentTitle = Place(
            "OpponentBoardTitle",
            _layout,
            HistoryPanelLayout.DetailLeft,
            HistoryPanelLayout.OpponentTitleTop,
            HistoryPanelLayout.DetailWidth,
            HistoryPanelLayout.BoardTitleHeight
        );
        _opponentOwner = Text(_opponentTitle, "", 0, 0, .11f, 1, 14, Muted);
        _opponentBoard = Place(
            "OpponentBoardBounds",
            _layout,
            HistoryPanelLayout.DetailLeft,
            HistoryPanelLayout.OpponentBoardTop,
            HistoryPanelLayout.DetailWidth,
            HistoryPanelLayout.OpponentBoardHeight
        );
        _opponentStatus = Text(_opponentBoard, "", 0, .4f, 1, .2f, 17, Muted, true);
        _opponentStatus.textWrappingMode = TextWrappingModes.Normal;
        _opponentStatus.overflowMode = TextOverflowModes.Overflow;
        _playerBoard = Place(
            "PlayerBoardBounds",
            _layout,
            HistoryPanelLayout.DetailLeft,
            HistoryPanelLayout.PlayerBoardTop,
            HistoryPanelLayout.DetailWidth,
            HistoryPanelLayout.PlayerBoardHeight
        );
        _playerStatus = Text(_playerBoard, "", 0, .35f, 1, .3f, 17, Muted, true);
        _playerStatus.textWrappingMode = TextWrappingModes.Normal;
        _playerStatus.overflowMode = TextOverflowModes.Overflow;
        // The lower board's ownership title. In Ghost it is the only board and belongs to the
        // challenger (ADR-0002), so the wording follows the section.
        _playerTitle = Place(
            "PlayerBoardTitle",
            _layout,
            HistoryPanelLayout.DetailLeft,
            HistoryPanelLayout.PlayerTitleTop,
            HistoryPanelLayout.DetailWidth,
            HistoryPanelLayout.BoardTitleHeight
        );
        _playerOwner = Text(_playerTitle, "", 0, 0, .3f, 1, 14, Muted);
        _statusLabel = Text(_layout, "", HistoryPanelLayout.StatusLabel, 12, Muted);
        // #399: the delete and favorite actions.
        Place("DeleteButtonSeam", _layout, HistoryPanelLayout.DeleteButton);
        _recordButton = Button(
            _layout,
            "record",
            "",
            Touch(HistoryPanelLayout.RecordButton),
            static () => { }
        );
        _replayButton = Button(
            _layout,
            "replay",
            "",
            Touch(HistoryPanelLayout.ReplayButton),
            _actions.Replay,
            true,
            23
        );
    }

    private void NavigateArchive(int direction)
    {
        _archiveList.ResetScroll = true;
        _actions.PageArchive(direction);
    }

    private PageList CreatePageList(string name, HistoryRect bounds, float rowHeight)
    {
        var viewport = Place(name, _layout!, bounds);
        viewport.gameObject.AddComponent<RectMask2D>();
        AndroidUiKit.Fill(viewport, Color.clear);
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 45;
        var content = Place("Rows", viewport, 0, 0, 1, 1);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        scroll.content = content;
        var list = new PageList { Scroll = scroll, Height = rowHeight };
        for (var i = 0; i < list.Rows.Length; i++)
        {
            var index = i;
            var control = Button(
                content,
                "archive_row",
                "",
                0,
                0,
                1,
                1,
                () =>
                {
                    if (_model?.SectionMode == HistorySectionMode.Runs)
                        _actions.SelectRun(index);
                    else
                        _actions.SelectBattle(index);
                },
                size: 14
            );
            var rect = control.Button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 1);
            rect.sizeDelta = new Vector2(0, rowHeight - 5);
            rect.anchoredPosition = new Vector2(0, -i * rowHeight);
            var label = control.Label;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.rectTransform.anchorMin = new Vector2(.26f, .06f);
            label.rectTransform.anchorMax = new Vector2(.77f, .94f);
            // The desktop draws the hero portrait here; the host shows the hero's name.
            var hero = Wrapped(Text(rect, "", .025f, .11f, .21f, .70f, 10, Muted, true));
            var badge = Badge(rect, "Outcome", .82f, .71f, .14f, .25f);
            var rank = Badge(rect, "Rank", .80f, .05f, .18f, .45f);
            var unknownResult = Text(badge.transform, "?", 0, 0, 1, 1, 14, Muted, true);
            var unknownRank = Text(rank.transform, "?", 0, 0, 1, 1, 11, Muted, true);
            var rating = Text(rect, "", .77f, .49f, .22f, .18f, 10, Ink, true);
            var run = new RunFields
            {
                Name = Wrapped(Text(rect, "", .245f, .07f, .425f, .28f, 15)),
                Meta = Wrapped(Text(rect, "", .245f, .39f, .425f, .22f, 11, Muted)),
                Stamp = Wrapped(Text(rect, "", .245f, .68f, .425f, .22f, 11, Muted)),
            };
            list.Rows[i] = new Row
            {
                Control = control,
                Badge = badge,
                RankBadge = rank,
                Rating = rating,
                UnknownResult = unknownResult,
                UnknownRank = unknownRank,
                Hero = hero,
                Run = run,
            };
            control.Button.gameObject.SetActive(false);
        }
        return list;
    }

    private DayStrip CreateDayStrip()
    {
        var viewport = Place(
            "BattleTimeline",
            _layout!,
            HistoryPanelLayout.TimelineLeft,
            HistoryPanelLayout.TimelineTop,
            HistoryPanelLayout.TimelineWidth,
            HistoryPanelLayout.TimelineHeight
        );
        viewport.gameObject.AddComponent<RectMask2D>();
        AndroidUiKit.Fill(viewport, Color.clear);
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        // A run has no hard battle cap, so the rail scrolls and pages.
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 45;
        var content = Place("Chips", viewport, 0, 0, 1, 1);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        scroll.content = content;
        var strip = new DayStrip { Scroll = scroll };
        for (var i = 0; i < strip.Chips.Length; i++)
        {
            var slot = i;
            var control = Button(
                content,
                "timeline_chip",
                "",
                0,
                0,
                1,
                1,
                () => _actions.SelectBattle(slot),
                size: 11
            );
            var rect = control.Button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 1);
            rect.sizeDelta = new Vector2(0, HistoryPanelLayout.DayChipHeight - 5);
            rect.anchoredPosition = new Vector2(0, -i * HistoryPanelLayout.DayChipHeight);
            control.Label.gameObject.SetActive(false);
            var hero = Wrapped(Text(rect, "", .06f, .10f, .28f, .49f, 9, Muted, true));
            var day = Wrapped(Text(rect, "", .35f, .18f, .30f, .28f, 11, Ink, true));
            var rank = Badge(rect, "OpponentRank", .70f, .08f, .24f, .36f);
            var unknown = Text(rank.transform, "?", 0, 0, 1, 1, 9, Muted, true);
            var rating = Wrapped(Text(rect, "", .66f, .41f, .32f, .24f, 10, Ink, true));
            rating.overflowMode = TextOverflowModes.Overflow;
            var name = Wrapped(Text(rect, "", .08f, .66f, .84f, .20f, 10, Ink, true));
            var outcome = Place("Outcome", rect, .08f, .90f, .84f, 0)
                .gameObject.AddComponent<Image>();
            outcome.rectTransform.sizeDelta = new Vector2(0, HistoryPanelLayout.DayChipOutcomeBar);
            outcome.raycastTarget = false;
            outcome.color = Color.clear;
            strip.Chips[i] = new DayChip
            {
                Control = control,
                Rank = rank,
                Unknown = unknown,
                Day = day,
                Name = name,
                Rating = rating,
                Hero = hero,
                Outcome = outcome,
            };
            control.Button.gameObject.SetActive(false);
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
            var shown = i < battles.Count;
            if (chip.Control.Button.gameObject.activeSelf != shown)
                chip.Control.Button.gameObject.SetActive(shown);
            if (!shown)
                continue;
            // Keep the page's newest-first order, matching the archive and pager arrows.
            var battle = battles[i];
            SetButton(chip.Control, string.Empty, i == selected, !_model!.PageLoading);
            var hasRating = BindRank(
                chip.Rank,
                chip.Rating,
                chip.Unknown,
                battle.OpponentRank,
                battle.OpponentRating
            );
            Anchor(
                chip.Rank.rectTransform,
                HistoryPanelLayout.Anchors(.70f, hasRating ? .08f : .185f, .24f, .36f)
            );
            chip.Day.text = HistoryPanelText.DayBadge(battle.Day);
            chip.Name.text = string.IsNullOrWhiteSpace(battle.OpponentName)
                ? HistoryPanelText.UnknownOpponent()
                : battle.OpponentName;
            chip.Hero.text = HeroLabel(battle.OpponentHero);
            chip.Outcome.color =
                HistoryPanelFormatter.IsBattleWin(battle) ? new Color(.62f, .80f, .54f, 1f)
                : HistoryPanelFormatter.IsBattleLoss(battle) ? new Color(.90f, .40f, .31f, 1f)
                : Color.clear;
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
    }

    // Archive rows carry runs in Runs and battles in Ghost; the badges move to suit either.
    private void ApplyArchiveRowMode(bool runs)
    {
        _archiveList.Height = runs
            ? HistoryPanelLayout.RunRowHeight
            : HistoryPanelLayout.GhostRowHeight;
        for (var i = 0; i < _archiveList.Rows.Length; i++)
        {
            var row = _archiveList.Rows[i];
            var rect = row.Control.Button.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, _archiveList.Height - 5);
            rect.anchoredPosition = new Vector2(0, -i * _archiveList.Height);
            row.Run.SetActive(runs);
            row.Control.Label.gameObject.SetActive(!runs);
            Anchor(
                row.Hero.rectTransform,
                runs
                    ? HistoryPanelLayout.Anchors(.025f, .14f, .20f, .72f)
                    : HistoryPanelLayout.Anchors(.025f, .11f, .21f, .70f)
            );
            Anchor(
                row.Badge.rectTransform,
                runs
                    ? HistoryPanelLayout.Anchors(.84f, .25f, .13f, .48f)
                    : HistoryPanelLayout.Anchors(.82f, .71f, .14f, .25f)
            );
            Anchor(
                row.Rating.rectTransform,
                runs
                    ? HistoryPanelLayout.Anchors(.66f, .66f, .17f, .20f)
                    : HistoryPanelLayout.Anchors(.77f, .49f, .22f, .18f)
            );
        }
    }

    private void BindRows(
        PageList list,
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
        var offset = Mathf.Max(0, list.Scroll.content.anchoredPosition.y);
        var oldTop = Math.Min(Rows - 1, (int)(offset / list.Height));
        var topId = list.Rows[oldTop].Id;
        var newTop = -1;
        for (var i = 0; i < list.Rows.Length; i++)
        {
            var row = list.Rows[i];
            var shown = i < ids.Count;
            if (row.Control.Button.gameObject.activeSelf != shown)
                row.Control.Button.gameObject.SetActive(shown);
            if (!shown)
            {
                row.Id = null;
                continue;
            }
            row.Id = ids[i];
            if (row.Id == topId)
                newTop = i;
            SetButton(
                row.Control,
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
            var outcome = badge(i);
            row.Badge.sprite = outcome.Sprite;
            row.Badge.color = outcome.Sprite != null ? Color.white : Color.clear;
            row.UnknownResult.text = outcome.Fallback;
            row.UnknownResult.gameObject.SetActive(outcome.Sprite == null);
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
            row.Hero.text = HeroLabel(hero(i));
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
    }

    private void UpdateContent()
    {
        if (_layout == null || _model is not { } m)
            return;
        var runs = m.SectionMode == HistorySectionMode.Runs;
        var ghost = m.SectionMode == HistorySectionMode.Ghost;
        var both = m.ShowsBothBoards;
        if (_boundLayout != (m.SectionMode, both))
        {
            if (_boundLayout?.Section != m.SectionMode)
            {
                _archiveList.ResetScroll = true;
                ApplyArchiveRowMode(runs);
            }
            _boundLayout = (m.SectionMode, both);
            _layoutFrames = 2;
            // One board (Ghost) drops the upper one, so the surviving title follows it up.
            Anchor(
                _playerTitle,
                HistoryPanelLayout.Anchors(
                    HistoryPanelLayout.DetailLeft,
                    both ? HistoryPanelLayout.PlayerTitleTop : HistoryPanelLayout.GhostTitleTop,
                    HistoryPanelLayout.DetailWidth,
                    HistoryPanelLayout.BoardTitleHeight
                )
            );
            Anchor(
                _playerBoard!,
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
        SetActive(_favoritesTab.Button.gameObject, m.FavoritesTabVisible);
        SetButton(
            _favoritesTab,
            HistoryPanelText.FavoritesTab(),
            m.SectionMode == HistorySectionMode.Favorites
        );
        SetActive(_dayStrip.Scroll.gameObject, runs);
        SetActive(_opponentBoard!.gameObject, both);
        SetActive(_opponentTitle.gameObject, both);
        _opponentOwner.text = HistoryPanelText.BoardOpponent();
        // Two boards show yours below the opponent's; one board is the challenger's.
        _playerOwner.text = both ? HistoryPanelText.BoardYou() : HistoryPanelText.BoardChallenger();
        SetActive(_battleNewer.Button.gameObject, runs);
        SetActive(_battleOlder.Button.gameObject, runs);
        _newer.Button.interactable = m.HasNewer && !m.PageLoading;
        _older.Button.interactable = m.HasOlder && !m.PageLoading;
        _latest.Button.interactable = !m.PageLoading;
        _battleNewer.Button.interactable = m.BattleHasNewer;
        _battleOlder.Button.interactable = m.BattleHasOlder;
        _pageLabel.text = m.PageLabel;
        _archiveEmptyLabel.text = m.ArchiveEmptyMessage;
        SetActive(_archiveEmptyLabel.gameObject, m.ArchiveEmptyMessage.Length > 0);
        if (runs)
            BindRows(
                _archiveList,
                m.Runs.Select(r => r.RunId).ToArray(),
                _ => string.Empty,
                i => m.Runs[i].Hero,
                i => RunBadge(m.Runs[i]),
                i => m.Runs[i].PlayerRank,
                i => m.Runs[i].PlayerRating,
                i => m.Runs[i].PlayerRatingDelta,
                m.SelectedRunIndex,
                i => HistoryPanelFormatter.RunRowFields(m.Runs[i], HeroLabel(m.Runs[i].Hero))
            );
        else
            BindRows(
                _archiveList,
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
            BindDayStrip(m.VisibleBattles, m.SelectedBattleIndex);
        _statusLabel.text = m.StatusMessage ?? "";
        _statusLabel.color = StatusColor(m.StatusSeverity);
        SetButton(
            _recordButton,
            m.RecordAndReplayButtonText,
            enabled: m.RecordAndReplayButtonEnabled && !m.PageLoading
        );
        SetButton(_replayButton, m.ReplayButtonText, true, m.ReplayButtonEnabled && !m.PageLoading);
    }

    // A finished run shows its outcome tier; the desktop's trophies are native art the host does
    // not load yet, so the tier renders as text. Unfinished runs keep the BPP state badges.
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

    // The desktop's rank badges are native art; the host shows the rank's name.
    private static bool BindRank(
        Image badge,
        TextMeshProUGUI ratingLabel,
        TextMeshProUGUI unknown,
        string? rank,
        int? rating,
        int? ratingDelta = null
    )
    {
        badge.sprite = null;
        badge.color = Color.clear;
        unknown.text = string.IsNullOrWhiteSpace(rank) ? "?" : rank.Trim();
        unknown.gameObject.SetActive(true);
        var hasRating =
            string.Equals(rank?.Trim(), "Legendary", StringComparison.OrdinalIgnoreCase)
            && rating.HasValue;
        ratingLabel.text = hasRating
            ? HistoryPanelFormatter.RatingWithDelta(rating.GetValueOrDefault(), ratingDelta)
            : string.Empty;
        return hasRating;
    }

    // The desktop resolves the game's own hero names; the host shows the stored id, with The
    // Dragons' aliases under their English name.
    private static string HeroLabel(string? hero) =>
        TheDragonsHeroId.IsAlias(hero) ? "The Dragons" : hero ?? string.Empty;

    private static string T(string english, string chinese) =>
        L.Resolve(new LocalizedTextSet(english, chinese));

    private static void SetActive(GameObject target, bool active)
    {
        if (target.activeSelf != active)
            target.SetActive(active);
    }
}
