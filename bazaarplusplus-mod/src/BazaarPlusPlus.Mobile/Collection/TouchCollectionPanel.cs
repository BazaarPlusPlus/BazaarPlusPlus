using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using BazaarPlusPlus.Mobile.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Mobile.Collection;

internal sealed class TouchCollectionPanel
{
    private readonly TouchCollectionData _data;
    private readonly TouchCollectionGridPort _port = new();
    private readonly CollectionViewState _state;
    private readonly Transform _parent;
    private readonly TMP_FontAsset _font;
    private IReadOnlyList<BPPSupporterSample> _headerSupporters = Array.Empty<BPPSupporterSample>();
    private TextMeshProUGUI _supporterLabel = null!;
    private readonly Action<string> _requestClose;
    private readonly List<ScrollTracker> _trackers = new();
    private CollectionTouchLayout _layout = null!;

    private readonly TouchViewSlot<GameObject> _tree;

    // While open, defer relayout until card setup has settled. Open handles screen changes immediately.
    private readonly TouchRelayoutDebounce _relayout = new(string.Empty);
    private NativeSkin _skin = null!;
    private TouchPlates? _plates;
    private TouchCollectionGrid _grid = null!;
    private TouchCollectionInspection _inspection = null!;
    private RectTransform _railContent = null!;
    private ScrollRect _railScroll = null!;
    private (Image Background, TextMeshProUGUI Label) _itemsTab;
    private (Image Background, TextMeshProUGUI Label) _skillsTab;
    private TMP_InputField _search = null!;
    private TextMeshProUGUI _searchPlaceholder = null!;
    private TextMeshProUGUI _status = null!;
    private string _query = string.Empty;
    private bool _loading;
    private bool _treeLost;
    private bool _advancedFilters;
    private RailPicker _expandedPicker;
    private float _expandedPickerTop;
    private CollectionPanelViewModel? _railModel;

    private enum RailPicker
    {
        None,
        Hero,
        Source,
    }

    public TouchCollectionPanel(
        Transform parent,
        TMP_FontAsset font,
        TouchCollectionData data,
        Action<string> requestClose
    )
    {
        _parent = parent;
        _font = font;
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _requestClose = requestClose ?? throw new ArgumentNullException(nameof(requestClose));
        _state = new CollectionViewState(_port, data.Sources, data.DayTiers, data.HeroPreference);
        _tree = new TouchViewSlot<GameObject>(DropTree);
        var key = TouchScreen.Key();
        if (
            _tree.Rebuild(key, () => Build(key), exception => LayoutFailed(exception, "", false))
            != null
        )
            _relayout.LaidOutAt(key);
    }

    // Through a typed local, so Unity's equality also reads a destroyed root as gone.
    public bool IsOpen
    {
        get
        {
            var root = _tree.View;
            return root != null && root.activeSelf;
        }
    }

    public long Relayouts { get; private set; }

    public bool Open(string source)
    {
        if (IsOpen)
            return true;
        if (
            (_tree.View == null || _skin.NeedsRefresh || !_tree.IsCurrent(TouchScreen.Key()))
            && !Relayout()
        )
            return false;
        CollapseFilters();
        _headerSupporters = Array.Empty<BPPSupporterSample>();
        _data.Supporters?.Warm();
        RefreshSupporterButton(sample: true);
        var root = _tree.View!;
        _data.Catalog.RetryIfUnavailable();
        _query = string.Empty;
        _search.SetTextWithoutNotify(string.Empty);
        var cached = _data.Catalog.Cached;
        var inRun = _data.Run.IsInRun();
        var day = inRun ? _data.Run.Day() : null;
        var opened = _state.Open(
            inRun
                ? CollectionOpenFacts.InRun(
                    _data.Run.Hero(),
                    day,
                    EncounterIds(),
                    cached,
                    Array.Empty<BPPSupporterSample>()
                )
                : CollectionOpenFacts.OutOfRun(cached, Array.Empty<BPPSupporterSample>())
        );
        root.SetActive(true);
        root.transform.SetAsLastSibling();
        if (cached != null)
        {
            // Accept supersedes the render from Open. Preserve Open's scroll resets when drawing once.
            var accepted = _state.AcceptCatalog(cached);
            Render(
                new CollectionRenderOutcome(
                    accepted.Model,
                    accepted.ResetScroll || opened.Render.ResetScroll,
                    accepted.ResetControlsScroll || opened.Render.ResetControlsScroll
                )
            );
        }
        else
        {
            Render(opened.Render);
            _loading = true;
            Render(_state.BeginCatalogLoad());
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.collection.opened"),
            ("source", source),
            ("cached", cached != null),
            ("in_run", inRun),
            ("hero", opened.Selection.SelectedHero),
            ("day", day)
        );
        return true;
    }

