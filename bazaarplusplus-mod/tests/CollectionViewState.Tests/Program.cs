#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Localization;

// Ticket requirement: entry installs L before production text resolves. Uninstalled L throws
// InvalidOperationException — there is no English fallback at the Resolve path.
{
    L.Reset();
    var threw = false;
    try
    {
        _ = CollectionPanelText.Title();
    }
    catch (InvalidOperationException)
    {
        threw = true;
    }
    AssertTrue(threw, "L.Resolve must throw when L is not installed (no English fallback).");
}

L.Install(() => "en", () => BppChineseLocaleMode.Mainland);

var vanessaTemplateId = Guid.NewGuid();
var vanessaSource = Source(
    "merchant:vanessa-only",
    CollectionSourceKind.Merchant,
    availableHeroes: new[] { CardHero.Vanessa },
    sourceTemplateIds: new[] { vanessaTemplateId }
);
var commonSource = Source(
    "merchant:common-all",
    CollectionSourceKind.Merchant,
    availableHeroes: Array.Empty<CardHero>()
);
var catalog = new DictionarySourceCatalog(vanessaSource, commonSource);

// The default merchant seen only by Vanessa, so an open without a hero keeps it until a prune.
var vanessaOnlyDefaultMerchantCatalog = new DictionarySourceCatalog(
    Source(
        CollectionPanelSelectionState.DefaultMerchantSourceKey,
        CollectionSourceKind.Merchant,
        availableHeroes: new[] { CardHero.Vanessa }
    )
);
var grid = new FakeGridPort();
var dayTiers = new FakeDayTier { Day = 5 };
var prefs = new RecordingHeroPreferenceStore();

CollectionViewState CreateState() => new(grid, catalog, dayTiers, prefs);

// --- ToggleHero full chain: prune before query, preference Save, control-bar scroll intent ---
{
    grid.Reset();
    prefs.Reset();
    var state = CreateState();
    var cards = new[]
    {
        Card("Vanessa Item", CardTier.Bronze, heroes: new[] { CardHero.Vanessa }),
        Card("Dooley Item", CardTier.Bronze, heroes: new[] { CardHero.Dooley }),
    };
    state.AcceptCatalog(cards);
    // Select Vanessa first so the Vanessa-only source is visible, then pin that source.
    // (Selecting the source under EffectiveHero=Common is cleared by query normalization.)
    state.ToggleHero(CardHero.Vanessa);
    prefs.Reset();
    state.ToggleSource(vanessaSource.SourceKey);
    AssertEqual(
        vanessaSource.SourceKey,
        state.Rebuild().Model.SelectedSourceKey,
        "Precondition: Vanessa-only source should be selected."
    );

    var publishesBefore = grid.PublishCount;
    var outcome = state.ToggleHero(CardHero.Dooley);
    AssertTrue(outcome != null, "ToggleHero must produce a render outcome.");
    AssertTrue(outcome!.ResetScroll, "ToggleHero must request list scroll reset.");
    AssertTrue(outcome.ResetControlsScroll, "ToggleHero must request controls scroll reset.");
    AssertTrue(prefs.SaveCount == 1, "ToggleHero must Save the effective hero preference.");
    AssertEqual(
        CardHero.Dooley,
        prefs.LastSaved,
        "ToggleHero Save must receive the effective hero."
    );
    AssertTrue(
        grid.PublishCount > publishesBefore,
        "ToggleHero must run the query (Publish) after filter mutation."
    );
    // Prune runs before query: Dooley cannot see Vanessa-only source, so selection clears
    // and Publish receives the unscoped catalog (not the empty offer pool of a pruned source).
    AssertEqual(
        null,
        outcome.Model.SelectedSourceKey,
        "Prune must clear a source invisible for the new hero before the query runs."
    );
    // With source pruned, the query uses hero=Dooley (not the Vanessa-only offer pool).
    AssertEqual(
        1,
        grid.LastPublishedCards.Count,
        "After prune, query must apply the new hero filter without the pruned source."
    );
    AssertEqual(
        "Dooley Item",
        grid.LastPublishedCards[0].DisplayName,
        "Prune-before-query must leave only the new-hero matches."
    );
}

