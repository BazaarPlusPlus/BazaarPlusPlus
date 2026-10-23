using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Android.Ui;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.CollectionPanel.Ui;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Android.Collection;

/// <summary>
/// The Collection panel on Android (#396), entered from the touch menu. Core's
/// <see cref="CollectionViewState"/> owns every filter, the search, the sort and the render model,
/// as it does on desktop; this class draws that model in uGUI over the touch root and forwards each
/// tap back as a view-state command. The grid shows the game's own card prefabs through the card
/// rental. Differences from desktop are listed in <c>docs/architecture/collection.md</c>.
/// </summary>
internal sealed class AndroidCollectionPanel
{
    private const float RailWidth = 480f;
    private const float HeaderHeight = 96f;

    private readonly AndroidCollectionCatalog _catalog = new();
    private readonly AndroidCollectionGridPort _port = new();
    private readonly EmbeddedCollectionSourceCatalog _sources = new(
        AndroidCardFacets.IsHeroAvailable
    );
    private readonly AndroidGameStateProbe _gameState = new();
    private readonly AndroidCollectionHeroPreferenceStore _heroStore = new();
    private readonly CollectionViewState _state;
    private readonly TMP_FontAsset _font;
    private readonly GameObject _root;
    private readonly AndroidCollectionGrid _grid;
    private readonly AndroidCollectionInspection _inspection;
    private readonly RectTransform _railContent;
    private readonly ScrollRect _railScroll;
    private readonly (Image Background, TextMeshProUGUI Label) _itemsTab;
    private readonly (Image Background, TextMeshProUGUI Label) _skillsTab;
    private readonly TMP_InputField _search;
    private readonly TextMeshProUGUI _searchPlaceholder;
    private readonly TextMeshProUGUI _status;
    private bool _loading;

    public AndroidCollectionPanel(Transform parent, TMP_FontAsset font)
    {
        _font = font;
        _state = new CollectionViewState(
            _port,
            _sources,
            new AndroidCollectionDayTierSource(IsInRun),
            _heroStore
        );

        var root = AndroidUiKit.Rect(
            "Collection",
            parent,
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

        var title = AndroidUiKit.Rect(
            "Title",
            root,
            new(0f, 1f),
            new(1f, 1f),
            new(32f, -HeaderHeight + 8f),
            new(-140f, -8f)
        );
        AndroidUiKit.Text(
            title,
            font,
            CollectionPanelText.Title(),
            36f,
            TextAlignmentOptions.MidlineLeft,
            AndroidUiKit.TextColor
        );
        var close = AndroidUiKit.Fixed("Close", root, new(1f, 1f), new(-16f, -14f), new(100f, 68f));
        AndroidUiKit.Button(close, font, "X", 34f, () => Close("close_button"));

        var tabs = AndroidUiKit.Rect(
            "Tabs",
            root,
            new(0f, 1f),
            new(0f, 1f),
            new(16f, -HeaderHeight - 72f),
            new(16f + RailWidth, -HeaderHeight)
        );
        _itemsTab = Tab(tabs, 0f, 0.5f, CollectionTabKind.Items);
        _skillsTab = Tab(tabs, 0.5f, 1f, CollectionTabKind.Skills);
        (_search, _searchPlaceholder) = Search(root);

        var rail = AndroidUiKit.Rect(
            "Rail",
            root,
            new(0f, 0f),
            new(0f, 1f),
            new(16f, 16f),
            new(16f + RailWidth, -HeaderHeight - 172f)
        );
        (_railScroll, _railContent) = VerticalScroll(rail);

        var gridArea = AndroidUiKit.Rect(
            "Grid",
            root,
            Vector2.zero,
            Vector2.one,
            new(32f + RailWidth, 16f),
            new(-16f, -HeaderHeight)
        );
        _grid = new AndroidCollectionGrid(gridArea, _port, Inspect);
        // The grid's RectMask2D does not clip the rented cards (their prefab carries its own
        // canvas), so the grid draws first and opaque bands above and below it hide the cards
        // scrolled past its edges; the rail and the title draw over those.
        gridArea.SetSiblingIndex(0);
        Band(root, background, new(0f, 1f), new(0f, -HeaderHeight), Vector2.zero)
            .SetSiblingIndex(1);
        Band(root, background, new(0f, 0f), Vector2.zero, new(0f, 16f)).SetSiblingIndex(2);
        var status = AndroidUiKit.Rect(
            "Status",
            root,
            Vector2.zero,
            Vector2.one,
            new(32f + RailWidth, 16f),
            new(-16f, -HeaderHeight)
        );
        _status = AndroidUiKit.Text(
            status,
            font,
            "",
            30f,
            TextAlignmentOptions.Center,
            AndroidUiKit.MutedTextColor
        );

        _inspection = new AndroidCollectionInspection(root, font);
        _root.SetActive(false);
    }

    public bool IsOpen => _root.activeSelf;

    public void Open()
    {
        if (IsOpen)
            return;
        _catalog.RetryIfUnavailable();
        _state.ResetSearchForLifecycle();
        _search.SetTextWithoutNotify(string.Empty);
        var cached = _catalog.Cached;
        _state.PrepareCatalogForOpen(cached);
        var inRun = IsInRun();
        var remembered = inRun ? null : LoadHero();
        var hero = inRun ? CurrentHero() : null;
        var day = inRun ? AndroidCollectionDayTierSource.CurrentDay() : null;
        var selection = CollectionPanelOpenSelectionResolver.Resolve(
            inRun,
            hero,
            null,
            null,
            _sources.Entries,
            remembered
        );
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        Render(
            _state.ApplyOpenSelection(
                selection,
                Array.Empty<BPPSupporterSample>(),
                day,
                Array.Empty<string>()
            )
        );
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
            ("hero", selection.SelectedHero),
            ("day", day)
        );
    }

