#nullable enable
using BazaarGameShared.Domain.Core.Types;
using HarmonyLib;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.UI.MainMenu;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.Lobby;

internal sealed class NativeLobbyHeroSelection
{
    private static readonly System.Reflection.FieldInfo? HeroSelectField = AccessTools.Field(
        typeof(MainMenuBaseView),
        "_heroSelectButton"
    );
    private static readonly System.Reflection.FieldInfo? LoadoutField = AccessTools.Field(
        typeof(MainMenuBaseView),
        "_loadoutButton"
    );
    private static readonly Func<MainMenuBaseView, bool>? ReadGamepad =
        AccessTools
            .PropertyGetter(typeof(MainMenuBaseView), "IsGamepad")
            ?.CreateDelegate(typeof(Func<MainMenuBaseView, bool>)) as Func<MainMenuBaseView, bool>;

    private readonly MainMenuBaseView _view;
    private readonly BazaarButtonController _heroSelect;
    private readonly RectTransform _loadout;
    private CanvasGroup? _heroSelectMask;
    private Vector2 _loadoutPosition;

    private NativeLobbyHeroSelection(
        MainMenuBaseView view,
        BazaarButtonController heroSelect,
        RectTransform loadout,
        RectTransform parent
    )
    {
        _view = view;
        _heroSelect = heroSelect;
        _loadout = loadout;
        Parent = parent;
    }

    internal RectTransform Parent { get; }
    internal bool Exists => _view != null && _heroSelect != null && _loadout != null;
    internal bool CanShow =>
        Exists
        && _view.isActiveAndEnabled
        && _heroSelect.gameObject.activeInHierarchy
        && !(ReadGamepad?.Invoke(_view) ?? false);

    internal static bool IsPopupOpen =>
        Services.TryGet<PopupManager>(out var popup)
        && popup != null
        && (popup.IsAnyPopupOpen || popup.IsSettingsOpen);

    internal static NativeLobbyHeroSelection? TryBind()
    {
        var view = UnityEngine.Object.FindFirstObjectByType<MainMenuBaseView>();
        if (
            view == null
            || HeroSelectField?.GetValue(view) is not BazaarButtonController heroSelect
            || heroSelect == null
            || LoadoutField?.GetValue(view) is not BazaarButtonController loadout
            || loadout == null
            || loadout.transform is not RectTransform loadoutRect
            || heroSelect.transform.parent is not RectTransform parent
            || loadoutRect.parent != parent
        )
            return null;
        return new NativeLobbyHeroSelection(view, heroSelect, loadoutRect, parent);
    }

    internal void Attach(float loadoutRise)
    {
        _loadoutPosition = _loadout.anchoredPosition;
        _heroSelectMask = _heroSelect.gameObject.AddComponent<CanvasGroup>();
        _heroSelectMask.alpha = 0;
        _heroSelectMask.blocksRaycasts = false;
        _heroSelectMask.interactable = false;
        _loadout.anchoredPosition = _loadoutPosition + Vector2.up * loadoutRise;
    }

    internal void Detach()
    {
        if (_heroSelectMask == null)
            return;
        if (_loadout != null)
            _loadout.anchoredPosition = _loadoutPosition;
        _heroSelectMask.alpha = 1;
        _heroSelectMask.blocksRaycasts = true;
        _heroSelectMask.interactable = true;
        UnityEngine.Object.Destroy(_heroSelectMask);
        _heroSelectMask = null;
    }

    internal static bool IsHeroInRandomPool(EHero hero) =>
        PlayerPreferences.Data.HeroesExcludedFromRandom.TryGetValue(
            hero.ToString(),
            out var excluded
        ) && !excluded;

    internal static void SetHeroInRandomPool(EHero hero, bool included) =>
        PlayerPreferences.Data.SetHeroExcludedFromRandom(hero, !included);

    internal static void SetRandom(bool enabled)
    {
        PlayerPreferences.Data.RandomHeroEnabled = enabled;
        TheBazaar.Events.HeroChanged.Trigger(Data.SelectedHero);
    }
}