// --- Normalization write-back ---
{
    grid.Reset();
    prefs.Reset();
    var state = CreateState();
    var cards = new[]
    {
        Card(
            "Tagged",
            CardTier.Bronze,
            tags: new[] { CardTag.Weapon },
            heroes: new[] { CardHero.Vanessa }
        ),
        Card("Untagged", CardTier.Bronze, heroes: new[] { CardHero.Vanessa }),
    };
    state.AcceptCatalog(cards);
    state.ToggleTag(CardTag.Weapon);
    state.ToggleTag(CardTag.Tool); // not present in catalog facets after acceptance
    // Tool is not in facet availability → normalization should drop it on query.
    var outcome = state.ToggleTier(CardTier.Bronze);
    AssertTrue(outcome != null, "ToggleTier must render.");
    AssertTrue(
        outcome!.Model.SelectedTags.Contains(CardTag.Weapon),
        "Normalization must retain available tags."
    );
    AssertFalse(
        outcome.Model.SelectedTags.Contains(CardTag.Tool),
        "Normalization must drop tags absent from facet availability."
    );
}

// --- Available-sources cache hit / invalidation ---
{
    grid.Reset();
    prefs.Reset();
    var state = CreateState();
    state.AcceptCatalog(
        new[] { Card("Item", CardTier.Bronze, heroes: new[] { CardHero.Vanessa, CardHero.Dooley }) }
    );
    var first = state.Rebuild().Model.AvailableSources;
    var second = state.Rebuild().Model.AvailableSources;
    AssertTrue(
        ReferenceEquals(first, second),
        "AvailableSources must reuse the cached projection for the same (kind, hero)."
    );

    state.ToggleHero(CardHero.Vanessa);
    var afterHero = state.Rebuild().Model.AvailableSources;
    AssertFalse(
        ReferenceEquals(first, afterHero),
        "Changing effective hero must invalidate the available-sources cache."
    );
}

// --- Pending search cancelled by a confirmed state change ---
{
    grid.Reset();
    prefs.Reset();
    var state = CreateState();
    state.AcceptCatalog(
        new[] { Card("Sword", CardTier.Bronze, heroes: new[] { CardHero.Vanessa }) }
    );
    state.SetSearchQuery("sw");
    state.ToggleTier(CardTier.Silver);
    AssertTrue(
        state.TickSearch(1f, isComposing: false) == null,
        "A confirmed filter change must cancel the pending search debounce."
    );
}

// --- Same-value no-op keeps pending search and does not reset scroll ---
{
    grid.Reset();
    prefs.Reset();
    var state = CreateState();
    state.AcceptCatalog(
        new[] { Card("Sword", CardTier.Bronze, heroes: new[] { CardHero.Vanessa }) }
    );
    state.SetSearchQuery("pending");
    var publishesBefore = grid.PublishCount;

    var noopTab = state.SetActiveTab(CollectionTabKind.Items);
    AssertTrue(noopTab == null, "SetActiveTab same tab must return null (no-op).");
    AssertEqual(publishesBefore, grid.PublishCount, "Same-value no-op must not Publish/query.");

    var noopSort = state.SetSortPriority(CollectionSortPriority.Quality);
    AssertTrue(noopSort == null, "SetSortPriority same value must return null (no-op).");

    state.SetSearchQuery("pending");
    AssertTrue(
        state.TickSearch(1f, isComposing: false) != null,
        "Same-value no-ops and an unchanged search string must preserve the pending search."
    );
}

// --- AcceptCatalog triad after an open: no-Save normalize + prune + zero preference writes ---
{
    var acceptCards = new[]
    {
        Card("Only Vanessa", CardTier.Bronze, heroes: new[] { CardHero.Vanessa }),
    };

    // In a run at the Vanessa-only merchant: the open stamps the run day and supporters, and
    // the source stays selected through the accept's prune.
    grid.Reset();
    prefs.Reset();
    var state = CreateState();
    var opened = state.Open(
        CollectionOpenFacts.InRun(
            CardHero.Vanessa,
            7,
            Encounter(current: vanessaTemplateId),
            cachedCatalog: null,
            new[]
            {
                new BPPSupporterSample { Name = "pat", Tier = 1 },
            }
        )
    );
    AssertEqual(7, opened.Render.Model.DayFilterValue, "Open must stamp the run day.");
    AssertEqual(
        7,
        state.Rebuild().Model.DayFilterValue,
        "The stamped run day holds until the first catalog query."
    );
    AssertEqual(
        "pat",
        opened.Render.Model.Supporters.Single().Name,
        "Open must carry the supporters into the model."
    );

    var outcome = state.AcceptCatalog(acceptCards);
    AssertEqual(
        (int?)5,
        outcome.Model.DayFilterValue,
        "The first catalog query re-reads the run day from the day-tier port."
    );
    AssertEqual(
        0,
        prefs.SaveCount,
        "AcceptCatalog must not write hero preference (normalize path)."
    );
    AssertTrue(outcome.Model.AvailableTags != null, "AcceptCatalog must recompute facets.");
    AssertTrue(
        outcome.Model.AvailableHeroes.Contains(CardHero.Vanessa),
        "AcceptCatalog must expose available heroes from the accepted catalog."
    );
    AssertEqual(
        vanessaSource.SourceKey,
        outcome.Model.SelectedSourceKey,
        "Visible source must survive AcceptCatalog prune."
    );

    // Out of a run with a remembered Common: no hero, the default merchant, which only Vanessa
    // sees. Loading keeps it at open; the accept prunes it for the effective Common hero.
    grid.Reset();
    prefs.Reset();
    prefs.Stored = "Common";
    state = new CollectionViewState(grid, vanessaOnlyDefaultMerchantCatalog, dayTiers, prefs);
    opened = state.Open(
        CollectionOpenFacts.OutOfRun(cachedCatalog: null, Array.Empty<BPPSupporterSample>())
    );
    AssertEqual(null, opened.Selection.SelectedHero, "A remembered Common opens with no hero.");
    AssertEqual(
        CollectionPanelSelectionState.DefaultMerchantSourceKey,
        opened.Render.Model.SelectedSourceKey,
        "Open must not prune while the catalog is loading."
    );
    outcome = state.AcceptCatalog(acceptCards);
    AssertEqual(0, prefs.SaveCount, "AcceptCatalog prune path must still write zero preferences.");
    AssertEqual(
        null,
        outcome.Model.SelectedSourceKey,
        "AcceptCatalog must prune sources invisible for the effective hero."
    );
}

