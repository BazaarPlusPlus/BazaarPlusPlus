#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI;
using TheBazaar.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

internal sealed class RandomHeroPoolPanelController : MonoBehaviour
{
    private const string LogCategory = "RandomHeroPool";
    private const string PanelObjectName = "BPP_RandomHeroPoolPanel";
    private const string HeaderObjectName = "BPP_RandomHeroPoolHeader";
    private const string EntriesRootObjectName = "BPP_RandomHeroPoolEntries";
    private const string EmptyLabelObjectName = "BPP_RandomHeroPoolEmpty";
    private const string EntryPrefix = "BPP_RandomHeroPoolHero_";
    private const int EntryColumnCount = 3;
    private const float PanelWidth = 236f;
    private const float PanelHorizontalPadding = 10f;
    private const float PanelTopPadding = 8f;
    private const float PanelBottomPadding = 10f;
    private const float HeaderHeight = 18f;
    private const float HeaderToEntriesSpacing = 8f;
    private const float EntryWidth = 66f;
    private const float EntryHeight = 32f;
    private const float EntryHorizontalSpacing = 6f;
    private const float EntryVerticalSpacing = 6f;
    private static readonly Vector2 PanelAnchorOffset = new(0f, -10f);

    private static readonly System.Reflection.FieldInfo? HeroItemViewsField = AccessTools.Field(
        typeof(HeroSelectButtonsView),
        "HeroItemViews"
    );

    private static readonly System.Reflection.FieldInfo? RandomHeroToggleField = AccessTools.Field(
        typeof(HeroSelectButtonsView),
        "RandomHeroToggle"
    );

    private static readonly System.Reflection.FieldInfo? HeroItemIsUnlockedField =
        AccessTools.Field(typeof(HeroItemView), "_isUnlocked");

    private readonly Dictionary<string, HeroPoolEntryView> _heroEntryViews = new(
        StringComparer.Ordinal
    );

    private HeroSelectButtonsView? _view;
    private Toggle? _randomHeroToggle;
    private RectTransform? _panelRoot;
    private RectTransform? _entriesRoot;
    private TextMeshProUGUI? _headerLabel;
    private TextMeshProUGUI? _emptyLabel;
    private RandomHeroPoolState? _state;
    private HeroAvailability[] _heroAvailabilities = Array.Empty<HeroAvailability>();
    private string[] _unlockedHeroIds = Array.Empty<string>();
    private bool _warnedMissingHeroItemViewsField;
    private bool _warnedMissingHeroUnlockStateField;
    private bool _warnedMissingRandomHeroToggleField;
    private bool _subscribedToEvents;
    private Coroutine? _pendingRosterRefreshCoroutine;

    internal static void Attach(HeroSelectButtonsView view)
    {
        if (view == null)
            return;

        var controller = GetOrCreateController(view);
        controller.TryAttach(view);
        controller.RefreshRosterState(forceRebuild: true);
        controller.UpdatePanelVisibility();
    }

    internal static void NotifyRosterChanged(HeroSelectButtonsView view, bool forceRebuild)
    {
        if (view == null)
            return;

        var controller = GetOrCreateController(view);
        controller.TryAttach(view);
        controller.RefreshRosterState(forceRebuild);
        controller.UpdatePanelVisibility();
    }

    internal static void ScheduleRosterRefresh(
        HeroSelectButtonsView view,
        Task refreshTask,
        bool forceRebuild
    )
    {
        if (view == null || refreshTask == null)
            return;

        var controller = GetOrCreateController(view);
        controller.TryAttach(view);
        controller.ScheduleRosterRefresh(refreshTask, forceRebuild);
    }

    internal static void NotifyVisibilityChanged(HeroSelectButtonsView view)
    {
        if (view == null)
            return;

        view.GetComponent<RandomHeroPoolPanelController>()?.UpdatePanelVisibility();
    }

    private void OnDestroy()
    {
        CancelPendingRosterRefresh();
        UnsubscribeFromEvents();
        UnbindToggle();
    }

    private void OnRectTransformDimensionsChange()
    {
        UpdatePanelVisibility();
    }

    private static RandomHeroPoolPanelController GetOrCreateController(HeroSelectButtonsView view)
    {
        var controller = view.GetComponent<RandomHeroPoolPanelController>();
        return controller ?? view.gameObject.AddComponent<RandomHeroPoolPanelController>();
    }

