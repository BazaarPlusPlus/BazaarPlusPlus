#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarGameShared;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Lobby;
using HarmonyLib;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal sealed class RandomHeroSkinPoolPanelController : MonoBehaviour
{
    private const string LogCategory = "RandomHeroSkinPool";
    private const string PanelObjectName = "BPP_RandomCollectiblePoolPanel";
    private const string HeaderObjectName = "BPP_RandomCollectiblePoolHeader";
    private const string EntriesRootObjectName = "BPP_RandomCollectiblePoolEntries";
    private const string EmptyLabelObjectName = "BPP_RandomCollectiblePoolEmpty";
    private const string EntryPrefix = "BPP_RandomCollectiblePoolEntry_";
    private const int EntryColumnCount = 2;
    private const float PanelWidth = 252f;
    private const float PanelHorizontalGap = 12f;
    private const float PanelVerticalOffset = 0f;
    private const float EntryWidth = 110f;
    private const float EntryHeight = 42f;
    private const float EntryHorizontalSpacing = 8f;
    private const float EntryVerticalSpacing = 8f;

    private static readonly System.Reflection.FieldInfo? ScrollRectField = AccessTools.Field(
        typeof(CosmeticsListManager),
        "_scrollRect"
    );

    private readonly Dictionary<string, SkinPoolEntryView> _entryViews = new(
        StringComparer.Ordinal
    );

    private CosmeticsListManager? _view;
    private ScrollRect? _scrollRect;
    private RectTransform? _panelRoot;
    private RectTransform? _entriesRoot;
    private TextMeshProUGUI? _headerLabel;
    private TextMeshProUGUI? _emptyLabel;
    private RandomHeroSkinPoolState? _state;
    private BazaarSaleItem[] _availableSkins = Array.Empty<BazaarSaleItem>();
    private string? _focusedCollectionItemId;
    private BazaarInventoryTypes.ECollectionType _currentCosmeticType = BazaarInventoryTypes
        .ECollectionType
        .Invalid;
    private EHero _currentHero = EHero.Common;
    private bool _subscribedToEvents;
    private bool _warnedMissingScrollRectField;

    internal static void Attach(
        CosmeticsListManager view,
        BazaarInventoryTypes.ECollectionType cosmeticType,
        EHero hero
    )
    {
        if (view == null)
            return;

        var controller = view.GetComponent<RandomHeroSkinPoolPanelController>();
        if (controller == null)
            controller = view.gameObject.AddComponent<RandomHeroSkinPoolPanelController>();

        controller.TryAttach(view);
        controller.RefreshState(cosmeticType, hero);
        controller.UpdatePanelVisibility();
    }

    internal static void NotifyRandomizeChanged(CosmeticsListManager view)
    {
        if (view == null)
            return;

        var controller = view.GetComponent<RandomHeroSkinPoolPanelController>();
        if (controller == null)
            return;

        controller.RefreshState(controller._currentCosmeticType, controller._currentHero);
        controller.UpdatePanelVisibility();
    }

    internal static void NotifyCollectibleSelected(
        CosmeticsListManager view,
        BazaarInventoryTypes.ECollectionType collectionType,
        EHero hero,
        string? collectionItemId
    )
    {
        if (view == null || string.IsNullOrWhiteSpace(collectionItemId))
            return;

        var controller = view.GetComponent<RandomHeroSkinPoolPanelController>();
        if (controller == null)
            return;

        if (
            controller._currentCosmeticType != collectionType
            || controller._currentHero != hero
            || controller._state == null
        )
        {
            controller.RefreshState(collectionType, hero);
        }

        if (controller._state == null || controller._currentCosmeticType != collectionType)
            return;

        if (!controller._state.IsSelected(collectionItemId))
        {
            controller._state = controller._state.SetSelected(collectionItemId, isSelected: true);
            RandomHeroSkinPoolPlayerPrefs.SaveSelectedIds(
                hero,
                collectionType,
                controller._state.SelectedSkinIds
            );
        }

        controller._focusedCollectionItemId = collectionItemId;
        controller.RebindEntries();
        controller.UpdatePanelVisibility();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    private void OnRectTransformDimensionsChange()
    {
        UpdatePanelVisibility();
    }

    private void TryAttach(CosmeticsListManager view)
    {
        if (!TryReadScrollRect(view, out var scrollRect))
            return;

        if (
            ReferenceEquals(_view, view)
            && ReferenceEquals(_scrollRect, scrollRect)
            && _panelRoot != null
        )
            return;

        _view = view;
        _scrollRect = scrollRect;
        SubscribeToEvents();
        TryEnsurePanel();
    }

    private bool TryReadScrollRect(CosmeticsListManager view, out ScrollRect scrollRect)
    {
        var reflectedScrollRect = ScrollRectField?.GetValue(view) as ScrollRect;
        if (reflectedScrollRect != null)
        {
            scrollRect = reflectedScrollRect;
            return true;
        }

        scrollRect = null!;
        if (!_warnedMissingScrollRectField)
        {
            _warnedMissingScrollRectField = true;
            BppLog.Warn(
                LogCategory,
                "CosmeticsListManager._scrollRect was unavailable; skipping random hero skin pool UI."
            );
        }

        return false;
    }

    private void SubscribeToEvents()
    {
        if (_subscribedToEvents)
            return;

        Events.ScreenSizeChanged.AddListener(OnScreenSizeChanged, this);
        Events.ResolutionChanged.AddListener(OnResolutionChanged, this);
        _subscribedToEvents = true;
    }

    private void UnsubscribeFromEvents()
    {
        if (!_subscribedToEvents)
            return;

        Events.ScreenSizeChanged.RemoveListener(OnScreenSizeChanged);
        Events.ResolutionChanged.RemoveListener(OnResolutionChanged);
        _subscribedToEvents = false;
    }

    private void OnScreenSizeChanged(Rect _)
    {
        UpdatePanelVisibility();
    }

    private void OnResolutionChanged(ResolutionChangeData _)
    {
        UpdatePanelVisibility();
    }

    private bool TryEnsurePanel()
    {
        var hostRect = _scrollRect?.viewport?.parent as RectTransform;
        if (hostRect == null)
            return false;

        var existingPanel = hostRect.Find(PanelObjectName) as RectTransform;
        if (existingPanel != null)
        {
            _panelRoot = existingPanel;
            _headerLabel = existingPanel.Find(HeaderObjectName)?.GetComponent<TextMeshProUGUI>();
            _entriesRoot = existingPanel.Find(EntriesRootObjectName) as RectTransform;
            _emptyLabel = existingPanel.Find(EmptyLabelObjectName)?.GetComponent<TextMeshProUGUI>();
            return _panelRoot != null
                && _headerLabel != null
                && _entriesRoot != null
                && _emptyLabel != null;
        }

        var panelObject = new GameObject(
            PanelObjectName,
            typeof(RectTransform),
            typeof(Image),
            typeof(Outline)
        );
        var panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(hostRect, worldPositionStays: false);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.localScale = Vector3.one;
        panelRect.localRotation = Quaternion.identity;
        panelRect.sizeDelta = new Vector2(PanelWidth, CalculatePanelHeight(0));

        var background = panelObject.GetComponent<Image>();
        background.color = new Color(0.10f, 0.09f, 0.11f, 0.88f);
        background.raycastTarget = false;

        var outline = panelObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.18f, 0.62f, 0.56f, 0.72f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.useGraphicAlpha = true;

        _headerLabel = LobbyPanelLayout.CreateText(
            HeaderObjectName,
            panelRect,
            15f,
            TextAlignmentOptions.Left,
            new Color(0.72f, 0.96f, 0.87f, 1f)
        );
        if (_headerLabel == null)
            return false;

        var headerRect = _headerLabel.rectTransform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0f, 1f);
        headerRect.offsetMin = new Vector2(
            LobbyPanelLayout.PanelHorizontalPadding,
            -LobbyPanelLayout.PanelTopPadding - LobbyPanelLayout.HeaderHeight
        );
        headerRect.offsetMax = new Vector2(
            -LobbyPanelLayout.PanelHorizontalPadding,
            -LobbyPanelLayout.PanelTopPadding
        );

        var entriesObject = new GameObject(EntriesRootObjectName, typeof(RectTransform));
        _entriesRoot = entriesObject.GetComponent<RectTransform>();
        _entriesRoot.SetParent(panelRect, worldPositionStays: false);
        _entriesRoot.anchorMin = new Vector2(0f, 1f);
        _entriesRoot.anchorMax = new Vector2(0f, 1f);
        _entriesRoot.pivot = new Vector2(0f, 1f);
        _entriesRoot.anchoredPosition = new Vector2(
            LobbyPanelLayout.PanelHorizontalPadding,
            -(
                LobbyPanelLayout.PanelTopPadding
                + LobbyPanelLayout.HeaderHeight
                + LobbyPanelLayout.HeaderToEntriesSpacing
            )
        );

        _emptyLabel = LobbyPanelLayout.CreateText(
            EmptyLabelObjectName,
            panelRect,
            13f,
            TextAlignmentOptions.Center,
            new Color(0.68f, 0.68f, 0.72f, 0.95f),
            wrap: true
        );
        if (_emptyLabel == null)
            return false;

        var emptyRect = _emptyLabel.rectTransform;
        emptyRect.anchorMin = new Vector2(0f, 1f);
        emptyRect.anchorMax = new Vector2(1f, 1f);
        emptyRect.pivot = new Vector2(0.5f, 1f);
        emptyRect.offsetMin = new Vector2(
            LobbyPanelLayout.PanelHorizontalPadding,
            -LobbyPanelLayout.PanelTopPadding - LobbyPanelLayout.HeaderHeight - 40f
        );
        emptyRect.offsetMax = new Vector2(
            -LobbyPanelLayout.PanelHorizontalPadding,
            -LobbyPanelLayout.PanelTopPadding - LobbyPanelLayout.HeaderHeight - 8f
        );
        _emptyLabel.text = "No skins";
        _emptyLabel.gameObject.SetActive(false);

        _panelRoot = panelRect;
        return true;
    }

    private void RefreshState(BazaarInventoryTypes.ECollectionType cosmeticType, EHero hero)
    {
        _currentCosmeticType = cosmeticType;
        _currentHero = hero;

        if (!RandomHeroSkinPoolRuntime.IsSupported(cosmeticType))
        {
            _availableSkins = Array.Empty<BazaarSaleItem>();
            _state = null;
            RebuildEntries();
            return;
        }

        var collectionManager = Services.Get<CollectionManager>();
        if (collectionManager == null)
        {
            _availableSkins = Array.Empty<BazaarSaleItem>();
            return;
        }

        _availableSkins = RandomHeroSkinPoolRuntime.GetAvailableCollectibles(
            hero,
            cosmeticType,
            collectionManager
        );
        _focusedCollectionItemId = ResolveFocusedCollectionItemId(
            collectionManager,
            cosmeticType,
            hero
        );
        _state =
            _availableSkins.Length == 0
                ? null
                : RandomHeroSkinPoolRuntime.ResolveState(hero, cosmeticType, _availableSkins);

        RebuildEntries();
        RebindEntries();
    }

    private void UpdatePanelVisibility()
    {
        if (_panelRoot == null)
            return;

        var shouldBeVisible =
            RandomHeroSkinPoolRuntime.IsSupported(_currentCosmeticType)
            && _view != null
            && _view.gameObject.activeInHierarchy
            && Services.Get<CollectionManager>()?.GetRandomizeLoadout(_currentHero) == true;

        if (_panelRoot.gameObject.activeSelf != shouldBeVisible)
            _panelRoot.gameObject.SetActive(shouldBeVisible);

        if (shouldBeVisible)
            SyncPanelPlacement();
    }

    private void SyncPanelPlacement()
    {
        if (_panelRoot == null || _scrollRect?.viewport == null)
            return;

        var parentRect = _panelRoot.parent as RectTransform;
        var viewportRect = _scrollRect.viewport;
        if (parentRect == null || viewportRect == null)
            return;

        var corners = new Vector3[4];
        viewportRect.GetWorldCorners(corners);

        var topLeft = parentRect.InverseTransformPoint(corners[1]);
        var topRight = parentRect.InverseTransformPoint(corners[2]);
        var fitsRight = topRight.x + PanelHorizontalGap + PanelWidth <= parentRect.rect.xMax;

        _panelRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRoot.localScale = Vector3.one;
        _panelRoot.localRotation = Quaternion.identity;

        if (fitsRight)
        {
            _panelRoot.pivot = new Vector2(0f, 1f);
            _panelRoot.localPosition = new Vector3(
                topRight.x + PanelHorizontalGap,
                topRight.y + PanelVerticalOffset,
                _panelRoot.localPosition.z
            );
        }
        else
        {
            _panelRoot.pivot = new Vector2(1f, 1f);
            _panelRoot.localPosition = new Vector3(
                topLeft.x - PanelHorizontalGap,
                topLeft.y + PanelVerticalOffset,
                _panelRoot.localPosition.z
            );
        }
    }

    private void RebuildEntries()
    {
        if (_entriesRoot == null || _panelRoot == null)
            return;

        _entryViews.Clear();
        LobbyPanelLayout.ClearChildren(_entriesRoot);

        for (var index = 0; index < _availableSkins.Length; index++)
            CreateEntry(_availableSkins[index], index);

        var rows = Mathf.CeilToInt(_availableSkins.Length / (float)EntryColumnCount);
        var entriesHeight =
            rows <= 0 ? 0f : (rows * EntryHeight) + ((rows - 1) * EntryVerticalSpacing);
        _entriesRoot.sizeDelta = new Vector2(
            PanelWidth - (LobbyPanelLayout.PanelHorizontalPadding * 2f),
            entriesHeight
        );
        _panelRoot.sizeDelta = new Vector2(PanelWidth, CalculatePanelHeight(rows));

        if (_emptyLabel != null)
            _emptyLabel.gameObject.SetActive(_availableSkins.Length == 0);
    }

    private void CreateEntry(BazaarSaleItem saleItem, int index)
    {
        if (_entriesRoot == null || string.IsNullOrWhiteSpace(saleItem.CollectionItemID))
            return;

        var entryObject = new GameObject(
            $"{EntryPrefix}{index}",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(Outline)
        );
        var entryRect = entryObject.GetComponent<RectTransform>();
        entryRect.SetParent(_entriesRoot, worldPositionStays: false);
        entryRect.anchorMin = new Vector2(0f, 1f);
        entryRect.anchorMax = new Vector2(0f, 1f);
        entryRect.pivot = new Vector2(0f, 1f);
        entryRect.sizeDelta = new Vector2(EntryWidth, EntryHeight);
        entryRect.anchoredPosition = LobbyPanelLayout.GridAnchoredPosition(
            index,
            EntryColumnCount,
            EntryWidth,
            EntryHeight,
            EntryHorizontalSpacing,
            EntryVerticalSpacing
        );

        var background = entryObject.GetComponent<Image>();
        background.raycastTarget = true;

        var outline = entryObject.GetComponent<Outline>();
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        var button = entryObject.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = background;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => OnEntryClicked(saleItem.CollectionItemID));

        var label = LobbyPanelLayout.CreateText(
            "Label",
            entryRect,
            11f,
            TextAlignmentOptions.Center,
            Color.white,
            wrap: true
        );
        if (label == null)
            return;

        label.enableAutoSizing = true;
        label.fontSizeMin = 8f;
        label.fontSizeMax = 11f;
        label.overflowMode = TextOverflowModes.Ellipsis;

        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(6f, 4f);
        labelRect.offsetMax = new Vector2(-6f, -4f);
        label.text = BuildEntryLabel(saleItem);

        _entryViews[saleItem.CollectionItemID] = new SkinPoolEntryView(
            saleItem,
            button,
            background,
            outline,
            label
        );
    }

    private void OnEntryClicked(string skinId)
    {
        if (_state == null)
            return;

        var nextState = _state.SetSelected(skinId, !_state.IsSelected(skinId));
        _state = nextState;
        RandomHeroSkinPoolPlayerPrefs.SaveSelectedIds(
            _currentHero,
            _currentCosmeticType,
            nextState.SelectedSkinIds
        );
        RebindEntries();
    }

    private void RebindEntries()
    {
        foreach (var saleItem in _availableSkins)
        {
            if (!_entryViews.TryGetValue(saleItem.CollectionItemID, out var entryView))
                continue;

            ApplyEntryVisual(
                entryView,
                _state?.IsSelected(saleItem.CollectionItemID) == true,
                string.Equals(
                    saleItem.CollectionItemID,
                    _focusedCollectionItemId,
                    StringComparison.Ordinal
                )
            );
        }

        if (_headerLabel != null)
        {
            var selectedCount = _state?.SelectedSkinIds.Count ?? 0;
            _headerLabel.text =
                _availableSkins.Length == 0
                    ? "RANDOM POOL 0/0"
                    : $"RANDOM POOL {selectedCount}/{_availableSkins.Length}";
        }
    }

    private static void ApplyEntryVisual(
        SkinPoolEntryView entryView,
        bool isSelected,
        bool isFocused
    )
    {
        entryView.Button.interactable = true;

        if (isFocused && isSelected)
        {
            entryView.Background.color = entryView.SaleItem.IsDefault
                ? new Color(0.39f, 0.53f, 0.78f, 0.98f)
                : new Color(0.28f, 0.72f, 0.53f, 0.98f);
            entryView.Outline.effectColor = new Color(1.00f, 0.96f, 0.72f, 0.95f);
            entryView.Label.color = Color.white;
            return;
        }

        if (isFocused)
        {
            entryView.Background.color = new Color(0.62f, 0.42f, 0.18f, 0.96f);
            entryView.Outline.effectColor = new Color(1.00f, 0.88f, 0.52f, 0.90f);
            entryView.Label.color = Color.white;
            return;
        }

        if (isSelected)
        {
            entryView.Background.color = entryView.SaleItem.IsDefault
                ? new Color(0.32f, 0.44f, 0.62f, 0.96f)
                : new Color(0.21f, 0.59f, 0.44f, 0.96f);
            entryView.Outline.effectColor = new Color(0.96f, 0.90f, 0.62f, 0.72f);
            entryView.Label.color = Color.white;
            return;
        }

        entryView.Background.color = new Color(0.29f, 0.29f, 0.32f, 0.92f);
        entryView.Outline.effectColor = new Color(0f, 0f, 0f, 0.45f);
        entryView.Label.color = new Color(0.86f, 0.86f, 0.88f, 0.95f);
    }

    private static string BuildEntryLabel(BazaarSaleItem saleItem)
    {
        if (saleItem.IsDefault)
            return "DEFAULT";

        var name = string.IsNullOrWhiteSpace(saleItem.Name)
            ? saleItem.CollectionItemID
            : saleItem.Name.Trim();
        if (name.Length <= 18)
            return name.ToUpperInvariant();

        return $"{name[..15].ToUpperInvariant()}...";
    }

    private static string? ResolveFocusedCollectionItemId(
        CollectionManager collectionManager,
        BazaarInventoryTypes.ECollectionType collectionType,
        EHero hero
    )
    {
        if (collectionType == BazaarInventoryTypes.ECollectionType.HeroSkins)
        {
            return collectionManager.GetEquippedHeroSkinSaleItem(hero).CollectionItemID;
        }

        return collectionManager.GetEquippedSaleItem(collectionType, hero).CollectionItemID;
    }

    private static float CalculatePanelHeight(int rows) =>
        LobbyPanelLayout.CalculatePanelHeight(rows, EntryHeight, EntryVerticalSpacing);

    private sealed class SkinPoolEntryView
    {
        public SkinPoolEntryView(
            BazaarSaleItem saleItem,
            Button button,
            Image background,
            Outline outline,
            TextMeshProUGUI label
        )
        {
            SaleItem = saleItem;
            Button = button;
            Background = background;
            Outline = outline;
            Label = label;
        }

        public BazaarSaleItem SaleItem { get; }

        public Button Button { get; }

        public Image Background { get; }

        public Outline Outline { get; }

        public TextMeshProUGUI Label { get; }
    }
}