// --- Open out of a run: the remembered hero, loaded against the readiness Open just set ---
{
    var cards = new[] { Card("Mak Item", CardTier.Bronze, heroes: new[] { CardHero.Mak }) };

    grid.Reset();
    prefs.Reset();
    prefs.Stored = "Mak";
    var state = CreateState();
    var publishesBefore = grid.PublishCount;
    var opened = state.Open(CollectionOpenFacts.OutOfRun(cards, Array.Empty<BPPSupporterSample>()));
    AssertEqual(1, prefs.LoadCount, "An out-of-run open must load the remembered hero once.");
    AssertEqual(
        (CollectionCatalogReadiness?)CollectionCatalogReadiness.Accepted,
        prefs.LastLoadReadiness,
        "A cached catalog makes the preference load see Accepted readiness."
    );
    AssertEqual((CardHero?)CardHero.Mak, opened.Selection.SelectedHero, "Remembered Mak.");
    AssertEqual(0, prefs.SaveCount, "Open must never save the hero preference.");
    AssertEqual(publishesBefore, grid.PublishCount, "Open must not publish.");
    AssertTrue(opened.HeroPreferenceFailure == null, "A loading store reports no failure.");
    AssertTrue(opened.Render.ResetScroll, "Open must reset the list scroll.");
    AssertTrue(opened.Render.ResetControlsScroll, "Open must reset the controls scroll.");
}

{
    // Readiness is set before the load: a cached catalog without Dragons cards makes a stored
    // TheDragons known but unavailable.
    var noDragons = new[]
    {
        Card("Vanessa Item", CardTier.Bronze, heroes: new[] { CardHero.Vanessa }),
    };
    grid.Reset();
    prefs.Reset();
    prefs.Stored = "TheDragons";
    var state = CreateState();
    var opened = state.Open(
        CollectionOpenFacts.OutOfRun(noDragons, Array.Empty<BPPSupporterSample>())
    );
    AssertEqual(
        (CollectionCatalogReadiness?)CollectionCatalogReadiness.Accepted,
        prefs.LastLoadReadiness,
        "The cached catalog must be accepted before the preference load."
    );
    AssertFalse(
        prefs.LastLoadHeroes!.Contains(CardHero.TheDragons),
        "The preference load must see the accepted roster, which lacks The Dragons."
    );
    AssertEqual(
        null,
        opened.Selection.SelectedHero,
        "A known-but-unavailable remembered hero opens with no hero."
    );

    // Uncached: the load sees Loading, so TheDragons is kept until an accepted roster
    // normalizes it, without a Save.
    grid.Reset();
    prefs.Reset();
    prefs.Stored = "TheDragons";
    state = CreateState();
    opened = state.Open(
        CollectionOpenFacts.OutOfRun(cachedCatalog: null, Array.Empty<BPPSupporterSample>())
    );
    AssertEqual(
        (CollectionCatalogReadiness?)CollectionCatalogReadiness.Loading,
        prefs.LastLoadReadiness,
        "No cached catalog makes the preference load see Loading readiness."
    );
    AssertEqual(
        (CardHero?)CardHero.TheDragons,
        opened.Render.Model.SelectedHero,
        "Open while loading keeps a known concrete remembered hero."
    );
    var accepted = state.AcceptCatalog(noDragons);
    AssertEqual(
        null,
        accepted.Model.SelectedHero,
        "The accepted roster normalizes an unavailable hero away."
    );
    AssertEqual(0, prefs.SaveCount, "Normalizing on accept must not save the preference.");
}