    private void TryAttach(HeroSelectButtonsView view)
    {
        if (!TryReadRandomHeroToggle(view, out var randomHeroToggle))
            return;

        if (
            ReferenceEquals(_view, view)
            && ReferenceEquals(_randomHeroToggle, randomHeroToggle)
            && _panelRoot != null
            && _subscribedToEvents
        )
        {
            return;
        }

        _view = view;
        _randomHeroToggle = randomHeroToggle;
        BindToggle(randomHeroToggle);
        SubscribeToEvents();
        if (!TryEnsurePanel())
            return;
    }

    private bool TryReadRandomHeroToggle(HeroSelectButtonsView view, out Toggle randomHeroToggle)
    {
        var reflectedToggle = RandomHeroToggleField?.GetValue(view) as Toggle;
        if (reflectedToggle != null)
        {
            randomHeroToggle = reflectedToggle;
            return true;
        }

        randomHeroToggle = null!;
        if (!_warnedMissingRandomHeroToggleField)
        {
            _warnedMissingRandomHeroToggleField = true;
            BppLog.Warn(
                LogCategory,
                "RandomHeroToggle field was unavailable; skipping random hero pool UI injection."
            );
        }

        return false;
    }

    private void BindToggle(Toggle randomHeroToggle)
    {
        UnbindToggle();
        _randomHeroToggle = randomHeroToggle;
        _randomHeroToggle.onValueChanged.AddListener(OnRandomHeroToggleValueChanged);
    }

    private void UnbindToggle()
    {
        if (_randomHeroToggle != null)
            _randomHeroToggle.onValueChanged.RemoveListener(OnRandomHeroToggleValueChanged);
    }

    private void SubscribeToEvents()
    {
        if (_subscribedToEvents)
            return;

        Events.RandomHeroModeChanged.AddListener(OnRandomHeroModeChanged, this);
        Events.ScreenSizeChanged.AddListener(OnScreenSizeChanged, this);
        Events.ResolutionChanged.AddListener(OnResolutionChanged, this);
        _subscribedToEvents = true;
    }

    private void UnsubscribeFromEvents()
    {
        if (!_subscribedToEvents)
            return;

        Events.RandomHeroModeChanged.RemoveListener(OnRandomHeroModeChanged);
        Events.ScreenSizeChanged.RemoveListener(OnScreenSizeChanged);
        Events.ResolutionChanged.RemoveListener(OnResolutionChanged);
        _subscribedToEvents = false;
    }

    private void OnRandomHeroToggleValueChanged(bool _)
    {
        UpdatePanelVisibility();
    }

