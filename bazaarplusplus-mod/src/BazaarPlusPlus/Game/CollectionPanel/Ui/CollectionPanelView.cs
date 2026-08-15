#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;
using BazaarPlusPlus.Game.Supporters.Ui;
using BazaarPlusPlus.GameInterop.Fonts;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

internal sealed partial class CollectionPanelView : IDisposable
{
    private const string StagingIdCopyHint = "STAGING · 悬停卡牌后按 Ctrl+C 复制 template ID";
    private const string SourceChipInitialsName = "bpp-source-chip-initials";
    private const string TagChipIconName = "bpp-tag-chip-icon";
    private const string TagChipLabelName = "bpp-tag-chip-label";
    private const string SizeChipLabelName = "bpp-size-chip-label";
    private const string FacetMatchAnyName = "bpp-facet-match-any";
    private const string FacetMatchAllName = "bpp-facet-match-all";
    private const string FacetMatchLabelName = "bpp-facet-match-label";
    private const string FacetChoiceDividerName = "bpp-facet-choice-divider";
    private const string SortQualityButtonName = "bpp-collection-sort-quality";
    private const string SortSizeButtonName = "bpp-collection-sort-size";
    private const string SortButtonIconName = "bpp-collection-sort-icon";
    private const string SortButtonLabelName = "bpp-collection-sort-label";

    private readonly Transform _parent;
    private readonly ICollectionPanelCommands _commands;
    private readonly bool _stagingItemIdCopyEnabled;

    private GameObject? _rootObject;
    private UIDocument? _document;
    private PanelSettings? _panelSettings;
    private NativeGameTypography.PanelScope? _typography;
    private NativeGameTitleOverlay? _titleOverlay;
    private VisualElement? _root;
    private Label? _title;
    private VisualElement? _subtitle;
    private Label? _stagingIdCopyLabel;
    private Label? _statusLabel;
    private Label? _disclaimerLabel;
    private VisualElement? _tabModeControl;
    private Button? _closeButton;
    private VisualElement? _standardOperationControls;
    private VisualElement? _searchInputContainer;
    private VisualElement? _searchFrame;
    private TextField? _searchField;
    private Label? _searchPlaceholderLabel;
    private Button? _dayToggleButton;
    private VisualElement? _dayToggleContent;
    private Label? _dayToggleCaption;
    private Label? _dayToggleValue;
    private Button? _sortQualityButton;
    private Button? _sortSizeButton;
    private Label? _heroFilterLabel;
    private Button? _allHeroesButton;
    private Label? _tierFilterLabel;
    private Label? _keywordFilterLabel;
    private VisualElement? _keywordMatchModeButton;
    private VisualElement? _tagMatchModeButton;
    private VisualElement? _heroChipRow;
    private VisualElement? _tierChipRow;
    private VisualElement? _sizeChipRow;
    private VisualElement? _tagChipRow;
    private VisualElement? _keywordChipRow;
    private Label? _sourceFilterLabel;
    private VisualElement? _sourceChipRow;
    private VisualElement? _tagFilterSection;
    private VisualElement? _keywordFilterSection;
    private VisualElement? _sourceFilterSection;
    private VisualElement? _heroFilterSection;
    private VisualElement? _tierFilterSection;
    private ScrollView? _controlsScrollView;
    private VisualElement? _controlsScrollShadow;
    private ScrollViewDragScroller? _controlsDragScroller;
    private VisualElement? _gridViewport;
    private ScrollView? _gridScrollView;
    private ScrollViewDragScroller? _gridDragScroller;
    private VisualElement? _gridContentSpacer;
    private Label? _emptyLabel;
    private Label? _loadingLabel;
    private bool _loadingVisible;
    private string _loadingMessage = string.Empty;
    private float _loadingFrameElapsed;
    private int _loadingFrameIndex;
    private int _stagingIdCopyFeedbackGeneration;
    private bool _searchFocused;
    private float _searchFocusPulseElapsed;

