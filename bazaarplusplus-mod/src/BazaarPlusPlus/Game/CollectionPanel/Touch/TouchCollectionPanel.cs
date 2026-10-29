#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Core.Ui;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Game.Touch;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.DayTiers;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CollectionPanel.Touch;

/// <summary>
/// The Collection panel on a mobile platform (ADR-0024): the Android host's touch Collection on the
/// Mono host. Core's <see cref="CollectionViewState"/> owns every filter, the search, the sort and
/// the render model, as on desktop and Android; this class draws that model in uGUI and forwards
/// each tap back as a view-state command. The geometry is Core's <see cref="CollectionTouchLayout"/>
/// inside the safe area: the shared header (title, tabs, search, close), the filter rail and a grid
/// whose column count follows its width, laid out again when the screen changes. The grid shows the
/// game's own card prefabs through <see cref="TouchCardRental"/>, and the catalog is the desktop's
/// <see cref="CollectionCatalog"/>. It is a Main Overlay Panel: <see cref="TouchCollectionHost"/>
/// registers it with the Overlay Panel Host, and its own close button and a failed relayout request
/// the close.
/// </summary>
internal sealed class TouchCollectionPanel
{
    private const float CatalogBuildFrameBudgetMs = 4f;

    private readonly CollectionCatalog _catalog;
    private readonly TouchCollectionGridPort _port = new();
    private readonly IBppServices _services;
    private readonly CollectionPanelHeroPreferenceStore _heroStore = new();
    private readonly CollectionViewState _state;
    private readonly Transform _parent;
    private readonly TMP_FontAsset _font;
    private IReadOnlyList<BPPSupporterSample> _headerSupporters = Array.Empty<BPPSupporterSample>();
    private TextMeshProUGUI _supporterLabel = null!;
    private readonly Action<string> _requestClose;
    private readonly List<TouchScrollTracker> _trackers = new();
    private CollectionTouchLayout _layout = null!;

    // The tree for the current screen; none after a build failed, and Open builds again.
    private readonly TouchViewSlot<GameObject> _tree;

