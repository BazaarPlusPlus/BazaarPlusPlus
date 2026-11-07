#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelView
{
    private static void CreateFilterChevron(Transform parent)
    {
        for (var i = 0; i < 2; i++)
        {
            var stroke = new GameObject("Chevron", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)stroke.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.92f, .5f);
            rect.sizeDelta = new Vector2(8f, 1.5f);
            rect.anchoredPosition = new Vector2(i == 0 ? -2.5f : 2.5f, 0f);
            rect.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -45f : 45f);
            var image = stroke.GetComponent<Image>();
            image.color = InGameTheme.InkMuted;
            image.raycastTarget = false;
        }
    }

    private void CreateHeroFilterMenu()
    {
        var shade = CreateRect("HeroFilterMenu", _layout!, 0, 0, 1, 1);
        var canvas = shade.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = BppOverlaySorting.PanelForeground;
        shade.gameObject.AddComponent<GraphicRaycaster>();
        shade.gameObject.AddComponent<Image>().color = InGameTheme.ScrimLight;
        var dismiss = shade.gameObject.AddComponent<Button>();
        dismiss.transition = Selectable.Transition.None;
        dismiss.onClick.AddListener(() => shade.gameObject.SetActive(false));
        var menu = CreateRect("Heroes", shade, .033f, .23f, .204f, .52f);
        ControlSurface(menu);
        var choices = new[] { string.Empty }
            .Concat(HistoryPanelHeroPresentation.RunFilterHeroIds)
            .ToArray();
        for (var i = 0; i < choices.Length; i++)
        {
            var hero = choices[i];
            _heroes[hero] = Button(
                menu,
                "",
                0,
                i / (float)choices.Length,
                1,
                1f / choices.Length,
                () =>
                {
                    _heroFilter(hero);
                    shade.gameObject.SetActive(false);
                },
                size: 16,
                style: ButtonStyle.GroupedOption
            );
            ButtonLabel(_heroes[hero]).alignment = TextAlignmentOptions.MidlineLeft;
        }
        _filterRoot = shade.gameObject;
        _filterRoot.SetActive(false);
    }
}