    public void Close(string reason)
    {
        // Hero taps only stage the preference; it reaches disk once per close.
        _data.HeroPreference.Flush();
        var treeLost = _treeLost;
        _treeLost = false;
        if (!IsOpen && !treeLost)
            return;
        var returned = 0;
        if (!treeLost)
        {
            _inspection.Close(reason);
            _grid.Scroll.enabled = true;
        }
        _loading = false;
        _state.Close();
        if (!treeLost)
        {
            returned = _grid.Leases;
            _grid.ReturnAll();
            _tree.View!.SetActive(false);
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.collection.closed"),
            ("reason", reason),
            ("returned", returned),
            ("rental_leases", MobilePlatform.Port.CardRental.ActiveLeases),
            ("rental_balanced", MobilePlatform.Port.CardRental.IsBalanced)
        );
    }

    public void FlushHeroPreference() => _data.HeroPreference.Flush();

    public bool HasModalBackStep =>
        IsOpen
        && (_inspection.IsOpen || (_expandedPicker != RailPicker.None && _railModel != null));

    public void DismissModalBackStep()
    {
        if (!IsOpen || _inspection.HandleBack())
            return;
        if (_expandedPicker != RailPicker.None && _railModel is { } model)
            SelectRail(model, null, "collapse");
    }

    public void Tick()
    {
        if (!IsOpen)
            return;
        if (
            _relayout.Observe(TouchScreen.Key(), _grid.SettingUp || _inspection.SettingUp)
            && !Relayout()
        )
        {
            _requestClose("relayout_failed");
            return;
        }
        if (_headerSupporters.Count == 0)
            RefreshSupporterButton(sample: true);
        if (_loading)
            AdvanceCatalog();
        _inspection.Tick();
        _grid.Scroll.enabled = !_inspection.IsOpen;
        if (!_inspection.IsOpen)
        {
            var search = _state.TickSearch(Time.unscaledDeltaTime, false);
            if (search != null)
                Render(search, "search");
            _grid.Tick();
        }
        var pressed = ScrollTracker.PointerPressed();
        foreach (var tracker in _trackers)
            tracker.Tick(pressed, _layout.Metrics.UnitsPerDp);
    }

    private GameObject Build(string key)
    {
        var layout = _layout = CollectionTouchLayout.For(TouchScreen.Read());
        _port.Touch = layout;
        _plates ??= TouchPlates.TryLoad();
        _skin = new NativeSkin(_plates, HistoryHeaderArtwork.TouchMultiplier(layout.Metrics));
        var chrome = layout.Chrome;
        var metrics = layout.Metrics;
        var root = UiKit.Rect(
            "Collection",
            _parent,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        var background = UiKit.PanelColor;
        background.a = 1f;
        try
        {
            BuildContent(root, background, layout);
        }
        catch
        {
            UnityEngine.Object.Destroy(root.gameObject);
            throw;
        }
        var dp = metrics.UnitsPerDp;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.collection.laid_out"),
            ("screen", key),
            ("units_per_dp", Math.Round(dp, 3)),
            ("grid_dp", Math.Round(layout.Grid.Width / dp)),
            ("rail_dp", Math.Round(layout.Rail.Width / dp)),
            ("search_dp", Math.Round(layout.Search.Width / dp)),
            ("item_columns", layout.GridColumns(CardType.Item, layout.Grid.Width)),
            ("skill_columns", layout.GridColumns(CardType.Skill, layout.Grid.Width)),
            ("target_dp", Math.Round(metrics.MinTarget / dp, 1)),
            ("body_sp", Math.Round(chrome.BodyFont / dp, 1))
        );
        return root.gameObject;
    }