    // An open panel lays out again only once the screen key holds still and no card is setting
    // up, as Ten-Win Builds does; Open still rebuilds at once for a changed screen.
    private readonly TouchRelayoutDebounce _relayout = new(string.Empty);
    private TouchNativeSkin _skin = null!;
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
        CollectionCatalog catalog,
        IBppServices services,
        GameDataDayTierResolver dayTiers,
        Action<string> requestClose
    )
    {
        _parent = parent;
        _font = font;
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _requestClose = requestClose ?? throw new ArgumentNullException(nameof(requestClose));
        _state = new CollectionViewState(
            _port,
            StaticCollectionSourceCatalog.Instance,
            new CollectionDayTierSource(dayTiers),
            _heroStore
        );
        _tree = new TouchViewSlot<GameObject>(DropTree);
        // A failed first build leaves no tree: the first Open builds again.
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

    /// <summary>Times the open panel was laid out again for a new screen.</summary>
    public long Relayouts { get; private set; }

    /// <summary>False when no tree could be built for the screen: the panel stays closed.</summary>
    public bool Open()
    {
        if (IsOpen)
            return true;
        if (
            // No tree first: after a failed build the skin may be the partial one's, or none.
            (_tree.View == null || _skin.NeedsRefresh || !_tree.IsCurrent(TouchScreen.Key()))
            && !Relayout()
        )
            return false;
        CollapseFilters();
        _headerSupporters = Array.Empty<BPPSupporterSample>();
        RefreshSupporterButton(sample: true);
        var root = _tree.View!;
        _query = string.Empty;
        _search.SetTextWithoutNotify(string.Empty);
        var cached = _catalog.TryGetCached(out var built) ? built.Cards : null;
        var inRun = IsInRun();
        var day = inRun ? CurrentDay() : null;
        // A failed hero-preference load opens on the default hero; this host does not report it.
        var opened = _state.Open(
            inRun
                ? CollectionOpenFacts.InRun(
                    CurrentHero(),
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
            // The open's render has not run the query yet and the accept's supersedes it, so
            // draw the rail once, keeping the open's scroll resets.
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
        _heroStore.Flush();
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
            _tree.View!.SetActive(false);
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.collection.closed"),
            ("reason", reason),
            ("returned", returned),
            ("rental_leases", TouchCardRental.ActiveLeases),
            ("rental_balanced", TouchCardRental.IsBalanced)
        );
    }

    /// <summary>Main thread, on app pause: writes a hero choice staged by the open panel.</summary>
    public void FlushHeroPreference() => _heroStore.Flush();

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
        var pressed = TouchScrollTracker.PointerPressed();
        foreach (var tracker in _trackers)
            tracker.Tick(pressed, _layout.Metrics.UnitsPerDp);
    }

    // The panel's uGUI tree for the current screen, from Core's CollectionTouchLayout. Every
    // geometry is in canvas units from the safe area and the density; a new screen builds a new
    // tree (Relayout) and renders the same view state into it. A build that throws destroys its
    // partial root, opaque and active until its last lines, before the exception leaves.
    private GameObject Build(string key)
    {
        var layout = _layout = CollectionTouchLayout.For(TouchScreen.Read());
        _port.Touch = layout;
        _skin = new TouchNativeSkin();
        var chrome = layout.Chrome;
        var metrics = layout.Metrics;
        var root = TouchUiKit.Rect(
            "Collection",
            _parent,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        // Opaque: the panel covers the whole screen, and nothing of the scene shows through.
        var background = TouchUiKit.PanelColor;
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

    // Everything under the root, which Build destroys when this throws.
    private void BuildContent(RectTransform root, Color background, CollectionTouchLayout layout)
    {
        var chrome = layout.Chrome;
        var metrics = layout.Metrics;
        TouchUiKit.Fill(root, background);

        var gridArea = TouchScreen.Child("Grid", root, layout.Grid);
        _grid = new TouchCollectionGrid(gridArea, _port, Inspect, _font, chrome.BodyFont);
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
        _status = TouchUiKit.Text(
            TouchScreen.Child("Status", root, layout.Grid),
            _font,
            "",
            chrome.ButtonFont,
            TextAlignmentOptions.Center,
            TouchUiKit.MutedTextColor
        );

        (_, _, _supporterLabel) = _skin.Button(
            TouchScreen.Child("Supporter", root, layout.Title),
            _font,
            BPPSupporterAttributionText.FormatSponsorAction(L.CurrentLanguageCode),
            chrome.ButtonFont,
            chrome.LabelInset,
            () =>
                TouchUiActions.Run(
                    "collection.sponsor",
                    () =>
                        Application.OpenURL(
                            BPPSupporterLinks.ResolveSponsorUrl(L.CurrentLanguageCode)
                        )
                )
        );
        _supporterLabel.textWrappingMode = TextWrappingModes.NoWrap;
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
            TouchUiKit.Rect(
                "RailFrame",
                rail,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero
            )
        );
        var inset = metrics.Dp(16f);
        var railInner = TouchUiKit.Rect(
            "RailInner",
            rail,
            Vector2.zero,
            Vector2.one,
            new(inset, inset),
            new(-inset, -inset)
        );
        (_railScroll, _railContent) = TouchUiKit.VerticalScroll(railInner);

        _inspection = new TouchCollectionInspection(root, _font, layout, _skin);
        _trackers.Clear();
        _trackers.Add(new TouchScrollTracker(Scrolled(), "grid", _grid.Scroll));
        _trackers.Add(new TouchScrollTracker(Scrolled(), "rail", _railScroll));
        _trackers.Add(new TouchScrollTracker(Scrolled(), "inspect", _inspection.DetailScroll));
        root.gameObject.SetActive(false);
    }

    private static BppLogEvent Scrolled() =>
        new(BppLogFeatureScope.Touch, "touch.collection.scrolled");

    // The screen changed under the panel: the old tree returns its cards, and a tree laid out for
    // the new screen renders the same view state. A build that throws leaves no tree, never the
    // destroyed one: the panel reads closed and the next Open builds again (LayoutFailed).
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
            ("rental_leases", TouchCardRental.ActiveLeases),
            ("held_frames", _relayout.HeldFrames)
        );
        return true;
    }

    // The old tree goes with its inspected card and its rented cards.
    private void DropTree(GameObject root)
    {
        _inspection.Close("relayout");
        _grid.ReturnAll();
        UnityEngine.Object.Destroy(root);
    }

    // The build threw and destroyed its partial root. The retry finds no tree, and its inspection
    // and grid may belong to that partial one, whose cards go back here, so it touches neither.
    // An open panel requests the host's close, which ends the view state (Close).
    private void LayoutFailed(Exception exception, string before, bool open)
    {
        _grid?.ReturnAll();
        _treeLost = open;
        BppLog.ErrorEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.collection.relayout_failed"),
            exception,
            ("from", before),
            ("open", open),
            ("rental_leases", TouchCardRental.ActiveLeases)
        );
    }

    private void AdvanceCatalog()
    {
        _catalog.AdvanceBuild(CatalogBuildFrameBudgetMs, out _);
        if (_catalog.TryGetCached(out var built))
        {
            _loading = false;
            Render(_state.AcceptCatalog(built.Cards));
        }
        else if (_catalog.WarmupStatus == CollectionCatalogWarmupStatus.Unavailable)
        {
            _loading = false;
            Render(_state.CatalogUnavailable());
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
            _headerSupporters = BPPSupporters.SampleMany(1);
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
        TouchFonts.Cover(_supporterLabel);
    }

    private void Render(CollectionRenderOutcome outcome, string? command = null)
    {
        var model = outcome.Model;
        RefreshSupporterButton();
        RenderTab(_itemsTab, CollectionTabKind.Items, CollectionPanelText.ItemsTab(), model);
        RenderTab(_skillsTab, CollectionTabKind.Skills, CollectionPanelText.SkillsTab(), model);
        _searchPlaceholder.text = CollectionPanelText.SearchPlaceholder(model.ActiveType);
        // The facets come from the whole catalog, not the search, and the selections are the
        // view state's live sets: a search changes only the run day the query re-reads and,
        // while loading, the source selector. Otherwise the drawn chips stay, bound to an
        // equivalent model.
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

    // The rail is rebuilt from the model on every render but a search's (Render): the chips a tab,
    // hero or source offers change with each command, and a rebuild costs one frame of a hundred
    // buttons at most.
    private void RenderRail(CollectionPanelViewModel model)
    {
        _railModel = model;
        for (var index = _railContent.childCount - 1; index >= 0; index--)
        {
            var child = _railContent.GetChild(index).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
        var flow = new TouchChipFlow(
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
                ? TouchEncounterNames.Display(
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
                : () => TouchPortraits.Hero(selectedHero),
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
                    : () => TouchPortraits.Encounter(selectedSource.RepresentativeTemplateId),
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
                    hero == CardHero.Common ? null : () => TouchPortraits.Hero(chosen),
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
                    TouchEncounterNames.Display(templateId, source.DisplayName),
                    selected,
                    () => SelectRail(model, selected ? null : _state.ToggleSource(key), "source"),
                    () => TouchPortraits.Encounter(templateId),
                    span: 6
                );
            }
            flow.Note(CollectionPanelText.SourceDisclaimer(), TouchUiKit.MutedTextColor);
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
        TouchChipFlow flow,
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
        label.textWrappingMode = TextWrappingModes.NoWrap;
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
        var textArea = TouchUiKit.Rect(
            "TextArea",
            rect,
            Vector2.zero,
            Vector2.one,
            new(inset * 1.5f, inset * 0.5f),
            new(-inset * 1.5f, -inset * 0.5f)
        );
        textArea.gameObject.AddComponent<RectMask2D>();
        var placeholder = TouchUiKit.Text(
            TouchUiKit.Rect(
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
            TouchUiKit.MutedTextColor
        );
        placeholder.textWrappingMode = TextWrappingModes.NoWrap;
        placeholder.overflowMode = TextOverflowModes.Ellipsis;
        var text = TouchUiKit.Text(
            TouchUiKit.Rect(
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
            TouchUiKit.TextColor
        );
        text.textWrappingMode = TextWrappingModes.NoWrap;
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
            new UnityEngine.Events.UnityAction<string>(OnSearchChanged)
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
        TouchUiKit.Fill(TouchScreen.Child("GridEdge", root, area), color);
    }

    private bool IsInRun()
    {
        try
        {
            return _services.RunContext.IsInGameRun
                || _services.GameStateProbe.ComputeIsInGameRun();
        }
        catch (Exception)
        {
            return _services.RunContext.IsInGameRun;
        }
    }

    private static int? CurrentDay()
    {
        try
        {
            return (int?)TheBazaar.Data.Run?.Day;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private EncounterIdsSnapshot EncounterIds()
    {
        try
        {
            return _services.EncounterState.GetEncounterIdsOutcome().Snapshot;
        }
        catch (Exception)
        {
            return EncounterIdsSnapshot.Empty;
        }
    }

    private static CardHero? CurrentHero()
    {
        try
        {
            var player = TheBazaar.Data.Run?.Player;
            if (
                player != null
                && DesktopCardFacets.Hero.TryToFacet(player.Hero, out var runHero)
                && CollectionPanelSelectionState.IsConcreteHero(runHero)
            )
                return runHero;
            return
                DesktopCardFacets.Hero.TryToFacet(TheBazaar.Data.SelectedHero, out var selected)
                && CollectionPanelSelectionState.IsConcreteHero(selected)
                ? selected
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
