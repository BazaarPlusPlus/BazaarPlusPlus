using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;

namespace BazaarPlusPlus.Mobile.History;

/// <summary>
/// The coordinator's existing filters as touch targets, one headed group per filter. The dialog is
/// as tall as the open section's groups and scrolls only when the sheet is shorter.
/// </summary>
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
    private readonly HistoryTouchLayout _layout;
    private readonly RectTransform _panel;
    private bool? _boundRuns;

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
        _layout = layout;
        _panel = TouchHistoryUi.Panel(shade, "FilterDialog", layout.Dialog);
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
            layout.TitleFont,
            () => Close("close_button"),
            UiKit.CloseGlyph
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
        var heading = layout.Metrics.Dp(28f);
        var choices = new[] { string.Empty }
            .Concat(HistoryPanelHeroPresentation.RunFilterHeroIds)
            .ToArray();
        var heroRows = (int)Math.Ceiling(choices.Length / (float)columns);
        _heroHeight = heading + heroRows * (target + gap) - gap;
        // Result: heading and three choices. Day: heading, the toggle, and the cloud note.
        _ghostHeight = 2f * heading + 3f * (target + gap) - gap;
        _heroes = TouchScreen.Child("Heroes", content, new(0, 0, width, _heroHeight)).gameObject;
        _ghost = TouchScreen.Child("Battles", content, new(0, 0, width, _ghostHeight)).gameObject;
        Heading(_heroes.transform, font, layout, new(0, 0, width, heading)).text =
            HistoryPanelText.FilterHero();
        Heading(_ghost.transform, font, layout, new(0, 0, width, heading)).text =
            HistoryPanelText.FilterOutcome();
        Heading(
            _ghost.transform,
            font,
            layout,
            new(0, heading + target + gap, width, heading)
        ).text = HistoryPanelText.FilterDay();
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
                    heading + (i / columns) * (target + gap),
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
                new((int)filter * (cell + gap), heading, cell, target),
                layout.ButtonFont,
                () => actions.OutcomeFilter(filter)
            );
        }
        _day = TouchHistoryUi.Button(
            _ghost.transform,
            font,
            "filter_day",
            new(0, 2f * heading + target + gap, width, target),
            layout.ButtonFont,
            actions.ToggleDayFilter
        );
        var note = TouchHistoryUi.Label(
            _ghost.transform,
            font,
            "CloudRetention",
            new(0, 2f * heading + 2f * (target + gap), width, target),
            layout.BodyFont,
            color: TouchHistoryUi.Muted,
            wrap: true
        );
        note.text = HistoryPanelText.CloudWindow();
        _root.SetActive(false);
    }

    private static TextMeshProUGUI Heading(
        Transform parent,
        TMP_FontAsset font,
        HistoryTouchLayout layout,
        TouchRect area
    ) =>
        TouchHistoryUi.Label(
            parent,
            font,
            "Heading",
            area,
            layout.BodyFont,
            color: TouchHistoryUi.Muted
        );

    // Shrinks the full-height dialog to its content; it keeps its top, title and close.
    private void Fit(float content)
    {
        var dialog = _layout.Dialog;
        var scroll = _layout.MoreScroll;
        var bottom = dialog.Top + dialog.Height - (scroll.Top + scroll.Height);
        var height = Math.Min(scroll.Height, content);
        TouchScreen.Place(
            _panel,
            dialog with
            {
                Height = scroll.Top - dialog.Top + height + bottom,
            }
        );
        // A cast of an IL2CPP Transform fails at run time; the component lookup does not.
        TouchScreen.Place(_scroll.GetComponent<RectTransform>(), scroll with { Height = height });
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
        var height = runs ? _heroHeight : _ghostHeight;
        _scroll.content.sizeDelta = new Vector2(_scroll.content.sizeDelta.x, height);
        if (_boundRuns != runs)
        {
            _boundRuns = runs;
            Fit(height);
        }
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