    private void BuildContent(RectTransform root, Color background, CollectionTouchLayout layout)
    {
        var chrome = layout.Chrome;
        var metrics = layout.Metrics;
        UiKit.Fill(root, background);

        var gridArea = TouchScreen.Child("Grid", root, layout.Grid);
        _grid = new TouchCollectionGrid(gridArea, _port, Inspect, _font, chrome.BodyFont);
        // Rented cards carry canvases that bypass RectMask2D. Draw opaque bands over
        // the grid edges to hide cards scrolled out of view.
        Band(root, background, new TouchRect(0f, 0f, metrics.CanvasWidth, layout.Grid.Top));
        Band(
            root,
            background,
            new TouchRect(
                0f,
                layout.Grid.Bottom,
                metrics.CanvasWidth,
                Math.Max(0f, metrics.CanvasHeight - layout.Grid.Bottom)
            )
        );
        _status = UiKit.Text(
            TouchScreen.Child("Status", root, layout.Grid),
            _font,
            "",
            chrome.ButtonFont,
            TextAlignmentOptions.Center,
            UiKit.MutedTextColor
        );

        (_, _, _supporterLabel) = _skin.Button(
            TouchScreen.Child("Supporter", root, layout.Title),
            _font,
            BPPSupporterAttributionText.FormatSponsorAction(L.CurrentLanguageCode),
            chrome.ButtonFont,
            chrome.LabelInset,
            () =>
                UiActions.Run(
                    "collection.sponsor",
                    () =>
                        Application.OpenURL(
                            BPPSupporterLinks.ResolveSponsorUrl(L.CurrentLanguageCode)
                        )
                )
        );
        Il2CppUi.SetWordWrapping(_supporterLabel, false);
        _supporterLabel.enableAutoSizing = true;
        _supporterLabel.fontSizeMin = chrome.BodyFont;
        _supporterLabel.fontSizeMax = chrome.ButtonFont;
        _supporterLabel.overflowMode = TextOverflowModes.Ellipsis;
        RefreshSupporterButton();
        _skin.Button(
            TouchScreen.Child("Close", root, layout.Close),
            _font,
            L.Resolve(new LocalizedTextSet("Back", "返回", "返回")),
            chrome.TitleFont,
            chrome.LabelInset,
            () => _requestClose("close_button")
        );
        _itemsTab = Tab(root, layout.ItemsTab, CollectionTabKind.Items);
        _skillsTab = Tab(root, layout.SkillsTab, CollectionTabKind.Skills);
        (_search, _searchPlaceholder) = Search(root, layout.Search, chrome);

        var rail = TouchScreen.Child("Rail", root, layout.Rail);
        _skin.Frame(
            UiKit.Rect("RailFrame", rail, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero)
        );
        var inset = metrics.Dp(16f);
        var edge = metrics.Dp(12f);
        var railInner = UiKit.Rect(
            "RailInner",
            rail,
            Vector2.zero,
            Vector2.one,
            new(inset, edge),
            new(-inset, -edge)
        );
        (_railScroll, _railContent) = UiKit.VerticalScroll(railInner);

        _inspection = new TouchCollectionInspection(root, _font, layout, _skin);
        _trackers.Clear();
        _trackers.Add(new ScrollTracker(Scrolled(), "grid", _grid.Scroll));
        _trackers.Add(new ScrollTracker(Scrolled(), "rail", _railScroll));
        _trackers.Add(new ScrollTracker(Scrolled(), "inspect", _inspection.DetailScroll));
        root.gameObject.SetActive(false);
    }

    private static BppLogEvent Scrolled() =>
        new(BppLogFeatureScope.Touch, "touch.collection.scrolled");

    private bool Relayout()
    {
        var before = _tree.ScreenKey;
        var open = IsOpen;
        var key = TouchScreen.Key();
        var root = _tree.Rebuild(
            key,
            () => Build(key),
            exception => LayoutFailed(exception, before, open)
        );
        if (root == null)
            return false;
        _relayout.LaidOutAt(key);
        Relayouts++;
        if (open)
        {
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            _search.SetTextWithoutNotify(_query);
            Render(_state.Rebuild());
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.collection.relaid_out"),
            ("from", before),
            ("to", _tree.ScreenKey),
            ("open", open),
            ("rental_leases", MobilePlatform.Port.CardRental.ActiveLeases),
            ("held_frames", _relayout.HeldFrames)
        );
        return true;
    }

    private void DropTree(GameObject root)
    {
        _inspection.Close("relayout");
        _grid.ReturnAll();
        UnityEngine.Object.Destroy(root);
    }