    private void OnRandomHeroModeChanged(bool _)
    {
        UpdatePanelVisibility();
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
        if (_randomHeroToggle == null)
        {
            BppLog.Warn(LogCategory, "Random hero toggle was unavailable while building panel.");
            return false;
        }

        var hostRect = _randomHeroToggle.transform.parent as RectTransform;
        if (hostRect == null)
        {
            BppLog.Warn(
                LogCategory,
                "Random hero toggle parent was not a RectTransform; skipping pool panel."
            );
            return false;
        }

        var existingPanel = hostRect.Find(PanelObjectName) as RectTransform;
        if (existingPanel != null)
        {
            _panelRoot = existingPanel;
            _headerLabel = existingPanel.Find(HeaderObjectName)?.GetComponent<TextMeshProUGUI>();
            _entriesRoot = existingPanel.Find(EntriesRootObjectName) as RectTransform;
            _emptyLabel = existingPanel.Find(EmptyLabelObjectName)?.GetComponent<TextMeshProUGUI>();
            if (_headerLabel != null && _entriesRoot != null && _emptyLabel != null)
                return true;

            UnityEngine.Object.Destroy(existingPanel.gameObject);
            _panelRoot = null;
            _headerLabel = null;
            _entriesRoot = null;
            _emptyLabel = null;
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

        var backgroundImage = panelObject.GetComponent<Image>();
        backgroundImage.color = new Color(0.10f, 0.09f, 0.11f, 0.88f);
        backgroundImage.raycastTarget = false;

        var outline = panelObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.72f, 0.30f, 0.18f, 0.72f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        outline.useGraphicAlpha = true;

        _headerLabel = CreateText(
            HeaderObjectName,
            panelRect,
            fontSize: 15f,
            alignment: TextAlignmentOptions.Left,
            color: new Color(0.96f, 0.77f, 0.45f, 1f)
        );
        if (_headerLabel == null)
        {
            BppLog.Warn(LogCategory, "Failed to create random hero pool header.");
            return false;
        }

        var headerRect = _headerLabel.rectTransform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0f, 1f);
        headerRect.offsetMin = new Vector2(PanelHorizontalPadding, -PanelTopPadding - HeaderHeight);
        headerRect.offsetMax = new Vector2(-PanelHorizontalPadding, -PanelTopPadding);

        var entriesObject = new GameObject(EntriesRootObjectName, typeof(RectTransform));
        _entriesRoot = entriesObject.GetComponent<RectTransform>();
        _entriesRoot.SetParent(panelRect, worldPositionStays: false);
        _entriesRoot.anchorMin = new Vector2(0f, 1f);
        _entriesRoot.anchorMax = new Vector2(0f, 1f);
        _entriesRoot.pivot = new Vector2(0f, 1f);
        _entriesRoot.sizeDelta = Vector2.zero;
        _entriesRoot.anchoredPosition = new Vector2(
            PanelHorizontalPadding,
            -(PanelTopPadding + HeaderHeight + HeaderToEntriesSpacing)
        );

        _emptyLabel = CreateText(
            EmptyLabelObjectName,
            panelRect,
            fontSize: 13f,
            alignment: TextAlignmentOptions.Center,
            color: new Color(0.68f, 0.68f, 0.72f, 0.95f)
        );
        if (_emptyLabel == null)
        {
            BppLog.Warn(LogCategory, "Failed to create random hero pool empty label.");
            return false;
        }

        var emptyRect = _emptyLabel.rectTransform;
        emptyRect.anchorMin = new Vector2(0f, 1f);
        emptyRect.anchorMax = new Vector2(1f, 1f);
        emptyRect.pivot = new Vector2(0.5f, 1f);
        emptyRect.offsetMin = new Vector2(
            PanelHorizontalPadding,
            -PanelTopPadding - HeaderHeight - 38f
        );
        emptyRect.offsetMax = new Vector2(
            -PanelHorizontalPadding,
            -PanelTopPadding - HeaderHeight - 8f
        );
        _emptyLabel.text = "No heroes";
        _emptyLabel.gameObject.SetActive(false);

        _panelRoot = panelRect;
        return true;
    }

    private void UpdatePanelVisibility()
    {
        if (_panelRoot == null || _randomHeroToggle == null)
            return;

        var shouldBeVisible =
            _randomHeroToggle.gameObject.activeSelf && IsRandomHeroModeEffectivelyEnabled();
        if (_panelRoot.gameObject.activeSelf != shouldBeVisible)
            _panelRoot.gameObject.SetActive(shouldBeVisible);

        if (shouldBeVisible)
            SyncPanelPlacement();
    }

    private static bool IsRandomHeroModeEffectivelyEnabled()
    {
        try
        {
            return HeroSelectButtonsView.IsRandomHeroEnabled
                || PlayerPreferences.Data.RandomHeroEnabled;
        }
        catch
        {
            return false;
        }
    }

    private void ScheduleRosterRefresh(Task refreshTask, bool forceRebuild)
    {
        CancelPendingRosterRefresh();
        _pendingRosterRefreshCoroutine = StartCoroutine(
            WaitForRosterRefresh(refreshTask, forceRebuild)
        );
    }

    private void CancelPendingRosterRefresh()
    {
        if (_pendingRosterRefreshCoroutine == null)
            return;

        StopCoroutine(_pendingRosterRefreshCoroutine);
        _pendingRosterRefreshCoroutine = null;
    }

    private IEnumerator WaitForRosterRefresh(Task refreshTask, bool forceRebuild)
    {
        while (!refreshTask.IsCompleted)
            yield return null;

        _pendingRosterRefreshCoroutine = null;
        if (_view != null)
            NotifyRosterChanged(_view, forceRebuild);
    }

