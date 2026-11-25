#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.Lobby;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.DesktopKit;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using TheBazaar;
using TheBazaar.AppFramework;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Lobby;

internal sealed class ClassicHeroSelectionController : MonoBehaviour
{
    private const float GlowTransitionSeconds = .14f;
    private const float GlowHiddenScale = .95f;

    private static readonly (EHero Hero, float X, float Y)[] Slots =
    [
        (EHero.Pygmalien, 164f, -120f),
        (EHero.Mak, 492f, -120f),
        (EHero.Karnok, 797f, -120f),
        (EHero.Vanessa, 350f, -423f),
        (EHero.Stelle, 670f, -423f),
        (EHero.Dooley, 164f, -760f),
        (EHero.Jules, 492f, -750f),
        (EHero.Hero8, 797f, -750f),
    ];

    private static readonly LocalizedTextSet RandomLabel = new(
        "Random hero",
        "随机英雄",
        "隨機英雄"
    );

    private readonly List<HeroTile> _tiles = new();
    private BppSettings? _config;
    private NativeLobbyHeroSelection? _native;
    private Task<NativeLobbyHeroArt?>? _artLoad;
    private bool _artUnavailable;
    private RectTransform? _root;
    private CanvasGroup? _controls;
    private RectTransform? _backdrop;
    private Material? _labelMaterial;
    private Button? _randomButton;
    private Image? _randomMark;
    private TextMeshProUGUI? _randomLabel;
    private float _nextRefresh;

    internal void Initialize(BppSettings config) => _config = config;

    private void LateUpdate()
    {
        if (
            _config?.EnableClassicHeroSelectionConfig.Value != true
            || SceneLoader.ActiveScene != SceneID.HeroSelectScene
            || Data.HasActiveRun
        )
        {
            RemovePanel();
            return;
        }

        if (_controls != null)
        {
            var input = !NativeLobbyHeroSelection.IsPopupOpen && !SceneLoader.IsTransitioning;
            _controls.interactable = input;
            _controls.blocksRaycasts = input;
            AnimateGlows();
        }

        if (_native?.Exists != true)
        {
            RemovePanel();
            _native = NativeLobbyHeroSelection.TryBind();
        }
        if (_native?.CanShow != true)
        {
            RemovePanel();
            ReadyArt();
            return;
        }

        if (_root != null)
        {
            _native.EnsureLoadoutLayout();
            if (Time.unscaledTime < _nextRefresh)
                return;
        }

        if (!Services.TryGet<HeroManager>(out var heroManager) || heroManager == null)
        {
            RemovePanel();
            return;
        }

        var art = ReadyArt();
        if (art == null)
            return;
        var justBuilt = false;
        if (_root == null)
        {
            BuildPanel(_native, heroManager, art);
            if (_root == null)
                return;
            if (PlayerPreferences.Data.RandomHeroEnabled && !_tiles.Any(IsRandomCandidate))
                IncludeAllOwnedHeroes();
            justBuilt = true;
        }

        _nextRefresh = Time.unscaledTime + .2f;

        var random = PlayerPreferences.Data.RandomHeroEnabled;
        var selected = Data.SelectedHero;
        var hasOwnedHero = false;
        foreach (var tile in _tiles)
        {
            var owned = HeroManager.OwnsHero(tile.Hero);
            hasOwnedHero |= owned;
            tile.Icon.canvasRenderer.SetAlpha(owned ? 1f : .8f);
            var highlighted = random
                ? owned && NativeLobbyHeroSelection.IsHeroInRandomPool(tile.Hero)
                : selected == tile.Hero;
            tile.GlowTargetVisible = highlighted;
            if (justBuilt)
                ApplyGlowImmediate(tile, highlighted);
            tile.NamePlate.gameObject.SetActive(highlighted);
            if (highlighted)
                tile.Name.text = heroManager.GetHeroName(tile.Hero);
        }

        _randomLabel!.text = RandomLabel.Resolve(L.CurrentLanguageCode, L.CurrentMode);
        _randomMark!.gameObject.SetActive(random);
        _randomButton!.interactable = hasOwnedHero;
    }

    private static void ApplyGlowImmediate(HeroTile tile, bool visible)
    {
        var progress = visible ? 1f : 0f;
        tile.GlowProgress = progress;
        var glow = tile.Glow;
        glow.gameObject.SetActive(visible);
        glow.rectTransform.localScale = Vector3.one * (visible ? 1f : GlowHiddenScale);
        glow.canvasRenderer.SetAlpha(progress);
    }