{
    grid.Reset();
    prefs.Reset();
    prefs.Stored = "NotARealHero";
    var opened = CreateState()
        .Open(CollectionOpenFacts.OutOfRun(cachedCatalog: null, Array.Empty<BPPSupporterSample>()));
    AssertEqual(
        CollectionPanelSelectionState.Default,
        opened.Selection,
        "An invalid remembered hero opens on the default selection."
    );
}

// --- A throwing preference store: Open still opens, on the default hero ---
{
    grid.Reset();
    prefs.Reset();
    prefs.ThrowOnLoad = true;
    var opened = CreateState()
        .Open(CollectionOpenFacts.OutOfRun(cachedCatalog: null, Array.Empty<BPPSupporterSample>()));
    AssertTrue(
        opened.HeroPreferenceFailure is InvalidOperationException,
        "Open must report the store's exception instead of throwing it."
    );
    AssertEqual(
        (CardHero?)CollectionPanelSelectionState.DefaultHero,
        opened.Selection.SelectedHero,
        "A failed preference load counts as absent and opens on the default hero."
    );
    AssertTrue(opened.Render.Model != null, "Open must still render after a failed load.");
}

// --- Open in a run ---
{
    grid.Reset();
    prefs.Reset();
    prefs.Stored = "Mak";
    var opened = CreateState()
        .Open(
            CollectionOpenFacts.InRun(
                CardHero.Dooley,
                3,
                EncounterIdsSnapshot.Empty,
                cachedCatalog: null,
                Array.Empty<BPPSupporterSample>()
            )
        );
    AssertEqual(0, prefs.LoadCount, "An in-run open must not load the remembered hero.");
    AssertEqual(
        new CollectionPanelSelectionState(
            CardHero.Dooley,
            CollectionPanelSelectionState.DefaultMerchantSourceKey,
            CollectionSourceKind.Merchant
        ),
        opened.Selection,
        "Without encounter ids (the Android host) a run opens on its hero and the default merchant."
    );
    AssertEqual(
        0,
        opened.Render.Model.EncounteredMerchantSourceKeys.Count,
        "Without encounter ids no merchant is marked encountered."
    );
}

{
    var aId = Guid.NewGuid();
    var bId = Guid.NewGuid();
    var cId = Guid.NewGuid();
    var merchantsCatalog = new DictionarySourceCatalog(
        Source("merchant:a", CollectionSourceKind.Merchant, sourceTemplateIds: new[] { aId }),
        Source(" merchant:a ", CollectionSourceKind.Merchant, sourceTemplateIds: new[] { bId }),
        Source("merchant:c", CollectionSourceKind.Merchant, sourceTemplateIds: new[] { cId })
    );
    grid.Reset();
    prefs.Reset();
    var opened = new CollectionViewState(grid, merchantsCatalog, dayTiers, prefs).Open(
        CollectionOpenFacts.InRun(
            CardHero.Vanessa,
            2,
            Encounter(choices: new[] { aId, bId, cId, aId }),
            cachedCatalog: null,
            Array.Empty<BPPSupporterSample>()
        )
    );
    var encountered = opened.Render.Model.EncounteredMerchantSourceKeys;
    AssertEqual(2, encountered.Count, "Each encountered merchant key must appear once.");
    AssertTrue(
        encountered.Contains("merchant:a") && encountered.Contains("merchant:c"),
        "Encountered merchant keys must be trimmed."
    );
}

// --- Open resets the search lifecycle ---
{
    grid.Reset();
    prefs.Reset();
    var state = CreateState();
    state.AcceptCatalog(
        new[] { Card("Sword", CardTier.Bronze, heroes: new[] { CardHero.Vanessa }) }
    );
    // The search starts expanded; collapse it so the reset is observable.
    AssertFalse(state.ToggleSearch().Model.SearchExpanded, "Precondition: search collapsed.");
    state.SetSearchQuery("sw");
    var opened = state.Open(
        CollectionOpenFacts.OutOfRun(cachedCatalog: null, Array.Empty<BPPSupporterSample>())
    );
    AssertTrue(
        opened.Render.Model.SearchExpanded,
        "Open must return the search to its default expanded state."
    );
    AssertEqual(string.Empty, opened.Render.Model.SearchQuery, "Open must clear the query.");
    AssertTrue(
        state.TickSearch(1f, isComposing: false) == null,
        "Open must cancel a pending search debounce."
    );
}

