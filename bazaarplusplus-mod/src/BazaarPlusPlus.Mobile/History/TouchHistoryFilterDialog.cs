using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.History;

/// <summary>The coordinator's existing filters, exposed as touch targets in one scrollable sheet.</summary>
internal sealed class TouchHistoryFilterDialog : IHistoryOverlay
{
    private readonly GameObject _root,
        _heroes,
        _ghost;
    private readonly Dictionary<string, HistoryButton> _heroButtons = new();
    private readonly Dictionary<GhostBattleFilter, HistoryButton> _outcomes = new();
    private readonly HistoryButton _day;
    private readonly UnityEngine.UI.ScrollRect _scroll;
    private readonly float _heroHeight,
        _ghostHeight;

    public TouchHistoryFilterDialog(
        RectTransform parent,
        TMP_FontAsset font,
        HistoryTouchLayout layout,
        TouchHistoryPanelView.ViewActions actions
    )
    {
        var shade = TouchHistoryUi.Overlay(
            parent,
            "Filters",
            "filters_outside",
            () => Close("outside_tap")
        );
        _root = shade.gameObject;
        TouchHistoryUi.Panel(shade, "FilterDialog", layout.Dialog);
        var title = TouchHistoryUi.Label(
            shade,
            font,
            "Title",
            layout.DialogTitle,
            layout.TitleFont
        );
        title.text = HistoryPanelText.Filters();
        TouchHistoryUi.Button(
            shade,
            font,
            "filters_close",
            layout.DialogClose,
            layout.ButtonFont,
            () => Close("close_button"),
            HistoryPanelText.Close()
        );
        var (scroll, content) = TouchHistoryUi.Scroll(
            shade,
            "FiltersScroll",
            layout.MoreScroll,
            false
        );
        _scroll = scroll;
        var target = layout.Metrics.MinTarget;
        var gap = layout.Gap;
        var width = layout.MoreScroll.Width;
        var columns = 2;
        var choices = new[] { string.Empty }
            .Concat(HistoryPanelHeroPresentation.RunFilterHeroIds)
            .ToArray();
        _heroHeight = (int)Math.Ceiling(choices.Length / (float)columns) * (target + gap);
        _ghostHeight = 4f * (target + gap);
        _heroes = TouchScreen.Child("Heroes", content, new(0, 0, width, _heroHeight)).gameObject;
        _ghost = TouchScreen.Child("Battles", content, new(0, 0, width, _ghostHeight)).gameObject;
        for (var i = 0; i < choices.Length; i++)
        {
            var hero = choices[i];
            var cellWidth = (width - gap) / columns;
            _heroButtons[hero] = TouchHistoryUi.Button(
                _heroes.transform,
                font,
                "filter_hero",
                new(
                    (i % columns) * (cellWidth + gap),
                    (i / columns) * (target + gap),
                    cellWidth,
                    target
                ),
                layout.ButtonFont,
                () =>
                {
                    actions.HeroFilter(hero);
                    Close("selected");
                }
            );
        }
        foreach (
            var filter in new[]
            {
                GhostBattleFilter.All,
                GhostBattleFilter.IWon,
                GhostBattleFilter.ILost,
            }
        )
        {
            var cell = (width - 2f * gap) / 3f;
            _outcomes[filter] = TouchHistoryUi.Button(
                _ghost.transform,
                font,
                "filter_outcome",
                new((int)filter * (cell + gap), 0, cell, target),
                layout.ButtonFont,
                () => actions.OutcomeFilter(filter)
            );
        }
        _day = TouchHistoryUi.Button(
            _ghost.transform,
            font,
            "filter_day",
            new(0, target + gap, width, target),
            layout.ButtonFont,
            actions.ToggleDayFilter
        );
        var note = TouchHistoryUi.Label(
            _ghost.transform,
            font,
            "CloudRetention",
            new(0, 2f * (target + gap), width, target),
            layout.BodyFont,
            color: TouchHistoryUi.Muted,
            wrap: true
        );
        note.text = HistoryPanelText.CloudWindow();
        _root.SetActive(false);
    }

    public bool IsOpen => _root.activeSelf;

    public void Open() => _root.SetActive(true);

    public bool Close(string reason)
    {
        var wasOpen = IsOpen;
        _root.SetActive(false);
        return wasOpen;
    }

    public void Bind(HistoryPanelViewModel model)
    {
        var runs = model.SectionMode == HistorySectionMode.Runs;
        _heroes.SetActive(runs);
        _ghost.SetActive(!runs);
        _scroll.content.sizeDelta = new Vector2(
            _scroll.content.sizeDelta.x,
            runs ? _heroHeight : _ghostHeight
        );
        foreach (var (hero, button) in _heroButtons)
            button.Set(
                string.IsNullOrEmpty(hero)
                    ? HistoryPanelText.AllHeroes()
                    : TouchHistoryPanelView.HeroLabel(hero),
                string.IsNullOrEmpty(hero)
                    ? string.IsNullOrEmpty(model.SelectedRunHero)
                    : HistoryPanelHeroPresentation.IsSelected(model.SelectedRunHero, hero),
                !model.PageLoading
            );
        foreach (var (filter, button) in _outcomes)
            button.Set(
                HistoryPanelText.OutcomeFilter(filter),
                model.GhostBattleFilter == filter,
                !model.PageLoading
            );
        _day.Set(HistoryPanelText.FilterDayMin10(), model.GhostDayMin10, !model.PageLoading);
    }
}
