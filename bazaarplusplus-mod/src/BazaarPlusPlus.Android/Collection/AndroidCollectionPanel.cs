using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.Supporters;
using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>
/// The Collection panel on Android (#396), entered from the touch menu. Core's
/// <see cref="CollectionViewState"/> owns every filter, the search, the sort and the render model,
/// as it does on desktop; this class draws that model in uGUI over the touch root and forwards each
/// tap back as a view-state command. The geometry is Core's <see cref="CollectionTouchLayout"/>
/// inside the safe area (#425): the shared header (title, tabs, search, close), the filter rail and
/// a grid whose column count follows its width, laid out again when the screen changes. The grid
/// shows the game's own card prefabs through the card rental. It is a Main Overlay Panel: the
/// Overlay Panel Host opens and closes it (<see cref="AndroidOverlayPanels"/>), and its own close
/// button and a failed relayout request the close. Differences from desktop are listed in
/// <c>docs/architecture/collection.md</c>.
/// </summary>
internal sealed class AndroidCollectionPanel
{
    private readonly AndroidCollectionCatalog _catalog = new();
    private readonly AndroidCollectionGridPort _port = new();
    private readonly EmbeddedCollectionSourceCatalog _sources = new(
        AndroidCardFacets.IsHeroAvailable
    );
    private readonly AndroidGameStateProbe _gameState = new();
    private readonly AndroidCollectionHeroPreferenceStore _heroStore = new();
    private readonly CollectionViewState _state;
    private readonly Transform _parent;
    private readonly TMP_FontAsset _font;
    private readonly AndroidSupporterData? _supporters;
    private IReadOnlyList<BPPSupporterSample> _headerSupporters = Array.Empty<BPPSupporterSample>();
    private TextMeshProUGUI _supporterLabel = null!;
    private readonly Action<string> _requestClose;
    private readonly List<AndroidScrollTracker> _trackers = new();
    private CollectionTouchLayout _layout = null!;

    // Null only after a relayout failed to build its tree; Open builds again.
    private GameObject? _root;
    private AndroidNativeSkin _skin = null!;
    private AndroidCollectionGrid _grid = null!;
    private AndroidCollectionInspection _inspection = null!;
    private RectTransform _railContent = null!;
    private ScrollRect _railScroll = null!;
    private (Image Background, TextMeshProUGUI Label) _itemsTab;
    private (Image Background, TextMeshProUGUI Label) _skillsTab;
    private TMP_InputField _search = null!;
    private TextMeshProUGUI _searchPlaceholder = null!;
    private TextMeshProUGUI _status = null!;
    private string _screenKey = string.Empty;
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

    public AndroidCollectionPanel(
        Transform parent,
        TMP_FontAsset font,
        AndroidSupporterData? supporters,
        Action<string> requestClose
    )
    {
        _parent = parent;
        _font = font;
        _supporters = supporters;
        _requestClose = requestClose ?? throw new ArgumentNullException(nameof(requestClose));
        _state = new CollectionViewState(
            _port,
            _sources,
            new AndroidCollectionDayTierSource(IsInRun),
            _heroStore
        );
        Build();
    }

    public bool IsOpen => _root != null && _root.activeSelf;

    /// <summary>Times the open panel was laid out again for a new screen.</summary>
    public long Relayouts { get; private set; }

    /// <summary>False when no tree could be built for the screen: the panel stays closed.</summary>
    public bool Open()
    {
        if (IsOpen)
            return true;
        if (
            (
                _root == null
                || _skin.NeedsRefresh
                || !string.Equals(AndroidTouchScreen.Key(), _screenKey, StringComparison.Ordinal)
            ) && !Relayout()
        )
            return false;
        CollapseFilters();
        _headerSupporters = Array.Empty<BPPSupporterSample>();
        _supporters?.Warm();
        RefreshSupporterButton(sample: true);
        var root = _root!;
        _catalog.RetryIfUnavailable();
        _query = string.Empty;
        _search.SetTextWithoutNotify(string.Empty);
        var cached = _catalog.Cached;
        var inRun = IsInRun();
        var day = inRun ? AndroidCollectionDayTierSource.CurrentDay() : null;
        // A failed hero-preference load opens on the default hero; this host does not report it.
        var opened = _state.Open(
            inRun
                ? CollectionOpenFacts.InRun(
                    CurrentHero(),
                    day,
                    // This host composes NoEncounterState (AndroidComposition).
                    EncounterIdsSnapshot.Empty,
                    cached,
                    Array.Empty<BPPSupporterSample>()
                )
                : CollectionOpenFacts.OutOfRun(cached, Array.Empty<BPPSupporterSample>())
        );
        root.SetActive(true);
        root.transform.SetAsLastSibling();
        Render(opened.Render);
        if (cached != null)
            Render(_state.AcceptCatalog(cached));
        else
        {
            _loading = true;
            Render(_state.BeginCatalogLoad());
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.opened"),
            ("cached", cached != null),
            ("in_run", inRun),
            ("hero", opened.Selection.SelectedHero),
            ("day", day)
        );
        return true;
    }