// --- A cached open prunes a source the resolved hero cannot see ---
{
    grid.Reset();
    prefs.Reset();
    // The shared catalog has no default merchant, so the default selection's source is
    // invisible once the cached catalog is accepted.
    var opened = CreateState()
        .Open(
            CollectionOpenFacts.OutOfRun(
                new[] { Card("Item", CardTier.Bronze, heroes: new[] { CardHero.Vanessa }) },
                Array.Empty<BPPSupporterSample>()
            )
        );
    AssertEqual(
        CollectionPanelSelectionState.DefaultMerchantSourceKey,
        opened.Selection.SelectedSourceKey,
        "Selection is the resolved open selection, before the prune."
    );
    AssertEqual(
        null,
        opened.Render.Model.SelectedSourceKey,
        "A cached open must prune an invisible source."
    );
}

// --- Close ---
{
    grid.Reset();
    prefs.Reset();
    var state = CreateState();
    state.Open(
        CollectionOpenFacts.OutOfRun(cachedCatalog: null, Array.Empty<BPPSupporterSample>())
    );
    state.BeginCatalogLoad();
    state.ToggleSearch();
    state.SetSearchQuery("sw");
    var publishesBefore = grid.PublishCount;
    var closed = state.Close();
    AssertTrue(closed.Model.IsLoading, "Close renders the loading state the panel closes with.");
    AssertTrue(
        closed.Model.SearchExpanded && closed.Model.SearchQuery.Length == 0,
        "Close must reset the search to its default expanded, empty state."
    );
    AssertTrue(
        state.TickSearch(1f, isComposing: false) == null,
        "Close must cancel a pending search debounce."
    );
    AssertFalse(closed.ResetScroll || closed.ResetControlsScroll, "Close resets no scroll.");
    AssertFalse(state.Rebuild().Model.IsLoading, "Close must clear the loading flag.");
    AssertEqual(publishesBefore, grid.PublishCount, "Close must not publish.");
    AssertEqual(
        CollectionCatalogReadiness.Loading,
        state.CatalogReadiness,
        "Close must leave catalog readiness alone."
    );

    AssertFalse(state.Close().Model.IsLoading, "A second Close is harmless.");

    var cards = new[] { Card("Sword", CardTier.Bronze, heroes: new[] { CardHero.Vanessa }) };
    state.Open(CollectionOpenFacts.OutOfRun(cards, Array.Empty<BPPSupporterSample>()));
    AssertEqual(
        1,
        state.AcceptCatalog(cards).Model.VisibleCount,
        "A reopen after Close renders the accepted cards."
    );
}

// --- Open rejects null facts ---
{
    var state = CreateState();
    AssertThrows<ArgumentNullException>(() => state.Open(null!), "Open(null) must throw.");
    AssertThrows<ArgumentNullException>(
        () => CollectionOpenFacts.OutOfRun(null, null!),
        "OutOfRun must reject null supporters."
    );
    AssertThrows<ArgumentNullException>(
        () =>
            CollectionOpenFacts.InRun(CardHero.Vanessa, 1, EncounterIdsSnapshot.Empty, null, null!),
        "InRun must reject null supporters."
    );
}