    private readonly Dictionary<EHero, Button> _heroChips = new();
    private readonly Dictionary<EHero, VisualElement> _heroChipIcons = new();
    private readonly Dictionary<ETier, Button> _tierChips = new();
    private readonly Dictionary<ECardSize, Button> _sizeChips = new();
    private readonly Dictionary<ECardTag, Button> _tagChips = new();
    private readonly List<ECardTag> _tagChipOrder = new();
    private readonly Dictionary<CollectionKeywordFacetOption, Button> _keywordChips = new();
    private readonly List<CollectionKeywordFacetOption> _keywordChipOrder = new();

    private readonly Dictionary<string, Button> _sourceChips = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VisualElement> _sourceChipIcons = new(
        StringComparer.Ordinal
    );
    private readonly List<string> _sourceChipOrder = new();
    private Rect _lastGridBounds;
    private float _appliedHeroChipBox = -1f;
    private float _appliedSourceChipBox = -1f;

    // Panel-open/-close fade state. _opacity is the displayed alpha, _targetOpacity is what
    // SetVisible asked for; TickOpacity ramps the first toward the second with an
    // exponential lerp whose time constant is direction-dependent (in vs. out).
    private float _opacity;
    private float _targetOpacity;

    private static readonly string[] LoadingFrames = { "|", "/", "-", "\\" };

    public event Action<Rect>? GridViewportBoundsChanged;