    public void Close(string reason)
    {
        // A relayout that failed while open left no tree: the host's close still ends the view state.
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
        // The render is discarded: the root deactivates below.
        _state.Close();
        if (!treeLost)
        {
            returned = _grid.Leases;
            _grid.ReturnAll();
            _root!.SetActive(false);
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.closed"),
            ("reason", reason),
            ("returned", returned),
            ("rental_leases", AndroidCardRental.ActiveLeases),
            ("rental_balanced", AndroidCardRental.IsBalanced)
        );
    }

    /// <summary>
    /// A back step the panel answers before it closes: the inspected card, then an expanded rail
    /// picker. The Overlay Panel Host reads it as the panel's modal.
    /// </summary>
    public bool HasModalBackStep =>
        IsOpen
        && (_inspection.IsOpen || (_expandedPicker != RailPicker.None && _railModel != null));

    /// <summary>The back key's modal step: the inspected card animates closed, else the picker collapses.</summary>
    public void DismissModalBackStep()
    {
        if (!IsOpen || _inspection.HandleBack())
            return;
        if (_expandedPicker != RailPicker.None && _railModel is { } model)
            SelectRail(model, null, "collapse");
    }

    /// <summary>Main thread, every frame: loads the catalog, debounces the search, fills the grid.</summary>
    public void Tick()
    {
        if (!IsOpen)
            return;
        if (
            !string.Equals(AndroidTouchScreen.Key(), _screenKey, StringComparison.Ordinal)
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
        var pressed = AndroidScrollTracker.PointerPressed();
        foreach (var tracker in _trackers)
            tracker.Tick(pressed, _layout.Metrics.UnitsPerDp);
    }

    // The panel's uGUI tree for the current screen, from Core's CollectionTouchLayout. Every
    // geometry is in canvas units from the safe area and the density; a new screen builds a new
    // tree (Relayout) and renders the same view state into it.
    private void Build()
    {
        _screenKey = AndroidTouchScreen.Key();
        var layout = _layout = CollectionTouchLayout.For(AndroidTouchScreen.Read());
        _port.Touch = layout;
        _skin = new AndroidNativeSkin();
        var chrome = layout.Chrome;
        var metrics = layout.Metrics;
        var root = AndroidUiKit.Rect(
            "Collection",
            _parent,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        // Opaque: the panel covers the whole screen, and nothing of the scene shows through.
        var background = AndroidUiKit.PanelColor;
        background.a = 1f;
        AndroidUiKit.Fill(root, background);
        _root = root.gameObject;

        var gridArea = AndroidTouchScreen.Child("Grid", root, layout.Grid);
        _grid = new AndroidCollectionGrid(gridArea, _port, Inspect, _font, chrome.BodyFont);
        // The grid's RectMask2D does not clip the rented cards (their prefab carries its own
        // canvas), so the grid draws first and opaque bands above and below it hide the cards
        // scrolled past its edges; the header and the rail draw over those.
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
        _status = AndroidUiKit.Text(
            AndroidTouchScreen.Child("Status", root, layout.Grid),
            _font,
            "",
            chrome.ButtonFont,
            TextAlignmentOptions.Center,
            AndroidUiKit.MutedTextColor
        );

        (_, _, _supporterLabel) = _skin.Button(
            AndroidTouchScreen.Child("Supporter", root, layout.Title),
            _font,
            BPPSupporterAttributionText.FormatSponsorAction(L.CurrentLanguageCode),
            chrome.ButtonFont,
            chrome.LabelInset,
            () =>
                AndroidUiActions.Run(
                    "collection.sponsor",
                    () =>
                        Application.OpenURL(
                            BPPSupporterLinks.ResolveSponsorUrl(L.CurrentLanguageCode)
                        )
                )
        );
        _supporterLabel.enableWordWrapping = false;
        _supporterLabel.enableAutoSizing = true;
        _supporterLabel.fontSizeMin = chrome.BodyFont;
        _supporterLabel.fontSizeMax = chrome.ButtonFont;
        _supporterLabel.overflowMode = TextOverflowModes.Ellipsis;
        RefreshSupporterButton();
        _skin.Button(
            AndroidTouchScreen.Child("Close", root, layout.Close),
            _font,
            L.Resolve(new LocalizedTextSet("Back", "返回", "返回")),
            chrome.TitleFont,
            chrome.LabelInset,
            () => _requestClose("close_button")
        );
        _itemsTab = Tab(root, layout.ItemsTab, CollectionTabKind.Items);
        _skillsTab = Tab(root, layout.SkillsTab, CollectionTabKind.Skills);
        (_search, _searchPlaceholder) = Search(root, layout.Search, chrome);

        var rail = AndroidTouchScreen.Child("Rail", root, layout.Rail);
        _skin.Frame(
            AndroidUiKit.Rect(
                "RailFrame",
                rail,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero
            )
        );
        var inset = metrics.Dp(16f);
        var railInner = AndroidUiKit.Rect(
            "RailInner",
            rail,
            Vector2.zero,
            Vector2.one,
            new(inset, inset),
            new(-inset, -inset)
        );
        (_railScroll, _railContent) = AndroidUiKit.VerticalScroll(railInner);

        _inspection = new AndroidCollectionInspection(root, _font, layout, _skin);
        _trackers.Clear();
        _trackers.Add(new AndroidScrollTracker(Scrolled(), "grid", _grid.Scroll));
        _trackers.Add(new AndroidScrollTracker(Scrolled(), "rail", _railScroll));
        _trackers.Add(new AndroidScrollTracker(Scrolled(), "inspect", _inspection.DetailScroll));
        root.gameObject.SetActive(false);
        var dp = metrics.UnitsPerDp;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.laid_out"),
            ("screen", _screenKey),
            ("units_per_dp", Math.Round(dp, 3)),
            ("grid_dp", Math.Round(layout.Grid.Width / dp)),
            ("rail_dp", Math.Round(layout.Rail.Width / dp)),
            ("search_dp", Math.Round(layout.Search.Width / dp)),
            ("item_columns", layout.GridColumns(CardType.Item, layout.Grid.Width)),
            ("skill_columns", layout.GridColumns(CardType.Skill, layout.Grid.Width)),
            ("target_dp", Math.Round(metrics.MinTarget / dp, 1)),
            ("body_sp", Math.Round(chrome.BodyFont / dp, 1))
        );
    }

    private static BppLogEvent Scrolled() =>
        new(BppLogFeatureScope.Android, "android.collection.scrolled");

    // The screen changed under the panel: the old tree returns its cards, and a tree laid out for
    // the new screen renders the same view state. A build that throws leaves no tree, never the
    // destroyed one: the panel reads closed, its cards returned, and the next Open builds again.
    // That retry finds no tree, and its inspection and grid may belong to the destroyed partial
    // one, whose cards the failed attempt already returned, so it touches neither.
    private bool Relayout()
    {
        var before = _screenKey;
        var open = IsOpen;
        if (_root != null)
        {
            _inspection.Close("relayout");
            _grid.ReturnAll();
            UnityEngine.Object.Destroy(_root);
        }
        _root = null;
        try
        {
            Build();
        }
        catch (Exception exception)
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            _root = null;
            _grid.ReturnAll();
            _screenKey = string.Empty;
            // Open: the panel requests the host's close, which ends the view state (Close).
            _treeLost = open;
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.collection.relayout_failed"),
                exception,
                ("from", before),
                ("open", open),
                ("rental_leases", AndroidCardRental.ActiveLeases)
            );
            return false;
        }
        Relayouts++;
        var root = _root!;
        if (open)
        {
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            _search.SetTextWithoutNotify(_query);
            Render(_state.Rebuild());
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.relaid_out"),
            ("from", before),
            ("to", _screenKey),
            ("open", open),
            ("rental_leases", AndroidCardRental.ActiveLeases)
        );
        return true;
    }

    private void AdvanceCatalog()
    {
        _catalog.Advance();
        switch (_catalog.Status)
        {
            case AndroidCollectionCatalogStatus.Ready:
                _loading = false;
                Render(_state.AcceptCatalog(_catalog.Cards!));
                break;
            case AndroidCollectionCatalogStatus.Unavailable:
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
        // Late catalog arrival fills the credit once, without resetting filters or rotating names.
        if (sample)
        {
            _headerSupporters = _supporters?.Next(1) ?? Array.Empty<BPPSupporterSample>();
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
        AndroidGameFont.Cover(_supporterLabel);
    }

    private void Render(CollectionRenderOutcome outcome, string? command = null)
    {
        var model = outcome.Model;
        RefreshSupporterButton();
        RenderTab(_itemsTab, CollectionTabKind.Items, CollectionPanelText.ItemsTab(), model);
        RenderTab(_skillsTab, CollectionTabKind.Skills, CollectionPanelText.SkillsTab(), model);
        _searchPlaceholder.text = CollectionPanelText.SearchPlaceholder(model.ActiveType);
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
                new BppLogEvent(BppLogFeatureScope.Android, "android.collection.filtered"),
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

    // The rail is rebuilt from the model on every render: the chips a tab, hero or source offers
    // change with each command, and a rebuild costs one frame of a hundred buttons at most.
    private void RenderRail(CollectionPanelViewModel model)
    {
        _railModel = model;
        for (var index = _railContent.childCount - 1; index >= 0; index--)
        {
            var child = _railContent.GetChild(index).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
        var flow = new AndroidChipFlow(
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
            : AndroidCollectionLabels.Hero(selectedHero);
        var selectedSource = model.AvailableSources.FirstOrDefault(source =>
            string.Equals(source.SourceKey, model.SelectedSourceKey, StringComparison.Ordinal)
        );
        var sourceValue =
            selectedSource != null
                ? AndroidEncounterNames.Display(
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
                : () => AndroidPortraits.Hero(selectedHero),
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
                    : () => AndroidPortraits.Encounter(selectedSource.RepresentativeTemplateId),
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
                    AndroidCollectionLabels.Hero(hero),
                    selected,
                    () => SelectRail(model, selected ? null : _state.ToggleHero(chosen), "hero"),
                    hero == CardHero.Common ? null : () => AndroidPortraits.Hero(chosen),
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
                    AndroidEncounterNames.Display(templateId, source.DisplayName),
                    selected,
                    () => SelectRail(model, selected ? null : _state.ToggleSource(key), "source"),
                    () => AndroidPortraits.Encounter(templateId),
                    span: 6
                );
            }
            flow.Note(CollectionPanelText.SourceDisclaimer(), AndroidUiKit.MutedTextColor);
        }
        flow.Divider();
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
            flow.Divider();
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
        var resetBottom = 0f;
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
                    _advancedFilters
                        ? Mathf.Clamp(resetBottom + _layout.Gap - viewportHeight, 0f, max)
                        : 0f
                );
            },
            span: 2
        );
        flow.Break();
        resetBottom = flow.Height + _layout.Chrome.Target;
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
                        AndroidCollectionLabels.Keyword(option),
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
                        AndroidCollectionLabels.Tag(tag),
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
        AndroidChipFlow flow,
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
            AndroidTouchScreen.Child(kind.ToString(), root, area),
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
        label.enableWordWrapping = false;
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
        var rect = AndroidTouchScreen.Child("Search", root, area);
        _skin.Frame(rect);
        var inset = chrome.Gap;
        var textArea = AndroidUiKit.Rect(
            "TextArea",
            rect,
            Vector2.zero,
            Vector2.one,
            new(inset * 1.5f, inset * 0.5f),
            new(-inset * 1.5f, -inset * 0.5f)
        );
        textArea.gameObject.AddComponent<RectMask2D>();
        var placeholder = AndroidUiKit.Text(
            AndroidUiKit.Rect(
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
            AndroidUiKit.MutedTextColor
        );
        placeholder.enableWordWrapping = false;
        placeholder.overflowMode = TextOverflowModes.Ellipsis;
        var text = AndroidUiKit.Text(
            AndroidUiKit.Rect(
                "Text",
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
            AndroidUiKit.TextColor
        );
        text.enableWordWrapping = false;
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
        field.onValueChanged.AddListener(
            (UnityEngine.Events.UnityAction<string>)new Action<string>(OnSearchChanged)
        );
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
        AndroidUiKit.Fill(AndroidTouchScreen.Child("GridEdge", root, area), color);
    }

    private bool IsInRun()
    {
        try
        {
            return _gameState.ComputeIsInGameRun();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static CardHero? CurrentHero()
    {
        try
        {
            var player = Il2CppTheBazaar.Data.Run?.Player;
            if (
                player != null
                && AndroidCardFacets.Hero.TryToFacet(player.Hero, out var runHero)
                && CollectionPanelSelectionState.IsConcreteHero(runHero)
            )
                return runHero;
            return
                AndroidCardFacets.Hero.TryToFacet(
                    Il2CppTheBazaar.Data.SelectedHero,
                    out var selected
                ) && CollectionPanelSelectionState.IsConcreteHero(selected)
                ? selected
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