// --- The open selection over a built source catalog ---
{
    var ailaId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var ailaId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    var globalMerchantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    var pygTrainerId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    var collisionId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    var entries = CollectionSourceCatalog.Build(
        $$"""
        {
          "schemaVersion": 4,
          "groups": [
            "generalist",
            "all-hero",
            "tag-type-specialist",
            "trainer"
          ],
          "entries": [
            {
              "name": "Aila",
              "kind": "Merchant",
              "group": "tag-type-specialist",
              "order": 0,
              "availableHeroes": ["Vanessa"],
              "description": "Sells Crit items",
              "portraitTemplateId": "{{ailaId1}}",
              "sourceTemplateIds": ["{{ailaId1}}", "{{ailaId2}}"],
              "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero", "hiddenTagsAny": ["Crit", "CritReference"] } }]
            },
            {
              "name": "Aila",
              "kind": "Merchant",
              "group": "tag-type-specialist",
              "order": 1,
              "availableHeroes": ["Dooley"],
              "description": "Sells Crit items",
              "portraitTemplateId": "{{collisionId}}",
              "sourceTemplateIds": ["{{collisionId}}"],
              "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero", "hiddenTagsAny": ["Crit", "CritReference"] } }]
            },
            {
              "name": "Nufu",
              "kind": "Merchant",
              "group": "generalist",
              "order": 0,
              "availableHeroes": [],
              "description": "Sells common items",
              "portraitTemplateId": "{{globalMerchantId}}",
              "sourceTemplateIds": ["{{globalMerchantId}}"],
              "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
            },
            {
              "name": "Professor Riggs",
              "kind": "Trainer",
              "group": "trainer",
              "order": 0,
              "availableHeroes": ["Pygmalien"],
              "description": "Teaches skills",
              "portraitTemplateId": "{{pygTrainerId}}",
              "sourceTemplateIds": ["{{pygTrainerId}}"],
              "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
            },
            {
              "name": "Nufu",
              "kind": "Merchant",
              "group": "all-hero",
              "order": 0,
              "availableHeroes": [],
              "description": "Second collision entry",
              "portraitTemplateId": "66666666-6666-6666-6666-666666666666",
              "sourceTemplateIds": ["66666666-6666-6666-6666-666666666666"],
              "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "AllHeroes" } }]
            }
          ]
        }
        """,
        _ => true
    );
    AssertEqual(5, entries.Count, "Precondition: the built catalog keeps all five entries.");
    var index = new CollectionSourceIndex(entries);
    var noDragonsCards = new[]
    {
        Card("Vanessa Item", CardTier.Bronze, heroes: new[] { CardHero.Vanessa }),
    };
    var dooleyAila = entries.Single(entry =>
        entry.Kind == CollectionSourceKind.Merchant
        && entry.Name == "Aila"
        && entry.AvailableHeroes.Contains(CardHero.Dooley)
    );
    var pygTrainer = entries.Single(entry =>
        entry.Kind == CollectionSourceKind.Trainer && entry.Name == "Professor Riggs"
    );
    var globalNufu = entries.First(entry =>
        entry.Kind == CollectionSourceKind.Merchant
        && entry.Name == "Nufu"
        && entry.OfferSegments[0].Rule.HeroMode == CollectionSourceHeroMode.SelectedHero
    );

    CollectionOpenOutcome OpenOn(CollectionOpenFacts facts, string? stored = null)
    {
        grid.Reset();
        prefs.Reset();
        prefs.Stored = stored;
        return new CollectionViewState(grid, index, dayTiers, prefs).Open(facts);
    }

    static CollectionOpenFacts OutOfRun(IReadOnlyList<CollectionCardVm>? cached = null) =>
        CollectionOpenFacts.OutOfRun(cached, Array.Empty<BPPSupporterSample>());

    static CollectionOpenFacts InRun(CardHero hero, EncounterIdsSnapshot encounter) =>
        CollectionOpenFacts.InRun(
            hero,
            1,
            encounter,
            cachedCatalog: null,
            Array.Empty<BPPSupporterSample>()
        );

    static CollectionPanelSelectionState Merchant(CardHero? hero, string sourceKey) =>
        new(hero, sourceKey, CollectionSourceKind.Merchant);

    AssertEqual(
        CollectionPanelSelectionState.Default,
        OpenOn(OutOfRun()).Selection,
        "Opening outside an in-game run should fall back to VAN + Jay Jay."
    );
    AssertEqual(
        (CardHero?)CardHero.TheDragons,
        OpenOn(OutOfRun(), stored: "TheDragons").Selection.SelectedHero,
        "Opening while the catalog loads should preserve a known concrete preference until an accepted roster can normalize it."
    );
    AssertEqual(
        Merchant(CardHero.Mak, CollectionPanelSelectionState.DefaultMerchantSourceKey),
        OpenOn(OutOfRun(noDragonsCards), stored: "Mak").Selection,
        "Opening outside a run should use the remembered explicit CollectionPanel hero."
    );
    AssertEqual(
        Merchant(null, CollectionPanelSelectionState.DefaultMerchantSourceKey),
        OpenOn(OutOfRun(noDragonsCards), stored: "Common").Selection,
        "Opening outside a run should preserve Common as a remembered hero."
    );
    AssertEqual(
        Merchant(null, CollectionPanelSelectionState.DefaultMerchantSourceKey),
        OpenOn(OutOfRun(noDragonsCards), stored: "TheDragons").Selection,
        "A known-but-unavailable preference should fall back to neutral for only the current session."
    );
    AssertEqual(
        CollectionPanelSelectionState.Default,
        OpenOn(OutOfRun(noDragonsCards), stored: "NotARealHero").Selection,
        "An invalid preference should behave like an absent first-open preference after deletion."
    );
    AssertEqual(
        Merchant(CardHero.Dooley, dooleyAila.SourceKey),
        OpenOn(
            InRun(CardHero.Dooley, Encounter(current: dooleyAila.SourceTemplateIds[0]))
        ).Selection,
        "Opening during a run should select the current concrete hero and merchant source."
    );
    var inRunWithRemembered = OpenOn(
        InRun(CardHero.Dooley, Encounter(current: dooleyAila.SourceTemplateIds[0])),
        stored: "Mak"
    );
    AssertEqual(
        Merchant(CardHero.Dooley, dooleyAila.SourceKey),
        inRunWithRemembered.Selection,
        "Opening during a run should keep the current run hero and source over a remembered hero."
    );
    AssertEqual(0, prefs.LoadCount, "Opening during a run must not load the remembered hero.");
    var atTrainer = OpenOn(InRun(CardHero.Pygmalien, Encounter(current: pygTrainerId)));
    AssertEqual(
        new CollectionPanelSelectionState(
            CardHero.Pygmalien,
            pygTrainer.SourceKey,
            CollectionSourceKind.Trainer
        ),
        atTrainer.Selection,
        "Opening during a run at a trainer should select the trainer source."
    );
    AssertEqual(
        CollectionTabKind.Skills,
        atTrainer.Render.Model.ActiveTab,
        "Opening during a run at a trainer should open the Skill tab."
    );
    AssertEqual(
        Merchant(CardHero.Vanessa, globalNufu.SourceKey),
        OpenOn(
            InRun(CardHero.Vanessa, Encounter(choices: new[] { globalMerchantId, pygTrainerId }))
        ).Selection,
        "Opening on the choice screen should select the first matching source in SelectionSet order."
    );
    AssertEqual(
        new CollectionPanelSelectionState(
            CardHero.Pygmalien,
            pygTrainer.SourceKey,
            CollectionSourceKind.Trainer
        ),
        OpenOn(
            InRun(CardHero.Pygmalien, Encounter(choices: new[] { pygTrainerId, globalMerchantId }))
        ).Selection,
        "Opening on a trainer choice should select the trainer source when it is the first visible match."
    );
    AssertEqual(
        Merchant(CardHero.Vanessa, CollectionPanelSelectionState.DefaultMerchantSourceKey),
        OpenOn(InRun(CardHero.Vanessa, Encounter(current: pygTrainerId))).Selection,
        "Opening during a run without a usable source should keep the run hero and fall back to Jay Jay."
    );
    AssertEqual(
        CollectionPanelSelectionState.Default,
        OpenOn(
            InRun(CardHero.Common, Encounter(current: dooleyAila.SourceTemplateIds[0]))
        ).Selection,
        "Opening during a run without a concrete hero should fall back to VAN + Jay Jay."
    );
}