    public CollectionPanelView(
        Transform parent,
        ICollectionPanelCommands commands,
        bool stagingItemIdCopyEnabled = false
    )
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _stagingItemIdCopyEnabled = stagingItemIdCopyEnabled;
    }

    internal void ShowStagingTemplateIdCopied(string displayName, string templateId)
    {
        if (_stagingIdCopyLabel == null)
            return;

        var generation = ++_stagingIdCopyFeedbackGeneration;
        _stagingIdCopyLabel.text = $"已复制 {displayName}: {templateId}";
        _stagingIdCopyLabel
            .schedule.Execute(() =>
            {
                if (generation == _stagingIdCopyFeedbackGeneration && _stagingIdCopyLabel != null)
                    _stagingIdCopyLabel.text = StagingIdCopyHint;
            })
            .StartingIn(2500);
    }

    public void EnsureCreated()
    {
        if (_rootObject != null)
            return;

        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.sortingOrder = BppOverlaySorting.PanelUiToolkit;
        _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        _panelSettings.match = 1f;
        _panelSettings.clearColor = false;
        _panelSettings.targetDisplay = 0;
        if (
            NativeGameTypography.TryAttachPanel(_panelSettings, out _typography)
                != NativeGameTypography.Outcome.Ready
            || _typography == null
        )
        {
            UnityEngine.Object.DestroyImmediate(_panelSettings);
            _panelSettings = null;
            return;
        }
        if (
            !NativeGameTitleOverlay.TryCreate(
                "CollectionPanelNativeTitle",
                _parent,
                BppOverlaySorting.NativeCardPreview,
                Sizes.FontTitle,
                Colors.White,
                UnicodeFontCoverage.ContainsCjk(CollectionPanelText.Title())
                    ? NativeGameTypography.OwnedTextRole.Body
                    : NativeGameTypography.OwnedTextRole.Heading,
                out _titleOverlay
            )
            || _titleOverlay == null
        )
        {
            _typography.Dispose();
            _typography = null;
            UnityEngine.Object.DestroyImmediate(_panelSettings);
            _panelSettings = null;
            return;
        }

        _rootObject = new GameObject("CollectionPanelUiToolkitRoot");
        _rootObject.transform.SetParent(_parent, false);
        _document = _rootObject.AddComponent<UIDocument>();
        _document.panelSettings = _panelSettings;
        _root = _document.rootVisualElement;
        _root.style.flexGrow = 1f;
        _root.style.position = Position.Absolute;
        _root.style.left = 0f;
        _root.style.right = 0f;
        _root.style.top = 0f;
        _root.style.bottom = 0f;
        _root.style.display = DisplayStyle.None;
        _typography.Apply(_root);
        _root.pickingMode = PickingMode.Position;

        BuildTree(_root);
        _titleOverlay.Attach(_title!);

        _gridViewport?.RegisterCallback<GeometryChangedEvent>(OnGridViewportGeometryChanged);
    }

    public void SetVisible(bool visible)
    {
        _targetOpacity = visible ? 1f : 0f;
        if (_root == null)
            return;
        if (visible)
        {
            // Show the element synchronously so the first ramp frame paints; opacity starts
            // wherever the previous tick left it (0 on first open, mid-fade if Close was
            // pressed during an in-flight open animation).
            _root.style.display = DisplayStyle.Flex;
            _root.style.opacity = _opacity;
            _titleOverlay?.SetVisible(true);
            _titleOverlay?.SetAlpha(_opacity);
        }
    }

    public float CurrentOpacity => _opacity;

    // True until the fade animation has settled and the root has finished hiding (if the
    // target was 0). CollectionPanel uses this to defer overlay deactivation + virtualizer
    // disposal until the visual fade-out is complete.
    public bool IsFadingOrVisible => _targetOpacity > 0f || _opacity > 0.005f;

    public void TickOpacity(float deltaSeconds)
    {
        if (_root == null || deltaSeconds <= 0f)
            return;
        TickSearchFocusPulse(deltaSeconds);
        if (Mathf.Approximately(_opacity, _targetOpacity))
        {
            if (_targetOpacity <= 0f && _root.style.display.value != DisplayStyle.None)
            {
                _root.style.display = DisplayStyle.None;
                _titleOverlay?.SetVisible(false);
            }
            return;
        }
        // Asymmetric tau: open is a presentation (slower, more deliberate); close is a
        // dismissal (snappier, the user just wants the panel gone).
        var tau =
            _targetOpacity > _opacity
                ? CollectionGridConstants.PanelFadeInSeconds
                : CollectionGridConstants.PanelFadeOutSeconds;
        var t = 1f - Mathf.Exp(-deltaSeconds / tau);
        _opacity = Mathf.Lerp(_opacity, _targetOpacity, t);
        if (Mathf.Abs(_opacity - _targetOpacity) < 0.005f)
            _opacity = _targetOpacity;
        _root.style.opacity = _opacity;
        _titleOverlay?.SetAlpha(_opacity);
        if (_targetOpacity <= 0f && _opacity <= 0.005f)
        {
            _root.style.display = DisplayStyle.None;
            _titleOverlay?.SetVisible(false);
        }
    }

    public void TickLoading(float deltaSeconds)
    {
        if (!_loadingVisible || _loadingLabel == null)
            return;

        _loadingFrameElapsed += Mathf.Max(0f, deltaSeconds);
        if (_loadingFrameElapsed < 0.16f)
            return;

        _loadingFrameElapsed = 0f;
        _loadingFrameIndex = (_loadingFrameIndex + 1) % LoadingFrames.Length;
        UpdateLoadingLabelText();
    }

    private void UpdateLoadingLabelText()
    {
        if (_loadingLabel == null)
            return;

        var message = string.IsNullOrWhiteSpace(_loadingMessage)
            ? CollectionPanelText.CatalogLoading()
            : _loadingMessage;
        _loadingLabel.text =
            $"{LoadingFrames[_loadingFrameIndex]} {StablePanelText.Compact(message, 180)}";
        _loadingLabel.tooltip = message;
    }

    // Snap the ScrollView to the top. Called on filter / tab changes so the user is not
    // stranded at the bottom of a tiny new visible set — the previous scrollOffset would
    // otherwise clamp to the new (smaller) max instead of returning to top.
    public void ResetScroll()
    {
        if (_gridScrollView != null)
            _gridScrollView.scrollOffset = new Vector2(_gridScrollView.scrollOffset.x, 0f);
    }

    public void ResetControlsScroll()
    {
        if (_controlsScrollView != null)
            _controlsScrollView.scrollOffset = new Vector2(_controlsScrollView.scrollOffset.x, 0f);
    }

    public void TickControlsScrollShadow()
    {
        if (_controlsScrollShadow == null || _controlsScrollView == null)
            return;

        _controlsScrollShadow.style.display =
            _controlsScrollView.scrollOffset.y > 0.5f ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void Refresh(CollectionPanelViewModel model)
    {
        if (_root == null)
            return;

        _title!.text = model.Title;
        _titleOverlay?.SetText(model.Title);
        BPPSupporterAttributionRow.Bind(_subtitle!, model.Supporters, model.Subtitle, _typography!);
        _statusLabel!.text = StablePanelText.Compact(model.StatusMessage, 150);
        _statusLabel.tooltip = model.StatusMessage ?? string.Empty;
        _statusLabel.style.display = string.IsNullOrWhiteSpace(model.StatusMessage)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
        _loadingVisible = model.IsLoading;
        _loadingMessage = model.StatusMessage ?? CollectionPanelText.CatalogLoading();
        if (_searchField != null && !string.Equals(_searchField.value, model.SearchQuery))
            _searchField.SetValueWithoutNotify(model.SearchQuery);
        RefreshSearchPlaceholder(model.SearchQuery);
        RefreshSearchMode(model);
        if (_loadingLabel != null)
        {
            _loadingLabel.style.display = model.IsLoading ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateLoadingLabelText();
        }

        RefreshChromeTexts(model.ActiveType);
        RefreshTabChoiceControl(model);

        KeywordIconSpriteProvider.BeginResolvePass();
        EnsureHeroChips(model.AvailableHeroes);
        EnsureTierChips(model.AvailableTiers);
        EnsureSizeChips(model.AvailableSizes);
        RefreshFacetChips(model);
        EnsureSourceChips(model.AvailableSources);
        RefreshTextMatchModeControl(_keywordMatchModeButton, model.KeywordMatchMode);
        RefreshTextMatchModeControl(_tagMatchModeButton, model.TagMatchMode);
        // Chip text is reset unconditionally on every Refresh: the Ensure*Chips early-exit
        // compares only keys, so a locale change while the chips survive would otherwise leave
        // their labels in the previous language (same P4 mechanism as RefreshChromeTexts).
        foreach (var pair in _heroChips)
        {
            pair.Value.tooltip = CollectionPanelText.Hero(pair.Key);
            RefreshHeroChip(
                pair.Key,
                pair.Value,
                model.AllHeroesSelected || model.SelectedHero == pair.Key
            );
        }
        RefreshAllHeroesButton(model.AllHeroesSelected);
        foreach (var pair in _tierChips)
        {
            pair.Value.text = CollectionPanelText.Tier(pair.Key);
            RefreshTierChip(pair.Key, pair.Value, model.SelectedTiers.Contains(pair.Key));
        }
        foreach (var pair in _sizeChips)
        {
            pair.Value.text = string.Empty;
            var icon = pair.Value.Q<VisualElement>(SizeChipIconName);
            if (icon != null)
                icon.style.display = CollectionPanelText.IsChineseLanguage()
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            var label = pair.Value.Q<Label>(SizeChipLabelName);
            if (label != null)
                label.text = CollectionPanelText.Size(pair.Key);
            RefreshSizeChip(pair.Value, model.SelectedSizes.Contains(pair.Key));
        }
        foreach (var pair in _tagChips)
        {
            var display = ResolveTagDisplay(pair.Key);
            ApplyTagChipContent(pair.Value, display);
            RefreshChip(pair.Value, model.SelectedTags.Contains(pair.Key), display.AccentColor);
        }
        foreach (var pair in _keywordChips)
        {
            var display = ResolveTagDisplay(pair.Key);
            ApplyTagChipContent(pair.Value, display, TagIconSize(pair.Key));
            RefreshChip(pair.Value, IsKeywordOptionSelected(pair.Key, model), display.AccentColor);
        }
        foreach (var pair in _sourceChips)
        {
            var selected = string.Equals(
                pair.Key,
                model.SelectedSourceKey,
                StringComparison.Ordinal
            );
            RefreshSourceChip(pair.Value, selected);
            RefreshSourceSelectionRing(pair.Value, selected);
            if (_sourceChipIcons.TryGetValue(pair.Key, out var sourceIcon))
                RefreshSourceEncounterHighlight(
                    sourceIcon,
                    model.EncounteredMerchantSourceKeys.Contains(pair.Key) && !selected
                );
            pair.Value.SetEnabled(model.SourceSelectorEnabled);
            pair.Value.style.opacity = model.SourceSelectorEnabled ? 1f : 0.58f;
        }
        if (_dayToggleButton != null)
        {
            RefreshDayToggle(model.DayFilterValue, model.DayFilterActive);
            _dayToggleButton.style.display = model.DayFilterVisible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _dayToggleButton.SetEnabled(model.DayFilterEnabled);
            _dayToggleButton.style.opacity = model.DayFilterEnabled ? 1f : 0.58f;
        }
        if (_sortQualityButton != null)
            RefreshSortChip(
                _sortQualityButton,
                model.SortPriority == CollectionSortPriority.Quality
            );
        if (_sortSizeButton != null)
            RefreshSortChip(_sortSizeButton, model.SortPriority == CollectionSortPriority.Size);
        ApplySortGroupLayout(model.SortPriority);

        // Size/tags only narrow Items. On Skills, let Quality fill the row and let source filters
        // move up naturally instead of reserving dead space.
        if (_heroFilterSection != null)
        {
            var showHeroChips = model.HeroFilterVisible;
            _heroFilterSection.style.display = showHeroChips
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _heroFilterSection.SetEnabled(model.HeroFilterEnabled);
            _heroFilterSection.style.opacity = model.HeroFilterEnabled ? 1f : 0.58f;
        }
        var showTierChips = model.TabProfile.ShowTierFilter;
        if (_tierFilterSection != null)
            _tierFilterSection.style.display = showTierChips
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        var showSizeChips = showTierChips && model.TabProfile.ShowSizeFilter;
        if (_sizeChipRow != null)
        {
            _sizeChipRow.style.display = showSizeChips ? DisplayStyle.Flex : DisplayStyle.None;
            _sizeChipRow.SetEnabled(showSizeChips);
            _sizeChipRow.pickingMode = showSizeChips ? PickingMode.Position : PickingMode.Ignore;
        }
        if (_tagFilterSection != null)
            _tagFilterSection.style.display = model.TabProfile.ShowTagFilter
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        if (_keywordFilterSection != null)
            _keywordFilterSection.style.display = model.TabProfile.ShowKeywordFilter
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        if (_sourceFilterSection != null)
            _sourceFilterSection.style.display =
                model.TabProfile.ShowSourceFilter && model.AvailableSources.Count > 0
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
        if (_sourceFilterLabel != null)
            _sourceFilterLabel.text = CollectionPanelText.SourceHeader(model.ActiveType);
        if (_tierFilterLabel != null)
            _tierFilterLabel.text = showSizeChips
                ? CollectionPanelText.TierSizeHeader()
                : CollectionPanelText.TierHeader();
        if (_keywordFilterLabel != null)
            _keywordFilterLabel.text = CollectionPanelText.KeywordHeader();

        UpdateContentSpacerHeight(model.ContentHeight);

        if (_emptyLabel != null)
        {
            var showEmpty =
                model.ContentHeight <= 0f
                && string.IsNullOrWhiteSpace(model.StatusMessage)
                && !model.IsLoading;
            _emptyLabel.style.display = showEmpty ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private static string TabLabel(
        CollectionTabKind tab,
        CollectionTabKind activeTab,
        int visibleCount
    )
    {
        var label =
            tab == CollectionTabKind.Items
                ? CollectionPanelText.ItemsTab()
                : CollectionPanelText.SkillsTab();
        return tab == activeTab ? $"{label}({visibleCount})" : label;
    }

    private void RefreshTabChoiceControl(CollectionPanelViewModel model)
    {
        var itemsSelected = model.ActiveTab == CollectionTabKind.Items;
        RefreshFacetChoiceControl(
            _tabModeControl,
            itemsSelected,
            TabLabel(CollectionTabKind.Items, model.ActiveTab, model.VisibleCount),
            TabLabel(CollectionTabKind.Skills, model.ActiveTab, model.VisibleCount),
            fontSize: Sizes.FontBody,
            slanted: false
        );
        if (_tabModeControl == null)
            return;

        var items = _tabModeControl.Q<Button>(FacetMatchAnyName);
        var skills = _tabModeControl.Q<Button>(FacetMatchAllName);
        var divider = _tabModeControl.Q<VisualElement>(FacetChoiceDividerName);
        if (items == null || skills == null || divider == null)
            return;

        var itemsWidth = itemsSelected
            ? Sizes.CollectionTabActiveWidth
            : Sizes.CollectionTabInactiveWidth;
        var skillsWidth = itemsSelected
            ? Sizes.CollectionTabInactiveWidth
            : Sizes.CollectionTabActiveWidth;
        UiStyle.FixedWidth(_tabModeControl.style, itemsWidth + skillsWidth);
        UiStyle.FixedWidth(items.style, itemsWidth);
        UiStyle.FixedWidth(skills.style, skillsWidth);
        UiStyle.HorizontalPadding(items.style, itemsSelected ? UiSpacing.Sm : UiSpacing.Xs);
        UiStyle.HorizontalPadding(skills.style, itemsSelected ? UiSpacing.Xs : UiSpacing.Sm);
        divider.style.left = itemsWidth;
    }

    // P4 fix: these chrome strings used to be set only at construction, so a locale change
    // while the view was alive (BPP Chinese script mode, or a non-restart game-language switch)
    // left them in the previous language. Re-resolving on every Refresh matches the existing
    // Title/Subtitle/Count per-refresh pattern.
    private void RefreshSearchMode(CollectionPanelViewModel _)
    {
        if (_standardOperationControls != null)
        {
            _standardOperationControls.SetEnabled(true);
            _standardOperationControls.pickingMode = PickingMode.Position;
            _standardOperationControls.style.display = DisplayStyle.Flex;
        }

        if (_searchInputContainer != null)
        {
            _searchInputContainer.SetEnabled(true);
            _searchInputContainer.pickingMode = PickingMode.Position;
            _searchInputContainer.style.display = DisplayStyle.Flex;
        }
    }

    private void RefreshChromeTexts(ECardType activeType)
    {
        if (_closeButton != null)
            _closeButton.tooltip = CollectionPanelText.Close();
        if (_searchField != null)
            _searchField.tooltip = CollectionPanelText.SearchTooltip();
        if (_searchPlaceholderLabel != null)
            _searchPlaceholderLabel.text = CollectionPanelText.SearchPlaceholder(activeType);
        if (_sortQualityButton != null)
            SetSortButtonText(_sortQualityButton, CollectionPanelText.SortQuality());
        if (_sortSizeButton != null)
            SetSortButtonText(_sortSizeButton, CollectionPanelText.SortSize());
        if (_dayToggleButton != null)
            _dayToggleButton.tooltip = CollectionPanelText.DayHeader();
        if (_heroFilterLabel != null)
            _heroFilterLabel.text = CollectionPanelText.HeroHeader();
        if (_allHeroesButton != null)
        {
            _allHeroesButton.text = CollectionPanelText.FacetMatchMode(
                CollectionFacetMatchMode.All
            );
            _allHeroesButton.tooltip = CollectionPanelText.AllHeroesTooltip();
        }
        if (_tierFilterLabel != null)
            _tierFilterLabel.text = CollectionPanelText.TierSizeHeader();
        if (_keywordFilterLabel != null)
            _keywordFilterLabel.text = CollectionPanelText.KeywordHeader();
        if (_disclaimerLabel != null)
        {
            _disclaimerLabel.text = CollectionPanelText.SourceDisclaimer();
            _disclaimerLabel.tooltip = _disclaimerLabel.text;
        }
        if (_emptyLabel != null)
            _emptyLabel.text = CollectionPanelText.NoMatches();
    }

    private void RefreshSearchPlaceholder(string? query)
    {
        if (_searchPlaceholderLabel != null)
            _searchPlaceholderLabel.style.display = string.IsNullOrEmpty(query)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
    }

    public void UpdateContentSpacerHeight(float contentHeightPixels)
    {
        if (_gridContentSpacer == null || _gridViewport == null)
            return;
        // ContentHeight is in overlay physical pixels, but the spacer height is UITK points and
        // ReadScrollYPixels multiplies scrollOffset (points) by scaledPixelsPerPoint to recover
        // the pixel offset the virtualizer consumes. So the spacer must be points = px / ppp, or
        // the scroll range and the card content diverge on any non-1.0 UI scale (1440p/4K), which
        // both strands the bottom rows and lets the view over-scroll into empty space. At ppp = 1
        // (1080p with the 1080 reference) this is a no-op.
        var ppp = _gridViewport.scaledPixelsPerPoint;
        if (ppp <= 0f)
            ppp = 1f;
        var height = Mathf.Max(0f, contentHeightPixels / ppp);
        _gridContentSpacer.style.height = height;
        _gridContentSpacer.style.minHeight = height;
    }

    internal bool IsTextInputFocused()
    {
        var focused = _root?.focusController?.focusedElement as VisualElement;
        if (focused == null)
            return false;

        var textField = focused as TextField ?? focused.GetFirstAncestorOfType<TextField>();
        return textField != null && IsVisibleAndEnabled(textField);
    }

    private bool IsVisibleAndEnabled(VisualElement element)
    {
        for (var current = element; current != null; current = current.parent)
        {
            if (current.style.display.value == DisplayStyle.None || !current.enabledInHierarchy)
                return false;

            if (current == _root)
                return true;
        }

        return false;
    }

    public float ReadScrollYPixels()
    {
        if (_gridScrollView == null || _gridViewport == null)
            return 0f;
        return _gridScrollView.scrollOffset.y * _gridViewport.scaledPixelsPerPoint;
    }

    public void Dispose()
    {
        _stagingIdCopyFeedbackGeneration++;
        _controlsDragScroller?.Dispose();
        _gridDragScroller?.Dispose();
        if (_rootObject != null)
            UnityEngine.Object.Destroy(_rootObject);
        _titleOverlay?.Dispose();
        _typography?.Dispose();
        if (_panelSettings != null)
            UnityEngine.Object.Destroy(_panelSettings);
        _rootObject = null;
        _document = null;
        _panelSettings = null;
        _typography = null;
        _titleOverlay = null;
        _root = null;
        _searchFrame = null;
        _searchField = null;
        _searchPlaceholderLabel = null;
        _searchFocused = false;
        _searchFocusPulseElapsed = 0f;
        _stagingIdCopyLabel = null;
        _controlsScrollView = null;
        _controlsScrollShadow = null;
        _controlsDragScroller = null;
        _gridDragScroller = null;
    }

    private void OnGridViewportGeometryChanged(GeometryChangedEvent evt)
    {
        if (_gridViewport == null)
            return;
        var worldBound = _gridViewport.worldBound;
        var ppp = _gridViewport.scaledPixelsPerPoint;
        var bounds = new Rect(
            Mathf.Round(worldBound.x * ppp),
            Mathf.Round(Screen.height - worldBound.yMax * ppp),
            Mathf.Max(1f, Mathf.Round(worldBound.width * ppp)),
            Mathf.Max(1f, Mathf.Round(worldBound.height * ppp))
        );
        if (bounds.width <= 0f || bounds.height <= 0f)
            return;
        if (RectApproximately(_lastGridBounds, bounds))
            return;
        _lastGridBounds = bounds;
        GridViewportBoundsChanged?.Invoke(bounds);
    }

    private static bool RectApproximately(Rect left, Rect right) =>
        Mathf.Approximately(left.x, right.x)
        && Mathf.Approximately(left.y, right.y)
        && Mathf.Approximately(left.width, right.width)
        && Mathf.Approximately(left.height, right.height);
}