    private void AnimateGlows()
    {
        var step = Time.unscaledDeltaTime / GlowTransitionSeconds;
        foreach (var tile in _tiles)
        {
            var target = tile.GlowTargetVisible ? 1f : 0f;
            var progress = Mathf.MoveTowards(tile.GlowProgress, target, step);
            if (progress == tile.GlowProgress)
                continue;

            tile.GlowProgress = progress;
            var eased = Mathf.SmoothStep(0f, 1f, progress);
            var glow = tile.Glow;
            if (progress > 0f && !glow.gameObject.activeSelf)
                glow.gameObject.SetActive(true);
            glow.rectTransform.localScale = Vector3.one * Mathf.Lerp(GlowHiddenScale, 1f, eased);
            glow.canvasRenderer.SetAlpha(eased);
            if (progress == 0f)
                glow.gameObject.SetActive(false);
        }
    }

    private NativeLobbyHeroArt? ReadyArt()
    {
        if (_artUnavailable)
            return null;
        if (_artLoad == null && Services.TryGet<AssetLoader>(out var loader) && loader != null)
            _artLoad = NativeLobbyHeroArt.Load(loader, Slots.Select(slot => slot.Hero).ToArray());

        if (_artLoad?.IsCompleted != true)
            return null;
        if (_artLoad.Status == TaskStatus.RanToCompletion && _artLoad.Result is { } art)
            return art;

        _artUnavailable = true;
        var entry = new BppLogEvent(
            BppLogFeatureScope.Lobby,
            "lobby.classic_heroes.art_unavailable"
        );
        if (_artLoad.Exception is { } error)
            BppLog.WarnEvent(entry, error);
        else
            BppLog.WarnEvent(entry);
        return null;
    }

    private void BuildPanel(
        NativeLobbyHeroSelection native,
        HeroManager heroManager,
        NativeLobbyHeroArt art
    )
    {
        if (
            NativeGameTypography.PrepareOwnedText(out var font)
                != NativeGameTypography.Outcome.Ready
            || font == null
            || !art.Portraits.Keys.Any(hero => heroManager.TryGetHeroSO(hero, out _))
        )
            return;

        _root = DesktopKit.Rect(native.Parent, "BPP_ClassicHeroSelection");
        _root.anchorMin = _root.anchorMax = new Vector2(0f, .5f);
        _root.pivot = new Vector2(0f, 1f);
        _root.anchoredPosition = new Vector2(60f, 29f);
        _controls = _root.gameObject.AddComponent<CanvasGroup>();
        _controls.interactable = _controls.blocksRaycasts =
            !NativeLobbyHeroSelection.IsPopupOpen && !SceneLoader.IsTransitioning;
        var kit = new DesktopKit(font);
        if (art.Backdrop != null)
        {
            _backdrop = DesktopKit.Rect(native.Parent.parent, "BPP_ClassicHeroBackdrop");
            _backdrop.SetAsFirstSibling();
            _backdrop.anchorMin = Vector2.zero;
            _backdrop.anchorMax = Vector2.up;
            _backdrop.pivot = new Vector2(0f, .5f);
            _backdrop.anchoredPosition = Vector2.zero;
            _backdrop.sizeDelta = new Vector2(1200f, 0f);
            var fade = _backdrop.gameObject.AddComponent<Image>();
            fade.sprite = art.Backdrop;
            fade.color = NativeLobbyHeroArt.BackdropTint;
            fade.raycastTarget = false;
        }

        foreach (var (hero, x, y) in Slots)
        {
            if (
                !art.Portraits.TryGetValue(hero, out var portrait)
                || !heroManager.TryGetHeroSO(hero, out _)
            )
                continue;

            var hit = MakeButton(
                _root,
                hero.ToString(),
                new Vector2(x, y),
                new Vector2(278f, 268f)
            );
            hit.onClick.AddListener(() => OnHeroClicked(hero));
            var glow = MakeImage(
                hit.transform,
                "SelectedGlow",
                art.Selected,
                new Vector2(0f, hero == EHero.Dooley ? -10f : -18f),
                new Vector2(306f, 368f)
            );
            var icon = MakeImage(
                hit.transform,
                "ShieldPortrait",
                portrait,
                new Vector2(0f, hero == EHero.Dooley ? -12f : 0f),
                new Vector2(374f, 374f)
            );
            var namePlate = MakeImage(
                hit.transform,
                "SelectedName",
                art.NamePlate,
                new Vector2(0f, hero == EHero.Dooley ? -101f : -109f),
                new Vector2(318f, 70f)
            );
            namePlate.preserveAspect = false;
            var name = kit.Text(
                namePlate.transform,
                string.Empty,
                KitTextRole.Title,
                NativeLobbyHeroArt.SelectedNameInk,
                alignment: TextAlignmentOptions.Center
            );
            StyleLabel(name, 48f, art);
            Place(name.rectTransform, new Vector2(0f, 3f), new Vector2(312f, 114f));
            glow.gameObject.SetActive(false);
            namePlate.gameObject.SetActive(false);
            _tiles.Add(new HeroTile(hero, icon, glow, namePlate, name));
        }

        _randomButton = MakeButton(
            _root,
            "RandomHero",
            new Vector2(245f, -1002f),
            new Vector2(490f, 126f)
        );
        _randomButton.onClick.AddListener(ToggleRandom);
        var box = MakeImage(
            _randomButton.transform,
            "Frame",
            art.RandomFrame,
            new Vector2(-157f, 0f),
            new Vector2(94f, 94f)
        );
        _randomMark = MakeImage(
            box.transform,
            "Check",
            art.RandomCheck,
            Vector2.zero,
            new Vector2(60f, 60f)
        );
        _randomLabel = kit.Text(
            _randomButton.transform,
            string.Empty,
            KitTextRole.Title,
            NativeLobbyHeroArt.RandomLabelInk
        );
        StyleLabel(_randomLabel, 60f, art);
        Place(_randomLabel.rectTransform, new Vector2(95f, 0f), new Vector2(310f, 108f));

        native.Attach(loadoutRise: 275f);
    }