    private void LayoutFailed(Exception exception, string before, bool open)
    {
        _grid?.ReturnAll();
        _treeLost = open;
        BppLog.ErrorEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.collection.relayout_failed"),
            exception,
            ("from", before),
            ("open", open),
            ("rental_leases", MobilePlatform.Port.CardRental.ActiveLeases)
        );
    }

    private void AdvanceCatalog()
    {
        switch (_data.Catalog.Advance())
        {
            case CollectionCardCatalogStatus.Ready when _data.Catalog.Cached is { } cards:
                _loading = false;
                Render(_state.AcceptCatalog(cards));
                break;
            case CollectionCardCatalogStatus.Unavailable:
                _loading = false;
                Render(_state.CatalogUnavailable());
                break;
        }
    }

    private void Inspect(CollectionCardVm card, RectTransform? source)
    {
        _grid.Scroll.StopMovement();
        _grid.Scroll.enabled = false;
        _inspection.Open(card, source);
    }

    private void Command(CollectionRenderOutcome? outcome, string command)
    {
        if (outcome != null)
            Render(outcome, command);
    }

    private void RefreshSupporterButton(bool sample = false)
    {
        if (sample)
        {
            _headerSupporters = _data.Supporters?.Next(1) ?? Array.Empty<BPPSupporterSample>();
            if (_headerSupporters.Count == 0)
                return;
        }
        var credit = BPPSupporterAttribution.FormatRichLine(
            _headerSupporters,
            L.CurrentLanguageCode
        );
        _supporterLabel.text = string.IsNullOrEmpty(credit)
            ? BPPSupporterAttributionText.FormatSponsorAction(L.CurrentLanguageCode)
            : credit;
        MobilePlatform.Port.CoverText(_supporterLabel);
    }

    private void Render(CollectionRenderOutcome outcome, string? command = null)
    {
        var model = outcome.Model;
        RefreshSupporterButton();
        RenderTab(_itemsTab, CollectionTabKind.Items, CollectionPanelText.ItemsTab(), model);
        RenderTab(_skillsTab, CollectionTabKind.Skills, CollectionPanelText.SkillsTab(), model);
        _searchPlaceholder.text = CollectionPanelText.SearchPlaceholder(model.ActiveType);
        // Search does not change catalog facets. Keep existing chips bound to equivalent selections.
        if (
            command == "search"
            && _railModel is { } drawn
            && drawn.DayFilterValue == model.DayFilterValue
            && drawn.SourceSelectorEnabled == model.SourceSelectorEnabled
        )
            _railModel = model;
        else
            RenderRail(model);
        _status.text =
            model.StatusMessage
            ?? (
                model.VisibleCount == 0
                && !model.IsLoading
                && _state.CatalogReadiness == CollectionCatalogReadiness.Accepted
                    ? CollectionPanelText.NoMatches()
                    : string.Empty
            );
        if (outcome.ResetScroll)
            _grid.ResetScroll();
        if (outcome.ResetControlsScroll)
        {
            _railScroll.StopMovement();
            _railContent.anchoredPosition = Vector2.zero;
        }
        foreach (var tracker in _trackers)
            tracker.Sync();
        if (command != null)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Touch, "touch.collection.filtered"),
                ("command", command),
                ("tab", model.ActiveTab),
                ("visible", model.VisibleCount),
                ("columns", _port.Layout.Columns)
            );
    }

    private void RenderTab(
        (Image Background, TextMeshProUGUI Label) tab,
        CollectionTabKind kind,
        string label,
        CollectionPanelViewModel model
    )
    {
        var active = model.ActiveTab == kind;
        tab.Label.text = active ? $"{label}({model.VisibleCount})" : label;
        _skin.Select(tab.Background, active);
    }

    private void RenderRail(CollectionPanelViewModel model)
    {
        _railModel = model;
        for (var index = _railContent.childCount - 1; index >= 0; index--)
        {
            var child = _railContent.GetChild(index).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
        var flow = new ChipFlow(
            _railContent,
            _font,
            _layout.Chrome,
            CollectionTouchLayout.RailColumns,
            _layout.RailHeaderHeight,
            _skin
        );
        var profile = model.TabProfile;

        var allLabel = CollectionPanelText.FacetMatchMode(CollectionFacetMatchMode.All);
        var selectedHero = model.SelectedHero ?? CardHero.Common;
        var heroValue = model.AllHeroesSelected
            ? L.Resolve(new LocalizedTextSet("All heroes", "全部英雄", "全部英雄"))
            : TouchCollectionLabels.Hero(selectedHero);
        var selectedSource = model.AvailableSources.FirstOrDefault(source =>
            string.Equals(source.SourceKey, model.SelectedSourceKey, StringComparison.Ordinal)
        );
        var sourceValue =
            selectedSource != null
                ? MobilePlatform.Port.EncounterName(
                    selectedSource.RepresentativeTemplateId,
                    selectedSource.DisplayName
                )
                : L.Resolve(
                    model.ActiveType == CardType.Skill
                        ? new LocalizedTextSet("All trainers", "全部训练师", "全部訓練師")
                        : new LocalizedTextSet("All merchants", "全部商人", "全部商人")
                );
        _expandedPickerTop = flow.Height;
        flow.Identity(
            heroValue,
            _expandedPicker == RailPicker.Hero,
            () => TogglePicker(RailPicker.Hero, model),
            model.AllHeroesSelected || selectedHero == CardHero.Common
                ? null
                : () => MobilePlatform.Port.HeroPortrait(selectedHero),
            span: profile.ShowSourceFilter ? 3 : 6,
            expanded: _expandedPicker == RailPicker.Hero,
            hero: selectedHero
        );
        if (profile.ShowSourceFilter)
            flow.Identity(
                sourceValue,
                _expandedPicker == RailPicker.Source,
                () =>
                {
                    if (model.SourceSelectorEnabled)
                        TogglePicker(RailPicker.Source, model);
                },
                selectedSource == null
                    ? null
                    : () =>
                        MobilePlatform.Port.EncounterPortrait(
                            selectedSource.RepresentativeTemplateId
                        ),
                expanded: _expandedPicker == RailPicker.Source
            );
        flow.Break();
        if (_expandedPicker == RailPicker.Hero)
        {
            flow.Chip(
                allLabel,
                model.AllHeroesSelected,
                () =>
                    SelectRail(
                        model,
                        model.AllHeroesSelected ? null : _state.ToggleAllHeroes(),
                        "all_heroes"
                    ),
                span: 6
            );
            foreach (var hero in new[] { CardHero.Common }.Concat(model.AvailableHeroes).Distinct())
            {
                var chosen = hero;
                var selected = !model.AllHeroesSelected && selectedHero == hero;
                flow.Identity(
                    TouchCollectionLabels.Hero(hero),
                    selected,
                    () => SelectRail(model, selected ? null : _state.ToggleHero(chosen), "hero"),
                    hero == CardHero.Common ? null : () => MobilePlatform.Port.HeroPortrait(chosen),
                    span: 6,
                    hero: chosen
                );
            }
            flow.Break();
        }
        if (
            _expandedPicker == RailPicker.Source
            && profile.ShowSourceFilter
            && model.SourceSelectorEnabled
        )
        {
            flow.Chip(
                allLabel,
                string.IsNullOrEmpty(model.SelectedSourceKey),
                () =>
                    SelectRail(
                        model,
                        string.IsNullOrEmpty(model.SelectedSourceKey)
                            ? null
                            : _state.ToggleSource(model.SelectedSourceKey),
                        "source"
                    ),
                span: 6
            );
            foreach (var source in model.AvailableSources)
            {
                var key = source.SourceKey;
                var templateId = source.RepresentativeTemplateId;
                var selected = string.Equals(
                    model.SelectedSourceKey,
                    key,
                    StringComparison.Ordinal
                );
                flow.Identity(
                    MobilePlatform.Port.EncounterName(templateId, source.DisplayName),
                    selected,
                    () => SelectRail(model, selected ? null : _state.ToggleSource(key), "source"),
                    () => MobilePlatform.Port.EncounterPortrait(templateId),
                    span: 6
                );
            }
            flow.Note(CollectionPanelText.SourceDisclaimer(), UiKit.MutedTextColor);
        }
        flow.Divider();
        // Keep sizes and tiers together so a divider cannot push the last chip below the folded rail.
        if (profile.ShowSizeFilter)
        {
            foreach (var size in model.AvailableSizes)
            {
                var chosen = size;
                flow.Chip(
                    CollectionPanelText.Size(size),
                    model.SelectedSizes.Contains(size),
                    () => Command(_state.ToggleSize(chosen), "size"),
                    span: 2
                );
            }
            flow.Break();
        }
        if (profile.ShowTierFilter)
        {
            foreach (var tier in model.AvailableTiers)
            {
                var chosen = tier;
                flow.Chip(
                    CollectionPanelText.Tier(tier),
                    model.SelectedTiers.Contains(tier),
                    () => Command(_state.ToggleTier(chosen), "tier"),
                    span: 2
                );
            }
        }

        var activeFacets =
            model.SelectedKeywords.Count + model.SelectedMechanics.Count + model.SelectedTags.Count;
        var advancedLabel = L.Resolve(
            _advancedFilters
                ? new LocalizedTextSet("Less", "收起", "收起")
                : new LocalizedTextSet("More", "更多", "更多")
        );
        flow.Break();
        var moreTop = flow.Height;
        flow.Chip(
            activeFacets > 0 ? $"{advancedLabel}·{activeFacets}" : advancedLabel,
            _advancedFilters,
            () =>
            {
                _advancedFilters = !_advancedFilters;
                RenderRail(model);
                _railScroll.StopMovement();
                var viewportHeight = _railScroll.viewport.rect.height;
                var max = Mathf.Max(0f, _railContent.rect.height - viewportHeight);
                _railContent.anchoredPosition = new Vector2(
                    0f,
                    _advancedFilters ? Mathf.Min(moreTop, max) : 0f
                );
            },
            span: 2
        );
        flow.Break();
        if (_advancedFilters)
        {
            flow.Chip(
                CollectionPanelText.ResetFilters(),
                false,
                () =>
                {
                    CollapseFilters();
                    Command(_state.ResetFilters(), "reset");
                },
                span: 6
            );
            flow.Break();
            if (model.DayFilterValue is { } day)
            {
                flow.Chip(
                    $"{CollectionPanelText.DayHeader()} {day}",
                    model.DayFilterActive,
                    () => Command(_state.ToggleRunDayFilter(), "day"),
                    span: 6
                );
                flow.Break();
            }

            flow.Header(L.Resolve(new LocalizedTextSet("Sort", "排序", "排序")));
            flow.Chip(
                CollectionPanelText.SortQuality(),
                model.SortPriority == CollectionSortPriority.Quality,
                () => Command(_state.SetSortPriority(CollectionSortPriority.Quality), "sort"),
                span: 3
            );
            flow.Chip(
                CollectionPanelText.SortSize(),
                model.SortPriority == CollectionSortPriority.Size,
                () => Command(_state.SetSortPriority(CollectionSortPriority.Size), "sort"),
                span: 3
            );
            if (profile.ShowKeywordFilter && model.AvailableKeywordOptions.Count > 0)
            {
                flow.Header(CollectionPanelText.KeywordHeader());
                MatchModes(flow, model.KeywordMatchMode, mode => _state.SetKeywordMatchMode(mode));
                foreach (var option in model.AvailableKeywordOptions)
                {
                    var chosen = option;
                    var selected = option.Keyword is { } keyword
                        ? model.SelectedKeywords.Contains(keyword)
                        : option.Mechanic is { } mechanic
                            && model.SelectedMechanics.Contains(mechanic);
                    flow.Chip(
                        TouchCollectionLabels.Keyword(option),
                        selected,
                        () => Command(_state.ToggleKeyword(chosen), "keyword"),
                        span: 2
                    );
                }
            }

            if (profile.ShowTagFilter && model.AvailableTags.Count > 0)
            {
                flow.Header(CollectionPanelText.TagHeader());
                MatchModes(flow, model.TagMatchMode, mode => _state.SetTagMatchMode(mode));
                foreach (var tag in model.AvailableTags)
                {
                    var chosen = tag;
                    flow.Chip(
                        TouchCollectionLabels.Tag(tag),
                        model.SelectedTags.Contains(tag),
                        () => Command(_state.ToggleTag(chosen), "tag"),
                        span: 2
                    );
                }
            }
        }

        flow.Break();
        _railContent.sizeDelta = new Vector2(0f, flow.Height + _layout.Gap);
    }

    private void CollapseFilters()
    {
        _expandedPicker = RailPicker.None;
        _advancedFilters = false;
    }

    private void TogglePicker(RailPicker picker, CollectionPanelViewModel model)
    {
        _expandedPicker = _expandedPicker == picker ? RailPicker.None : picker;
        RenderRail(model);
        _railScroll.StopMovement();
        var max = Mathf.Max(0f, _railContent.rect.height - _railScroll.viewport.rect.height);
        _railContent.anchoredPosition = new Vector2(
            0f,
            _expandedPicker == RailPicker.None ? 0f : Mathf.Min(_expandedPickerTop, max)
        );
    }

    private void SelectRail(
        CollectionPanelViewModel model,
        CollectionRenderOutcome? outcome,
        string command
    )
    {
        _expandedPicker = RailPicker.None;
        if (outcome != null)
            Command(outcome, command);
        else
            RenderRail(model);
        _railScroll.StopMovement();
        _railContent.anchoredPosition = Vector2.zero;
    }

    private void MatchModes(
        ChipFlow flow,
        CollectionFacetMatchMode current,
        Func<CollectionFacetMatchMode, CollectionRenderOutcome?> set
    )
    {
        foreach (var mode in new[] { CollectionFacetMatchMode.Any, CollectionFacetMatchMode.All })
        {
            var chosen = mode;
            flow.Chip(
                CollectionPanelText.FacetMatchMode(mode),
                current == mode,
                () => Command(set(chosen), "match_mode"),
                span: 3
            );
        }
        flow.Break();
    }

    private (Image Background, TextMeshProUGUI Label) Tab(
        RectTransform root,
        TouchRect area,
        CollectionTabKind kind
    )
    {
        var (_, background, label) = _skin.Button(
            TouchScreen.Child(kind.ToString(), root, area),
            _font,
            "",
            _layout.Chrome.ButtonFont,
            _layout.Chrome.LabelInset,
            () =>
            {
                CollapseFilters();
                var outcome = _state.SetActiveTab(kind);
                if (outcome != null)
                    Command(outcome, "tab");
                else if (_railModel is { } model)
                    SelectRail(model, null, "tab");
            }
        );
        Il2CppUi.SetWordWrapping(label, false);
        label.enableAutoSizing = true;
        label.fontSizeMin = _layout.Chrome.BodyFont;
        label.fontSizeMax = _layout.Chrome.ButtonFont;
        return (background, label);
    }

    private (TMP_InputField Field, TextMeshProUGUI Placeholder) Search(
        RectTransform root,
        TouchRect area,
        TouchChrome chrome
    )
    {
        var rect = TouchScreen.Child("Search", root, area);
        _skin.Frame(rect);
        var inset = chrome.Gap;
        var textArea = UiKit.Rect(
            "TextArea",
            rect,
            Vector2.zero,
            Vector2.one,
            new(inset * 1.5f, inset * 0.5f),
            new(-inset * 1.5f, -inset * 0.5f)
        );
        textArea.gameObject.AddComponent<RectMask2D>();
        var placeholder = UiKit.Text(
            UiKit.Rect(
                "Placeholder",
                textArea,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero
            ),
            _font,
            "",
            chrome.ButtonFont,
            TextAlignmentOptions.MidlineLeft,
            UiKit.MutedTextColor
        );
        Il2CppUi.SetWordWrapping(placeholder, false);
        placeholder.overflowMode = TextOverflowModes.Ellipsis;
        var text = UiKit.Text(
            UiKit.Rect("Text", textArea, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
            _font,
            "",
            chrome.ButtonFont,
            TextAlignmentOptions.MidlineLeft,
            UiKit.TextColor
        );
        Il2CppUi.SetWordWrapping(text, false);
        // Configured while inactive: the field's OnEnable reads its text component.
        rect.gameObject.SetActive(false);
        var field = rect.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = textArea;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.fontAsset = _font;
        field.pointSize = chrome.ButtonFont;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 64;
        Il2CppUi.OnTextChanged(field, OnSearchChanged);
        rect.gameObject.SetActive(true);
        return (field, placeholder);
    }

    private void OnSearchChanged(string query)
    {
        _query = query ?? string.Empty;
        _state.SetSearchQuery(_query);
    }

    private static void Band(RectTransform root, Color color, TouchRect area)
    {
        if (area.Height <= 0f)
            return;
        UiKit.Fill(TouchScreen.Child("GridEdge", root, area), color);
    }

    private EncounterIdsSnapshot EncounterIds()
    {
        try
        {
            return _data.Encounters.GetEncounterIdsOutcome().Snapshot;
        }
        catch (Exception)
        {
            return EncounterIdsSnapshot.Empty;
        }
    }
}