    public void Close(string reason)
    {
        if (!IsOpen)
            return;
        _inspection.Close(reason);
        if (_loading)
        {
            _loading = false;
            _state.NoteCatalogLoadCancelled();
        }
        _state.ResetSearchForLifecycle();
        var returned = _grid.Leases;
        _grid.ReturnAll();
        _root.SetActive(false);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.collection.closed"),
            ("reason", reason),
            ("returned", returned),
            ("rental_leases", AndroidCardRental.ActiveLeases),
            ("rental_balanced", AndroidCardRental.IsBalanced)
        );
    }

    /// <summary>Main thread, every frame: loads the catalog, debounces the search, fills the grid.</summary>
    public void Tick()
    {
        if (!IsOpen)
            return;
        if (_loading)
            AdvanceCatalog();
        var search = _state.TickSearch(Time.unscaledDeltaTime, false);
        if (search != null)
            Render(search, "search");
        _grid.Tick();
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

    private void Inspect(CollectionCardVm card) => _inspection.Open(card);

    private void Command(CollectionRenderOutcome? outcome, string command)
    {
        if (outcome != null)
            Render(outcome, command);
    }

    private void Render(CollectionRenderOutcome outcome, string? command = null)
    {
        var model = outcome.Model;
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
        if (command != null)
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.collection.filtered"),
                ("command", command),
                ("tab", model.ActiveTab),
                ("visible", model.VisibleCount)
            );
    }

    private static void RenderTab(
        (Image Background, TextMeshProUGUI Label) tab,
        CollectionTabKind kind,
        string label,
        CollectionPanelViewModel model
    )
    {
        var active = model.ActiveTab == kind;
        tab.Label.text = active ? $"{label}({model.VisibleCount})" : label;
        tab.Background.color = active ? AndroidUiKit.ActiveColor : AndroidUiKit.ButtonColor;
    }

    // The rail is rebuilt from the model on every render: the chips a tab, hero or source offers
    // change with each command, and a rebuild costs one frame of a hundred buttons at most.
    private void RenderRail(CollectionPanelViewModel model)
    {
        for (var index = _railContent.childCount - 1; index >= 0; index--)
            UnityEngine.Object.Destroy(_railContent.GetChild(index).gameObject);
        var flow = new AndroidChipFlow(
            _railContent,
            _font,
            columns: 6,
            chipHeight: 60f,
            fontSize: 24f
        );
        var profile = model.TabProfile;

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
        if (model.DayFilterValue is { } day)
            flow.Chip(
                $"{CollectionPanelText.DayHeader()} {day}",
                model.DayFilterActive,
                () => Command(_state.ToggleRunDayFilter(), "day"),
                span: 6
            );

        flow.Header(CollectionPanelText.HeroHeader());
        flow.Chip(
            CollectionPanelText.FacetMatchMode(CollectionFacetMatchMode.All),
            model.AllHeroesSelected,
            () => Command(_state.ToggleAllHeroes(), "all_heroes"),
            span: 2
        );
        foreach (var hero in model.AvailableHeroes)
        {
            var chosen = hero;
            flow.Chip(
                AndroidCollectionLabels.Hero(hero),
                !model.AllHeroesSelected && model.SelectedHero == hero,
                () => Command(_state.ToggleHero(chosen), "hero"),
                span: 2
            );
        }

        if (profile.ShowTierFilter || profile.ShowSizeFilter)
            flow.Header(
                profile.ShowSizeFilter
                    ? CollectionPanelText.TierSizeHeader()
                    : CollectionPanelText.TierHeader()
            );
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

        if (profile.ShowKeywordFilter && model.AvailableKeywordOptions.Count > 0)
        {
            flow.Header(CollectionPanelText.KeywordHeader());
            MatchModes(flow, model.KeywordMatchMode, mode => _state.SetKeywordMatchMode(mode));
            foreach (var option in model.AvailableKeywordOptions)
            {
                var chosen = option;
                var selected = option.Keyword is { } keyword
                    ? model.SelectedKeywords.Contains(keyword)
                    : option.Mechanic is { } mechanic && model.SelectedMechanics.Contains(mechanic);
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

        if (profile.ShowSourceFilter && model.AvailableSources.Count > 0)
        {
            flow.Header(CollectionPanelText.SourceHeader(model.ActiveType));
            foreach (var source in model.AvailableSources)
            {
                var key = source.SourceKey;
                flow.Chip(
                    source.DisplayName,
                    string.Equals(model.SelectedSourceKey, key, StringComparison.Ordinal),
                    () =>
                    {
                        if (model.SourceSelectorEnabled)
                            Command(_state.ToggleSource(key), "source");
                    },
                    span: 3
                );
            }
            flow.Note(CollectionPanelText.SourceDisclaimer(), 64f);
        }

        flow.Break();
        flow.Chip(
            CollectionPanelText.ResetFilters(),
            false,
            () => Command(_state.ResetFilters(), "reset"),
            span: 6
        );
        _railContent.sizeDelta = new Vector2(0f, flow.Height + 16f);
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
        RectTransform tabs,
        float from,
        float to,
        CollectionTabKind kind
    )
    {
        var rect = AndroidUiKit.Rect(
            kind.ToString(),
            tabs,
            new(from, 0f),
            new(to, 1f),
            new(from == 0f ? 0f : 4f, 0f),
            new(to == 1f ? 0f : -4f, 0f)
        );
        var (_, background, label) = AndroidUiKit.Button(
            rect,
            _font,
            "",
            28f,
            () => Command(_state.SetActiveTab(kind), "tab")
        );
        return (background, label);
    }

    private (TMP_InputField Field, TextMeshProUGUI Placeholder) Search(RectTransform root)
    {
        var rect = AndroidUiKit.Rect(
            "Search",
            root,
            new(0f, 1f),
            new(0f, 1f),
            new(16f, -HeaderHeight - 160f),
            new(16f + RailWidth, -HeaderHeight - 84f)
        );
        AndroidUiKit.Fill(rect, AndroidUiKit.ButtonColor);
        var area = AndroidUiKit.Rect(
            "TextArea",
            rect,
            Vector2.zero,
            Vector2.one,
            new(18f, 6f),
            new(-18f, -6f)
        );
        area.gameObject.AddComponent<RectMask2D>();
        var placeholder = AndroidUiKit.Text(
            AndroidUiKit.Rect(
                "Placeholder",
                area,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero
            ),
            _font,
            "",
            26f,
            TextAlignmentOptions.MidlineLeft,
            AndroidUiKit.MutedTextColor
        );
        placeholder.enableWordWrapping = false;
        var text = AndroidUiKit.Text(
            AndroidUiKit.Rect("Text", area, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero),
            _font,
            "",
            26f,
            TextAlignmentOptions.MidlineLeft,
            AndroidUiKit.TextColor
        );
        text.enableWordWrapping = false;
        // Configured while inactive: the field's OnEnable reads its text component.
        rect.gameObject.SetActive(false);
        var field = rect.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.fontAsset = _font;
        field.pointSize = 26f;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 64;
        field.onValueChanged.AddListener(
            (UnityEngine.Events.UnityAction<string>)new Action<string>(OnSearchChanged)
        );
        rect.gameObject.SetActive(true);
        return (field, placeholder);
    }

    private void OnSearchChanged(string query) => _state.SetSearchQuery(query);

    private static RectTransform Band(
        RectTransform root,
        Color color,
        Vector2 edge,
        Vector2 offsetMin,
        Vector2 offsetMax
    )
    {
        var band = AndroidUiKit.Rect(
            "GridEdge",
            root,
            new(0f, edge.y),
            new(1f, edge.y),
            offsetMin,
            offsetMax
        );
        AndroidUiKit.Fill(band, color);
        return band;
    }

    private static (ScrollRect Scroll, RectTransform Content) VerticalScroll(RectTransform area)
    {
        var viewport = AndroidUiKit.Rect(
            "Viewport",
            area,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero
        );
        AndroidUiKit.Fill(viewport, new Color(0f, 0f, 0f, 0.001f));
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = AndroidUiKit.Rect(
            "Content",
            viewport,
            new(0f, 1f),
            new(1f, 1f),
            Vector2.zero,
            Vector2.zero
        );
        content.pivot = new Vector2(0f, 1f);
        var scroll = area.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
        return (scroll, content);
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

    private CollectionPanelHeroPreferenceLoadResult? LoadHero()
    {
        try
        {
            return _heroStore.Load(_state.CatalogReadiness, _state.AvailableHeroes);
        }
        catch (Exception)
        {
            return null;
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
                && CollectionPanelOpenSelectionResolver.IsConcreteHero(runHero)
            )
                return runHero;
            return
                AndroidCardFacets.Hero.TryToFacet(
                    Il2CppTheBazaar.Data.SelectedHero,
                    out var selected
                ) && CollectionPanelOpenSelectionResolver.IsConcreteHero(selected)
                ? selected
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
