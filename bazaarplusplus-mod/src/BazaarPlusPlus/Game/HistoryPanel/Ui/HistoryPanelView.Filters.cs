#nullable enable
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private readonly List<(string Hero, KitButton Option)> _heroOptions = new();
    private KitModal? _heroMenu;

    // The hero filter's menu: a floating card under the select, one row for each hero.
    private void CreateHeroMenu(DesktopKit kit)
    {
        _heroMenu = kit.Modal(
            _layout!,
            "HeroFilterMenu",
            RailWidth,
            InGameTheme.ScrimLight,
            () => _heroMenu!.Root.SetActive(false)
        );
        var card = _heroMenu.Card;
        var group = card.GetComponent<VerticalLayoutGroup>();
        var inset = (int)InGameSizes.Space6;
        group.padding = new RectOffset(inset, inset, inset, inset);
        group.spacing = InGameSizes.Space2;
        card.pivot = new Vector2(0, 1);
        foreach (
            var hero in new[] { string.Empty }.Concat(HistoryPanelHeroPresentation.RunFilterHeroIds)
        )
        {
            var choice = hero;
            var option = kit.Button(
                card,
                string.Empty,
                KitButtonKind.Row,
                KitSize.Md,
                () =>
                {
                    _heroFilter(choice);
                    _heroMenu.Root.SetActive(false);
                }
            );
            option.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            _heroOptions.Add((hero, option));
        }
    }

    private void OpenHeroMenu()
    {
        if (_heroMenu == null)
            return;
        var select = (RectTransform)_heroSelect.transform;
        select.GetWorldCorners(_corners);
        _heroMenu.Root.SetActive(true);
        var card = _heroMenu.Card;
        card.sizeDelta = new Vector2(select.rect.width, card.sizeDelta.y);
        card.position = _corners[0];
        card.anchoredPosition -= new Vector2(0, InGameSizes.Space4);
    }

    private void BindHeroMenu(HistoryPanelViewModel m)
    {
        foreach (var (hero, option) in _heroOptions)
        {
            var all = string.IsNullOrEmpty(hero);
            option.Label = all
                ? HistoryPanelText.AllHeroes()
                : HistoryPanelHeroNames.DisplayName(hero);
            option.On = all
                ? string.IsNullOrEmpty(m.SelectedRunHero)
                : HistoryPanelHeroPresentation.IsSelected(m.SelectedRunHero, hero);
        }
    }
}