// TickSearch fires after debounce
{
    grid.Reset();
    prefs.Reset();
    var state = CreateState();
    state.AcceptCatalog(
        new[] { Card("Sword", CardTier.Bronze, heroes: new[] { CardHero.Vanessa }) }
    );
    state.SetSearchQuery("sw");
    AssertTrue(state.TickSearch(0.05f, isComposing: false) == null, "Debounce not elapsed yet.");
    var fired = state.TickSearch(0.20f, isComposing: false);
    AssertTrue(fired != null, "TickSearch must render once debounce elapses.");
    AssertTrue(fired!.ResetScroll, "Search refresh must reset list scroll.");
}

Console.WriteLine("CollectionViewState.Tests: all assertions passed.");

// --- helpers ---

static CollectionCardVm Card(
    string name,
    CardTier tier,
    CardType type = CardType.Item,
    CardSize size = CardSize.Medium,
    bool isPackage = false,
    IReadOnlyCollection<CardTag>? tags = null,
    IReadOnlyCollection<CardHiddenTag>? hiddenTags = null,
    IReadOnlyCollection<CardHero>? heroes = null
) =>
    new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Size = size,
        StartingTier = tier,
        Tags = tags ?? Array.Empty<CardTag>(),
        HiddenTags = hiddenTags ?? Array.Empty<CardHiddenTag>(),
        DisplayName = name,
        InternalName = name,
        Heroes = heroes ?? new[] { CardHero.Common },
        IsPackage = isPackage,
        SearchText = name,
    };

static CollectionSourceEntry Source(
    string sourceKey,
    CollectionSourceKind kind,
    IReadOnlyList<CardHero>? availableHeroes = null,
    IReadOnlyList<Guid>? sourceTemplateIds = null
) =>
    new(
        sourceKey,
        kind,
        sourceKey,
        availableHeroes ?? Array.Empty<CardHero>(),
        string.Empty,
        Guid.NewGuid(),
        sourceTemplateIds ?? Array.Empty<Guid>(),
        new[]
        {
            new CollectionSourceOfferSegment(
                "all",
                CollectionSourceOfferSegmentKind.Normal,
                string.Empty,
                new CollectionSourceOfferRule(
                    CollectionSourceHeroMode.AllHeroes,
                    null,
                    null,
                    Array.Empty<CardSize>(),
                    Array.Empty<CardTag>(),
                    Array.Empty<CardTag>(),
                    Array.Empty<CardHiddenTag>(),
                    false,
                    Array.Empty<CardEnchantment>(),
                    Array.Empty<CardTag>(),
                    Array.Empty<CardHiddenTag>()
                )
            ),
        },
        "test",
        0,
        0
    );

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static EncounterIdsSnapshot Encounter(Guid? current = null, IReadOnlyList<Guid>? choices = null) =>
    new()
    {
        CurrentEncounterTemplateId = current,
        ChoiceSelectionEntryIds = Array.Empty<string>(),
        ChoiceSelectionTemplateIds = choices ?? Array.Empty<Guid>(),
    };