    private void SyncPanelPlacement()
    {
        if (_panelRoot == null || _randomHeroToggle == null)
            return;

        var parentRect = _panelRoot.parent as RectTransform;
        var randomToggleRect = _randomHeroToggle.transform as RectTransform;
        if (parentRect == null || randomToggleRect == null)
            return;

        _panelRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRoot.pivot = new Vector2(0f, 1f);
        _panelRoot.localScale = Vector3.one;
        _panelRoot.localRotation = Quaternion.identity;

        var corners = new Vector3[4];
        randomToggleRect.GetWorldCorners(corners);
        var toggleBottomLeft = parentRect.InverseTransformPoint(corners[0]);
        _panelRoot.localPosition = new Vector3(
            toggleBottomLeft.x + PanelAnchorOffset.x,
            toggleBottomLeft.y + PanelAnchorOffset.y,
            _panelRoot.localPosition.z
        );
    }

    private void RefreshRosterState(bool forceRebuild)
    {
        if (_entriesRoot == null || _view == null || _panelRoot == null)
            return;

        if (!TryBuildHeroAvailabilities(_view, out var heroAvailabilities))
            return;

        if (!forceRebuild && !HasHeroAvailabilityChanged(heroAvailabilities))
            return;

        _heroAvailabilities = heroAvailabilities;
        _unlockedHeroIds = heroAvailabilities
            .Where(candidate => candidate.IsUnlocked)
            .Select(candidate => candidate.HeroId)
            .ToArray();

        if (
            RandomHeroPoolPlayerPrefs.TryResolveState(_unlockedHeroIds, out var state)
            && state != null
        )
        {
            ApplyState(state, persist: true);
        }
        else
        {
            _state = null;
        }

        RebuildHeroEntries();
        RebindHeroEntries();
    }