    private void StyleLabel(TextMeshProUGUI label, float fontSize, NativeLobbyHeroArt art)
    {
        label.fontSize = fontSize;
        _labelMaterial ??= art.CloneLabelMaterial(label.fontSharedMaterial);
        if (_labelMaterial != null)
        {
            label.fontSharedMaterial = _labelMaterial;
            label.extraPadding = true;
        }
        else
        {
            label.fontStyle = FontStyles.Bold;
        }
    }

    private static Button MakeButton(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var rect = DesktopKit.Rect(parent, name);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var target = rect.gameObject.AddComponent<Image>();
        target.color = Color.clear;
        var button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = target;
        return button;
    }

    private static Image MakeImage(
        Transform parent,
        string name,
        Sprite sprite,
        Vector2 position,
        Vector2 size
    )
    {
        var rect = DesktopKit.Rect(parent, name);
        Place(rect, position, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private void ToggleRandom()
    {
        if (!CanChoose())
            return;

        var enabled = !PlayerPreferences.Data.RandomHeroEnabled;
        if (enabled)
            IncludeAllOwnedHeroes();
        NativeLobbyHeroSelection.SetRandom(enabled);
        _nextRefresh = 0f;
    }

    private bool CanChoose() =>
        _native?.CanShow == true
        && !NativeLobbyHeroSelection.IsPopupOpen
        && !SceneLoader.IsTransitioning
        && !Data.HasActiveRun;

    private void OnHeroClicked(EHero hero)
    {
        if (!CanChoose())
            return;

        if (!PlayerPreferences.Data.RandomHeroEnabled)
        {
            if (Services.TryGet<HeroManager>(out var manager) && manager != null)
                manager.SelectHero(hero);
            _nextRefresh = 0f;
            return;
        }

        if (!HeroManager.OwnsHero(hero))
            return;

        var wasIncluded = NativeLobbyHeroSelection.IsHeroInRandomPool(hero);
        // The native draw keeps the last selected hero if the pool is empty.
        if (wasIncluded && !_tiles.Any(tile => tile.Hero != hero && IsRandomCandidate(tile)))
            return;

        NativeLobbyHeroSelection.SetHeroInRandomPool(hero, !wasIncluded);
        _nextRefresh = 0f;
    }

    private void IncludeAllOwnedHeroes()
    {
        foreach (var tile in _tiles)
        {
            if (HeroManager.OwnsHero(tile.Hero))
                NativeLobbyHeroSelection.SetHeroInRandomPool(tile.Hero, included: true);
        }
    }

    private static bool IsRandomCandidate(HeroTile tile) =>
        HeroManager.OwnsHero(tile.Hero) && NativeLobbyHeroSelection.IsHeroInRandomPool(tile.Hero);

    private void RemovePanel()
    {
        // The lobby can destroy the native root before we release its tiles and label material.
        if (_root is null)
            return;

        _native?.Detach();
        if (_root != null)
        {
            _root.gameObject.SetActive(false);
            Destroy(_root.gameObject);
        }
        if (_backdrop != null)
            Destroy(_backdrop.gameObject);
        if (_labelMaterial != null)
            Destroy(_labelMaterial);
        _root = null;
        _backdrop = null;
        _labelMaterial = null;
        _controls = null;
        _randomButton = null;
        _randomMark = null;
        _randomLabel = null;
        _tiles.Clear();
    }

    private async void OnDestroy()
    {
        RemovePanel();
        if (_artLoad == null)
            return;
        // SpriteAtlas.GetSprite creates instances; the other sprites belong to AssetLoader.
        NativeLobbyHeroArt? art;
        try
        {
            art = await _artLoad;
        }
        catch
        {
            return;
        }

        if (art != null)
        {
            Destroy(art.RandomFrame);
            Destroy(art.RandomCheck);
            Destroy(art.Backdrop);
        }
    }

    private sealed record HeroTile(
        EHero Hero,
        Image Icon,
        Image Glow,
        Image NamePlate,
        TextMeshProUGUI Name
    )
    {
        internal bool GlowTargetVisible { get; set; }
        internal float GlowProgress { get; set; }
    }
}