internal sealed class DictionarySourceCatalog : ICollectionSourceCatalog
{
    private readonly Dictionary<string, CollectionSourceEntry> _bySourceKey = new(
        StringComparer.Ordinal
    );

    // Insertion order: the open selection takes the first matching entry.
    private readonly List<CollectionSourceEntry> _entries = new();

    public DictionarySourceCatalog(params CollectionSourceEntry[] entries)
    {
        foreach (var entry in entries)
        {
            _bySourceKey[entry.SourceKey] = entry;
            _entries.Add(entry);
        }
    }

    public IReadOnlyList<CollectionSourceEntry> Entries => _entries;

    public bool TryGetBySourceKey(string sourceKey, out CollectionSourceEntry? entry) =>
        _bySourceKey.TryGetValue(sourceKey, out entry);

    public IEnumerable<CollectionSourceEntry> For(CollectionSourceKind kind, CardHero effectiveHero)
    {
        foreach (var entry in _entries)
        {
            if (entry.Kind != kind)
                continue;
            if (!entry.IsVisibleForHero(effectiveHero))
                continue;
            yield return entry;
        }
    }
}

internal sealed class FakeGridPort : ICollectionGridPort
{
    public bool Unavailable { get; set; }
    public int PublishCount { get; private set; }
    public IReadOnlyList<CollectionCardVm> LastPublishedCards { get; private set; } =
        Array.Empty<CollectionCardVm>();
    private CollectionGridProjection _last = CollectionGridProjection.Empty;
    private bool _hasPublished;

    public void Reset()
    {
        Unavailable = false;
        PublishCount = 0;
        LastPublishedCards = Array.Empty<CollectionCardVm>();
        _last = CollectionGridProjection.Empty;
        _hasPublished = false;
    }

    public CollectionGridProjection? Publish(
        IReadOnlyList<CollectionCardVm> cards,
        CollectionTabKind activeTab
    )
    {
        PublishCount++;
        if (Unavailable)
            return null;

        LastPublishedCards = cards;
        _last = new CollectionGridProjection(cards.Count, cards.Count * 10f);
        _hasPublished = true;
        return _last;
    }

    public CollectionGridProjection Current =>
        !_hasPublished || Unavailable ? CollectionGridProjection.Empty : _last;
}

internal sealed class FakeDayTier : ICollectionDayTierSource
{
    public int? Day { get; set; } = 1;

    public CollectionDayTiers Resolve() =>
        Day.HasValue
            ? new CollectionDayTiers(Day, CardTier.Diamond)
            : new CollectionDayTiers(null, null);
}

internal sealed class RecordingHeroPreferenceStore : ICollectionPanelHeroPreferenceStore
{
    public int SaveCount { get; private set; }
    public CardHero? LastSaved { get; private set; }

    // Null means no stored value.
    public string? Stored { get; set; }
    public bool ThrowOnLoad { get; set; }
    public int LoadCount { get; private set; }
    public CollectionCatalogReadiness? LastLoadReadiness { get; private set; }
    public IReadOnlyCollection<CardHero>? LastLoadHeroes { get; private set; }

    public void Reset()
    {
        SaveCount = 0;
        LastSaved = null;
        Stored = null;
        ThrowOnLoad = false;
        LoadCount = 0;
        LastLoadReadiness = null;
        LastLoadHeroes = null;
    }

    public CollectionPanelHeroPreferenceLoadResult Load(
        CollectionCatalogReadiness catalogReadiness,
        IReadOnlyCollection<CardHero> availableHeroes
    )
    {
        LoadCount++;
        LastLoadReadiness = catalogReadiness;
        LastLoadHeroes = availableHeroes;
        if (ThrowOnLoad)
            throw new InvalidOperationException("Preference store unavailable.");
        return CollectionPanelHeroPreference.ResolveStored(
            hasStoredValue: Stored != null,
            raw: Stored,
            catalogReadiness,
            availableHeroes
        );
    }

    public void Save(CardHero hero)
    {
        SaveCount++;
        LastSaved = hero;
    }
}