    private bool TryBuildHeroAvailabilities(
        HeroSelectButtonsView view,
        out HeroAvailability[] heroAvailabilities
    )
    {
        heroAvailabilities = Array.Empty<HeroAvailability>();
        if (!TryReadHeroItemViews(view, out var heroItemViews))
            return false;

        heroAvailabilities = heroItemViews
            .Where(candidate => candidate != null)
            .Select(candidate => new HeroAvailability(
                candidate.Hero.ToString(),
                IsUnlocked(candidate)
            ))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.HeroId))
            .Distinct(HeroAvailabilityComparer.Instance)
            .ToArray();
        return true;
    }

    private bool HasHeroAvailabilityChanged(HeroAvailability[] nextHeroAvailabilities)
    {
        if (_heroAvailabilities.Length != nextHeroAvailabilities.Length)
            return true;

        for (var index = 0; index < nextHeroAvailabilities.Length; index++)
        {
            var current = _heroAvailabilities[index];
            var next = nextHeroAvailabilities[index];
            if (
                !string.Equals(current.HeroId, next.HeroId, StringComparison.Ordinal)
                || current.IsUnlocked != next.IsUnlocked
            )
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyState(RandomHeroPoolState nextState, bool persist)
    {
        _state = nextState;
        if (persist)
            RandomHeroPoolPlayerPrefs.SaveSelectedHeroIds(nextState.SelectedHeroIds);
        RebindHeroEntries();
    }

    private void RebuildHeroEntries()
    {
        if (_entriesRoot == null || _panelRoot == null)
            return;

        _heroEntryViews.Clear();
        for (var index = _entriesRoot.childCount - 1; index >= 0; index--)
        {
            var child = _entriesRoot.GetChild(index);
            if (child != null)
                UnityEngine.Object.Destroy(child.gameObject);
        }

        for (var index = 0; index < _heroAvailabilities.Length; index++)
            CreateHeroEntry(_heroAvailabilities[index], index);

        var rows = Mathf.CeilToInt(_heroAvailabilities.Length / (float)EntryColumnCount);
        var entriesHeight =
            rows <= 0 ? 0f : (rows * EntryHeight) + ((rows - 1) * EntryVerticalSpacing);
        _entriesRoot.sizeDelta = new Vector2(
            PanelWidth - (PanelHorizontalPadding * 2f),
            entriesHeight
        );
        _panelRoot.sizeDelta = new Vector2(PanelWidth, CalculatePanelHeight(rows));

        if (_emptyLabel != null)
            _emptyLabel.gameObject.SetActive(_heroAvailabilities.Length == 0);
    }

    private void CreateHeroEntry(HeroAvailability heroAvailability, int index)
    {
        if (_entriesRoot == null)
            return;

        var entryObject = new GameObject(
            $"{EntryPrefix}{heroAvailability.HeroId}",
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
        entryRect.anchoredPosition = new Vector2(
            (index % EntryColumnCount) * (EntryWidth + EntryHorizontalSpacing),
            -(index / EntryColumnCount) * (EntryHeight + EntryVerticalSpacing)
        );

        var background = entryObject.GetComponent<Image>();
        background.raycastTarget = true;

        var outline = entryObject.GetComponent<Outline>();
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;

        var button = entryObject.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => OnHeroEntryClicked(heroAvailability.HeroId));
        button.targetGraphic = background;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        var label = CreateText(
            "Label",
            entryRect,
            fontSize: 16f,
            alignment: TextAlignmentOptions.Center,
            color: Color.white
        );
        if (label == null)
        {
            BppLog.Warn(
                LogCategory,
                $"Failed to create hero pool label for '{heroAvailability.HeroId}'."
            );
            return;
        }

        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        label.text = GetHeroBadgeStyle(heroAvailability.HeroId).ShortCode;

        _heroEntryViews[heroAvailability.HeroId] = new HeroPoolEntryView(
            heroAvailability.HeroId,
            button,
            background,
            outline,
            label
        );
    }

    private void OnHeroEntryClicked(string heroId)
    {
        if (_state == null)
            return;

        var nextState = _state.SetSelected(heroId, !_state.IsSelected(heroId));
        ApplyState(nextState, persist: true);
    }

    private void RebindHeroEntries()
    {
        foreach (var heroAvailability in _heroAvailabilities)
        {
            if (!_heroEntryViews.TryGetValue(heroAvailability.HeroId, out var entryView))
                continue;

            ApplyEntryVisual(
                entryView,
                heroAvailability.IsUnlocked,
                heroAvailability.IsUnlocked && _state?.IsSelected(heroAvailability.HeroId) == true
            );
        }

        if (_headerLabel != null)
        {
            var selectedCount = _state?.SelectedHeroIds.Count ?? 0;
            _headerLabel.text =
                _unlockedHeroIds.Length == 0
                    ? "POOL 0/0"
                    : $"POOL {selectedCount}/{_unlockedHeroIds.Length}";
        }
    }

    private static void ApplyEntryVisual(
        HeroPoolEntryView entryView,
        bool isUnlocked,
        bool isSelected
    )
    {
        var style = GetHeroBadgeStyle(entryView.HeroId);
        if (!isUnlocked)
        {
            entryView.Button.interactable = false;
            entryView.Background.color = new Color(0.17f, 0.17f, 0.19f, 0.72f);
            entryView.Outline.effectColor = new Color(0f, 0f, 0f, 0.30f);
            entryView.Label.color = new Color(0.52f, 0.52f, 0.56f, 0.88f);
            return;
        }

        entryView.Button.interactable = true;
        if (isSelected)
        {
            entryView.Background.color = style.Background;
            entryView.Outline.effectColor = new Color(0.98f, 0.86f, 0.45f, 0.70f);
            entryView.Label.color = style.Text;
            return;
        }

        entryView.Background.color = new Color(0.29f, 0.29f, 0.32f, 0.92f);
        entryView.Outline.effectColor = new Color(0f, 0f, 0f, 0.45f);
        entryView.Label.color = new Color(0.86f, 0.86f, 0.88f, 0.95f);
    }

    private static bool TryReadHeroItemViews(
        HeroSelectButtonsView view,
        out IReadOnlyList<HeroItemView> heroItemViews
    )
    {
        heroItemViews = Array.Empty<HeroItemView>();
        if (HeroItemViewsField?.GetValue(view) is not IEnumerable<HeroItemView> reflectedViews)
        {
            var controller = view.GetComponent<RandomHeroPoolPanelController>();
            if (controller != null && !controller._warnedMissingHeroItemViewsField)
            {
                controller._warnedMissingHeroItemViewsField = true;
                BppLog.Warn(
                    LogCategory,
                    "HeroItemViews field was unavailable; skipping random hero pool entries."
                );
            }

            return false;
        }

        heroItemViews = reflectedViews.Where(candidate => candidate != null).ToArray();
        return true;
    }

    private bool IsUnlocked(HeroItemView heroItemView)
    {
        if (HeroItemIsUnlockedField?.GetValue(heroItemView) is bool unlocked)
            return unlocked;

        if (!_warnedMissingHeroUnlockStateField)
        {
            _warnedMissingHeroUnlockStateField = true;
            BppLog.Warn(
                LogCategory,
                "HeroItemView unlock-state field was unavailable; falling back to button state."
            );
        }

        return heroItemView.HeroButton != null && heroItemView.HeroButton.interactable;
    }

    private static TextMeshProUGUI? CreateText(
        string objectName,
        Transform parent,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color
    )
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(parent, worldPositionStays: false);

        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private static float CalculatePanelHeight(int rows)
    {
        var entriesHeight =
            rows <= 0 ? 24f : (rows * EntryHeight) + ((rows - 1) * EntryVerticalSpacing);
        return PanelTopPadding
            + HeaderHeight
            + HeaderToEntriesSpacing
            + entriesHeight
            + PanelBottomPadding;
    }

    private static HeroBadgeStyle GetHeroBadgeStyle(string? heroName)
    {
        if (string.IsNullOrWhiteSpace(heroName))
            return new HeroBadgeStyle("UNK", new Color(0.20f, 0.29f, 0.38f, 0.95f), Color.white);

        return heroName.Trim() switch
        {
            "Vanessa" => BuildHeroBadgeStyle("VAN", 192, 33, 33),
            "Pygmalien" => BuildHeroBadgeStyle("PYG", 39, 103, 192),
            "Dooley" => BuildHeroBadgeStyle("DOO", 225, 154, 8),
            "Mak" => BuildHeroBadgeStyle("MAK", 190, 230, 91),
            "Jules" => BuildHeroBadgeStyle("JUL", 180, 52, 236),
            "Karnok" => BuildHeroBadgeStyle("KAR", 59, 136, 156),
            "Stelle" => BuildHeroBadgeStyle("STE", 255, 235, 24),
            _ => BuildHeroBadgeStyle(
                heroName.Length <= 3
                    ? heroName.ToUpperInvariant()
                    : heroName[..3].ToUpperInvariant(),
                57,
                73,
                97
            ),
        };
    }

    private static HeroBadgeStyle BuildHeroBadgeStyle(string shortCode, int r, int g, int b)
    {
        var background = new Color(r / 255f, g / 255f, b / 255f, 0.98f);
        var luminance = (0.299f * background.r) + (0.587f * background.g) + (0.114f * background.b);
        var text = luminance > 0.62f ? new Color(0.10f, 0.12f, 0.15f, 1f) : Color.white;
        return new HeroBadgeStyle(shortCode, background, text);
    }

    private readonly struct HeroAvailability
    {
        public HeroAvailability(string heroId, bool isUnlocked)
        {
            HeroId = heroId;
            IsUnlocked = isUnlocked;
        }

        public string HeroId { get; }

        public bool IsUnlocked { get; }
    }

    private sealed class HeroAvailabilityComparer : IEqualityComparer<HeroAvailability>
    {
        public static readonly HeroAvailabilityComparer Instance = new();

        public bool Equals(HeroAvailability x, HeroAvailability y)
        {
            return string.Equals(x.HeroId, y.HeroId, StringComparison.Ordinal);
        }

        public int GetHashCode(HeroAvailability obj)
        {
            return StringComparer.Ordinal.GetHashCode(obj.HeroId);
        }
    }

    private sealed class HeroPoolEntryView
    {
        public HeroPoolEntryView(
            string heroId,
            Button button,
            Image background,
            Outline outline,
            TextMeshProUGUI label
        )
        {
            HeroId = heroId;
            Button = button;
            Background = background;
            Outline = outline;
            Label = label;
        }

        public string HeroId { get; }

        public Button Button { get; }

        public Image Background { get; }

        public Outline Outline { get; }

        public TextMeshProUGUI Label { get; }
    }

    private readonly struct HeroBadgeStyle
    {
        public HeroBadgeStyle(string shortCode, Color background, Color text)
        {
            ShortCode = shortCode;
            Background = background;
            Text = text;
        }

        public string ShortCode { get; }

        public Color Background { get; }

        public Color Text { get; }
    }
}
