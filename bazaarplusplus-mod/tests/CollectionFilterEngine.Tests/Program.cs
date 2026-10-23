using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

var searchRefreshGate = new CollectionSearchRefreshGate(0.16f);
AssertFalse(
    searchRefreshGate.Advance(1f),
    "An idle search refresh gate should not request a list rebuild."
);
searchRefreshGate.Schedule();
AssertFalse(
    searchRefreshGate.Advance(0.1f),
    "Search refresh should wait while the user is still typing."
);
searchRefreshGate.Schedule();
AssertFalse(
    searchRefreshGate.Advance(0.1f),
    "Another keystroke should restart the search refresh delay."
);
AssertTrue(
    searchRefreshGate.Advance(0.061f),
    "Search refresh should run once after the typing pause expires."
);
AssertFalse(
    searchRefreshGate.Advance(1f),
    "An elapsed search refresh should not run more than once."
);
searchRefreshGate.Schedule();
searchRefreshGate.Cancel();
AssertFalse(
    searchRefreshGate.Advance(1f),
    "Cancelling a pending search refresh should prevent a later rebuild."
);
searchRefreshGate.Schedule();
AssertFalse(
    searchRefreshGate.Advance(1f, isComposing: true),
    "Search refresh should remain paused while an IME composition is active."
);
AssertTrue(
    searchRefreshGate.IsPending,
    "IME composition should preserve the pending committed search refresh."
);
AssertFalse(
    searchRefreshGate.Advance(0.1f),
    "The debounce should resume from its pre-composition duration."
);
AssertTrue(
    searchRefreshGate.Advance(0.061f),
    "The committed query should refresh once after composition ends and debounce elapses."
);

var heroState = new CollectionFilterState();
AssertEqual(
    null,
    heroState.SelectedHero,
    "A new filter state should have no concrete hero selected."
);
AssertEqual(
    CardHero.Common,
    heroState.EffectiveHero,
    "A filter state without a concrete selection should query in the Common hero context."
);
heroState.ToggleHero(CardHero.Vanessa);
AssertEqual(
    CardHero.Vanessa,
    heroState.SelectedHero,
    "Selecting a concrete hero should select that hero."
);
heroState.ToggleHero(CardHero.Dooley);
AssertEqual(
    CardHero.Dooley,
    heroState.SelectedHero,
    "Selecting a second concrete hero should replace the first."
);
var neutralToggleValue = heroState.ToggleHero(CardHero.Dooley);
AssertEqual(
    null,
    heroState.SelectedHero,
    "Toggling the only selected hero should clear the hero selection."
);
AssertEqual(
    CardHero.Common,
    neutralToggleValue,
    "A toggle should return the post-toggle effective hero for persistence."
);
AssertEqual(
    CardHero.Common,
    heroState.EffectiveHero,
    "Clearing the selected hero should restore the Common query context."
);
heroState.ToggleHero(CardHero.Dooley);
AssertEqual(
    CardHero.Dooley,
    heroState.SelectedHero,
    "Selecting a concrete hero from neutral mode should remain single-select."
);

var allHeroesSameHeroState = new CollectionFilterState();
allHeroesSameHeroState.ToggleHero(CardHero.Vanessa);
allHeroesSameHeroState.ToggleAllHeroes();
allHeroesSameHeroState.ToggleHero(CardHero.Vanessa);
AssertEqual(
    false,
    allHeroesSameHeroState.AllHeroesSelected,
    "Selecting the residual hero from all-heroes should exit all-heroes."
);
AssertEqual(
    CardHero.Vanessa,
    allHeroesSameHeroState.SelectedHero,
    "Selecting the residual hero from all-heroes should preserve that hero as the scope."
);
AssertEqual(
    CardHero.Vanessa,
    allHeroesSameHeroState.EffectiveHero,
    "Selecting the residual hero from all-heroes should preserve that effective hero."
);

var allHeroesDifferentHeroState = new CollectionFilterState();
allHeroesDifferentHeroState.ToggleHero(CardHero.Vanessa);
allHeroesDifferentHeroState.ToggleAllHeroes();
allHeroesDifferentHeroState.ToggleHero(CardHero.Dooley);
AssertEqual(
    false,
    allHeroesDifferentHeroState.AllHeroesSelected,
    "Selecting a different hero from all-heroes should exit all-heroes."
);
AssertEqual(
    CardHero.Dooley,
    allHeroesDifferentHeroState.SelectedHero,
    "Selecting a different hero from all-heroes should use that hero as the scope."
);

var normalHeroToggleState = new CollectionFilterState();
normalHeroToggleState.ToggleHero(CardHero.Vanessa);
normalHeroToggleState.ToggleHero(CardHero.Vanessa);
AssertEqual(
    null,
    normalHeroToggleState.SelectedHero,
    "Selecting the active hero twice outside all-heroes should clear the hero scope."
);

var defaultSelection = CollectionPanelSelectionState.Default;
AssertEqual(
    CardHero.Vanessa,
    defaultSelection.SelectedHero,
    "Default panel selection should start on VAN/Vanessa."
);
AssertEqual(
    "merchant:jay-jay:global",
    defaultSelection.SelectedSourceKey,
    "Default panel selection should start on Jay Jay."
);
AssertEqual(
    CollectionSourceKind.Merchant,
    defaultSelection.SelectedSourceKind,
    "Default panel selection should target the Item merchant source rail."
);
AssertEqual(
    "BPP.CollectionPanel.SelectedHero.anonymous",
    CollectionPanelHeroPreference.BuildPrefsKey(null),
    "CollectionPanel hero preference should use an anonymous scope when no account scope is available."
);
AssertEqual(
    "BPP.CollectionPanel.SelectedHero.account%2Fone",
    CollectionPanelHeroPreference.BuildPrefsKey("account/one"),
    "CollectionPanel hero preference key should URI-escape account scopes."
);
AssertEqual(
    "Dooley",
    CollectionPanelHeroPreference.Serialize(CardHero.Dooley),
    "CollectionPanel hero preference should serialize enum names."
);
AssertTrue(
    CollectionPanelHeroPreference.TryParse("Dooley", out var parsedDooley)
        && parsedDooley == CardHero.Dooley,
    "CollectionPanel hero preference should parse a supported concrete hero."
);
AssertTrue(
    CollectionPanelHeroPreference.TryParse("Common", out var parsedCommon)
        && parsedCommon == CardHero.Common,
    "CollectionPanel hero preference should preserve Common as the neutral effective hero."
);
AssertFalse(
    CollectionPanelHeroPreference.TryParse("NotARealHero", out _),
    "CollectionPanel hero preference should reject unknown hero strings."
);
AssertFalse(
    CollectionPanelHeroPreference.TryParse("", out _),
    "CollectionPanel hero preference should reject empty values."
);
AssertEqual(
    CollectionPanelHeroPreferenceLoadStatus.Absent,
    CollectionPanelHeroPreference
        .ResolveStored(
            hasStoredValue: false,
            raw: null,
            CollectionCatalogReadiness.Loading,
            CollectionHeroSelectionRoster.BaseConcreteHeroes
        )
        .Status,
    "Missing preference state should remain distinguishable from invalid stored data."
);
AssertEqual(
    CollectionPanelHeroPreferenceLoadStatus.Invalid,
    CollectionPanelHeroPreference
        .ResolveStored(
            hasStoredValue: true,
            raw: "NotARealHero",
            CollectionCatalogReadiness.Accepted,
            CollectionHeroSelectionRoster.BaseConcreteHeroes
        )
        .Status,
    "Invalid preference state should be deletable without treating it as merely unavailable."
);
var resolvedDooleyPreference = CollectionPanelHeroPreference.ResolveStored(
    hasStoredValue: true,
    raw: "Dooley",
    CollectionCatalogReadiness.Accepted,
    CollectionHeroSelectionRoster.BaseConcreteHeroes
);
AssertEqual(
    CollectionPanelHeroPreferenceLoadStatus.Resolved,
    resolvedDooleyPreference.Status,
    "A known hero in the accepted roster should resolve."
);
AssertEqual(
    CardHero.Dooley,
    resolvedDooleyPreference.Hero,
    "Resolved preference state should retain its concrete hero."
);
var dragonsHero = CardHero.TheDragons;
var dragonsCatalogCards = new[]
{
    Card("Dragons Item", CardTier.Bronze, heroes: new[] { dragonsHero }),
    Card("Dragons Skill", CardTier.Bronze, type: CardType.Skill, heroes: new[] { dragonsHero }),
};
AssertValues(
    CollectionHeroSelectionRoster.ResolveAvailableHeroes(
        CollectionCatalogReadiness.Loading,
        dragonsCatalogCards
    ),
    CollectionHeroSelectionRoster.BaseConcreteHeroes,
    "A loading catalog should expose only the seven established concrete heroes."
);
AssertValues(
    CollectionHeroSelectionRoster.ResolveAvailableHeroes(
        CollectionCatalogReadiness.Accepted,
        new[] { Card("Vanessa Item", CardTier.Bronze, heroes: new[] { CardHero.Vanessa }) }
    ),
    CollectionHeroSelectionRoster.BaseConcreteHeroes,
    "An accepted catalog without The Dragons content should not expose an empty hero chip."
);
AssertValues(
    CollectionHeroSelectionRoster.ResolveAvailableHeroes(
        CollectionCatalogReadiness.Accepted,
        new[]
        {
            Card(
                "Dragons Package",
                CardTier.Bronze,
                isPackage: true,
                heroes: new[] { dragonsHero }
            ),
        }
    ),
    CollectionHeroSelectionRoster.BaseConcreteHeroes,
    "Package-only The Dragons content should not expose a chip whose normal Collection results are empty."
);
var acceptedDragonsRoster = CollectionHeroSelectionRoster.ResolveAvailableHeroes(
    CollectionCatalogReadiness.Accepted,
    dragonsCatalogCards
);
AssertValues(
    acceptedDragonsRoster,
    new[]
    {
        CardHero.Vanessa,
        CardHero.Dooley,
        CardHero.Pygmalien,
        CardHero.Karnok,
        CardHero.Mak,
        CardHero.Stelle,
        CardHero.Jules,
        dragonsHero,
    },
    "An accepted catalog with The Dragons content should append exactly one concrete hero chip after the existing seven."
);
var loadingRosterPolicyAfterAcceptedCatalog = CollectionHeroSelectionRoster.ResolveAvailableHeroes(
    CollectionCatalogReadiness.Loading,
    Array.Empty<CollectionCardVm>()
);
AssertValues(
    loadingRosterPolicyAfterAcceptedCatalog,
    CollectionHeroSelectionRoster.BaseConcreteHeroes,
    "Loading roster policy should project only the seven base chips even when the prior accepted policy result had eight."
);
var loadingPreferencePolicyAfterAcceptedCatalog = CollectionPanelHeroPreference.ResolveStored(
    hasStoredValue: true,
    raw: "TheDragons",
    CollectionCatalogReadiness.Loading,
    loadingRosterPolicyAfterAcceptedCatalog
);
AssertEqual(
    CollectionPanelHeroPreferenceLoadStatus.Resolved,
    loadingPreferencePolicyAfterAcceptedCatalog.Status,
    "Loading preference policy should retain a saved The Dragons preference."
);
AssertEqual(
    dragonsHero,
    loadingPreferencePolicyAfterAcceptedCatalog.Hero,
    "Loading preference policy should keep the saved runtime hero available for the next accepted catalog."
);
var resolvedDragonsPreference = CollectionPanelHeroPreference.ResolveStored(
    hasStoredValue: true,
    raw: "Hero8",
    CollectionCatalogReadiness.Accepted,
    acceptedDragonsRoster
);
AssertEqual(
    CollectionPanelHeroPreferenceLoadStatus.Resolved,
    resolvedDragonsPreference.Status,
    "An accepted catalog with The Dragons content should resolve a legacy saved preference."
);
AssertEqual(
    TheDragonsHeroId.CanonicalId,
    resolvedDragonsPreference.CanonicalRaw,
    "A resolved legacy preference should migrate to the canonical The Dragons identity."
);
var dragonsIdentityFilter = new CollectionFilterState();
dragonsIdentityFilter.ToggleHero(dragonsHero);
var misleadingCommonItem = Card(
    "The Dragons Decoy",
    CardTier.Bronze,
    heroes: new[] { CardHero.Common },
    artKey: "TheDragons/Hero8"
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { dragonsCatalogCards[0], misleadingCommonItem },
        dragonsIdentityFilter
    ),
    new[] { dragonsCatalogCards[0].Id },
    "The Dragons item filtering should use enum identity rather than names or art keys."
);
dragonsIdentityFilter.ActiveType = CardType.Skill;
var misleadingCommonSkill = Card(
    "Hero8 Skill Decoy",
    CardTier.Bronze,
    type: CardType.Skill,
    heroes: new[] { CardHero.Common },
    artKey: "TheDragons/Skill"
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { dragonsCatalogCards[1], misleadingCommonSkill },
        dragonsIdentityFilter
    ),
    new[] { dragonsCatalogCards[1].Id },
    "The Dragons skill filtering should use enum identity rather than names or art keys."
);
var loadingDragonsPreference = CollectionPanelHeroPreference.ResolveStored(
    hasStoredValue: true,
    raw: "Hero8",
    CollectionCatalogReadiness.Loading,
    CollectionHeroSelectionRoster.BaseConcreteHeroes
);
AssertEqual(
    CollectionPanelHeroPreferenceLoadStatus.Resolved,
    loadingDragonsPreference.Status,
    "Catalog loading must not misclassify a known preference as unavailable."
);
AssertEqual(
    TheDragonsHeroId.CanonicalId,
    loadingDragonsPreference.CanonicalRaw,
    "Known aliases should retain a canonical raw preference."
);
AssertEqual(
    CollectionPanelHeroPreferenceLoadStatus.Resolved,
    CollectionPanelHeroPreference
        .ResolveStored(
            hasStoredValue: true,
            raw: "TheDragons",
            CollectionCatalogReadiness.Unavailable,
            CollectionHeroSelectionRoster.BaseConcreteHeroes
        )
        .Status,
    "An unavailable catalog must not normalize or clear a known preference."
);
var unavailableDragonsPreference = CollectionPanelHeroPreference.ResolveStored(
    hasStoredValue: true,
    raw: "TheDragons",
    CollectionCatalogReadiness.Accepted,
    CollectionHeroSelectionRoster.BaseConcreteHeroes
);
AssertEqual(
    CollectionPanelHeroPreferenceLoadStatus.KnownUnavailable,
    unavailableDragonsPreference.Status,
    "Only an accepted roster may classify a known hero preference as unavailable."
);
AssertEqual(
    dragonsHero,
    unavailableDragonsPreference.Hero,
    "Known-unavailable preference state should retain its resolved runtime hero."
);
AssertEqual(
    dragonsHero,
    CollectionHeroSelectionRoster.NormalizeSelection(
        dragonsHero,
        CollectionCatalogReadiness.Loading,
        CollectionHeroSelectionRoster.BaseConcreteHeroes
    ),
    "Catalog loading must not clear an unresolved concrete selection."
);
AssertEqual(
    dragonsHero,
    CollectionHeroSelectionRoster.NormalizeSelection(
        dragonsHero,
        CollectionCatalogReadiness.Unavailable,
        CollectionHeroSelectionRoster.BaseConcreteHeroes
    ),
    "Catalog unavailability must not clear an unresolved concrete selection."
);
AssertEqual(
    null,
    CollectionHeroSelectionRoster.NormalizeSelection(
        dragonsHero,
        CollectionCatalogReadiness.Accepted,
        CollectionHeroSelectionRoster.BaseConcreteHeroes
    ),
    "Only an accepted roster may normalize an unavailable concrete selection to neutral."
);
AssertTrue(
    new CollectionFilterState().UseRunDayFilter,
    "New filter state should start with the day filter selected."
);
var selectionState = new CollectionFilterState();
selectionState.SelectedSourceKey = "trainer:old";
selectionState.ApplySelection(defaultSelection);
AssertEqual(
    CardHero.Vanessa,
    selectionState.SelectedHero,
    "Applying the default selection should select Vanessa in the filter state."
);
AssertEqual(
    "merchant:jay-jay:global",
    selectionState.SelectedSourceKey,
    "Applying the default selection should select Jay Jay in the filter state."
);
var legacyCommonSelection = new CollectionPanelSelectionState(
    CardHero.Common,
    CollectionPanelSelectionState.DefaultMerchantSourceKey,
    CollectionSourceKind.Merchant
);
AssertEqual(
    null,
    legacyCommonSelection.SelectedHero,
    "A legacy Common selection should normalize to no concrete UI hero."
);
selectionState.ApplySelection(legacyCommonSelection);
AssertEqual(
    null,
    selectionState.SelectedHero,
    "Applying a legacy Common selection should leave every concrete hero unselected."
);
AssertEqual(
    CardHero.Common,
    selectionState.EffectiveHero,
    "Applying a legacy Common selection should retain Common as the effective query hero."
);
var runtimeSelection = new CollectionPanelSelectionState(
    CardHero.Dooley,
    "merchant:jules:diamond:dooley+karnok+mak+pygmalien+stelle+vanessa",
    CollectionSourceKind.Merchant
);
selectionState.ApplySelection(runtimeSelection);
AssertEqual(
    CardHero.Dooley,
    selectionState.SelectedHero,
    "Runtime selection should replace the previous selected hero."
);
AssertEqual(
    "merchant:jules:diamond:dooley+karnok+mak+pygmalien+stelle+vanessa",
    selectionState.SelectedSourceKey,
    "Runtime selection should replace the previous selected merchant."
);
var trainerSelection = new CollectionPanelSelectionState(
    CardHero.Pygmalien,
    "trainer:mr-tuskari:pygmalien",
    CollectionSourceKind.Trainer
);
selectionState.SelectedSourceKey = "merchant:stale";
selectionState.ApplySelection(trainerSelection);
AssertEqual(
    CardType.Skill,
    selectionState.ActiveType,
    "Applying a trainer runtime selection should route the panel to the Skill tab."
);
AssertEqual(
    "trainer:mr-tuskari:pygmalien",
    selectionState.SelectedSourceKey,
    "Applying a trainer runtime selection should store the trainer source key."
);
selectionState.ApplySelection(runtimeSelection);
AssertEqual(
    CardType.Item,
    selectionState.ActiveType,
    "Applying a merchant runtime selection should route the panel back to the Item tab."
);

var sourceState = new CollectionFilterState();
sourceState.ToggleSource(CollectionTabKind.Items, "merchant:aila");
AssertEqual(
    "merchant:aila",
    sourceState.SelectedSourceKey,
    "Item source selection should store the merchant source key."
);
sourceState.ToggleSource(CollectionTabKind.Items, "merchant:helt");
AssertEqual(
    "merchant:helt",
    sourceState.SelectedSourceKey,
    "Selecting another item source should replace the prior merchant source."
);
sourceState.ToggleSource(CollectionTabKind.Items, "merchant:helt");
AssertEqual(
    null,
    sourceState.SelectedSourceKey,
    "Selecting the active item source again should clear it."
);
sourceState.ToggleSource(CollectionTabKind.Skills, "trainer:juliette");
AssertEqual(
    "trainer:juliette",
    sourceState.SelectedSourceKey,
    "Skill source selection should store the trainer source key."
);
AssertEqual(
    CardType.Skill,
    sourceState.ActiveType,
    "Toggling a Skill source should set Skill active."
);
var topLevelModeState = new CollectionFilterState { ActiveType = CardType.Skill };
AssertTrue(
    topLevelModeState.SelectTab(CollectionTabKind.Items),
    "Selecting Items from Skills should report a mode change."
);
AssertTrue(
    topLevelModeState.SelectTab(CollectionTabKind.Skills),
    "Selecting Skills from Items should report a mode change."
);
AssertEqual(
    CardType.Skill,
    topLevelModeState.ActiveType,
    "Selecting Skills should activate the Skill card type."
);
AssertFalse(
    topLevelModeState.SelectTab(CollectionTabKind.Skills),
    "Selecting the already active Skills tab should be a no-op."
);

sourceState.ActiveType = CardType.Item;
sourceState.SelectedSourceKey = "merchant:hidden";
AssertTrue(
    sourceState.PruneSelectedSource(new[] { "merchant:visible" }),
    "Pruning should report a change when a selected source is no longer visible."
);
AssertEqual(
    null,
    sourceState.SelectedSourceKey,
    "Pruning should clear invisible merchant source selection."
);
sourceState.ActiveType = CardType.Skill;
sourceState.SelectedSourceKey = "trainer:visible";
AssertFalse(
    sourceState.PruneSelectedSource(new[] { "trainer:visible" }),
    "Pruning should report no change when the selected source is still visible."
);
AssertEqual(
    "trainer:visible",
    sourceState.SelectedSourceKey,
    "Pruning should preserve visible trainer source selection."
);

var normal = Card("Normal", CardTier.Bronze);
var package = Card("Starter Package", CardTier.Silver, isPackage: true);
var bronzePackage = Card(
    "Bronze Package",
    CardTier.Bronze,
    isPackage: true,
    heroes: new[] { CardHero.Dooley },
    tags: new[] { CardTag.Tool },
    hiddenTags: new[] { CardHiddenTag.Package, CardHiddenTag.Shield }
);

var defaultPackageResult = CollectionFilterEngine.Apply(
    new[] { package, normal },
    new CollectionFilterState()
);
AssertSequence(defaultPackageResult, new[] { normal.Id }, "Packages are excluded by default.");

var matchingPackageFilter = new CollectionFilterState();
matchingPackageFilter.ToggleHero(CardHero.Dooley);
matchingPackageFilter.Tiers.Add(CardTier.Bronze);
matchingPackageFilter.Sizes.Add(CardSize.Medium);
matchingPackageFilter.Tags.Add(CardTag.Tool);
matchingPackageFilter.Keywords.Add(CardHiddenTag.Shield);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { bronzePackage, normal }, matchingPackageFilter),
    Array.Empty<Guid>(),
    "Package cards stay hidden from CollectionPanel even when their facets match."
);

var bronzeLarge = Card("A Bronze Large", CardTier.Bronze, size: CardSize.Large);
var bronzeSmall = Card("B Bronze Small", CardTier.Bronze, size: CardSize.Small);
var silverLarge = Card("C Silver Large", CardTier.Silver, size: CardSize.Large);
var silverSmall = Card("D Silver Small", CardTier.Silver, size: CardSize.Small);
var tierSizeResult = CollectionFilterEngine.Apply(
    new[] { silverSmall, bronzeLarge, silverLarge, bronzeSmall },
    new CollectionFilterState()
);
AssertSequence(
    tierSizeResult,
    new[] { bronzeSmall.Id, bronzeLarge.Id, silverSmall.Id, silverLarge.Id },
    "Visible cards sort by tier first, then by size within each tier."
);

var sizePriorityFilter = new CollectionFilterState { SortPriority = CollectionSortPriority.Size };
var sizePriorityResult = CollectionFilterEngine.Apply(
    new[] { silverSmall, bronzeLarge, silverLarge, bronzeSmall },
    sizePriorityFilter
);
AssertSequence(
    sizePriorityResult,
    new[] { bronzeSmall.Id, silverSmall.Id, bronzeLarge.Id, silverLarge.Id },
    "Size sort priority sorts by size first, then by tier within each size."
);

var offerPoolItem = Card("Offer Pool Item", CardTier.Bronze);
var offerPoolExcluded = Card("Offer Pool Excluded", CardTier.Bronze);

var offerPoolResult = CollectionFilterEngine.Apply(
    new[] { offerPoolExcluded, offerPoolItem, normal },
    new CollectionFilterState(),
    new CollectionFilterContext { OfferedCardIds = new[] { offerPoolItem.Id, Guid.NewGuid() } }
);
AssertSequence(
    offerPoolResult,
    new[] { offerPoolItem.Id },
    "Resolved offer pool should AND with the normal visible card filters."
);

var vanessaBronze = Card("Vanessa Bronze", CardTier.Bronze, heroes: new[] { CardHero.Vanessa });
var dooleyBronze = Card("Dooley Bronze", CardTier.Bronze, heroes: new[] { CardHero.Dooley });
var vanessaSilver = Card("Vanessa Silver", CardTier.Silver, heroes: new[] { CardHero.Vanessa });
var sourceAndHeroFilter = new CollectionFilterState();
sourceAndHeroFilter.ToggleHero(CardHero.Vanessa);
sourceAndHeroFilter.Tiers.Add(CardTier.Bronze);
var sourceAndHeroResult = CollectionFilterEngine.Apply(
    new[] { dooleyBronze, vanessaSilver, vanessaBronze },
    sourceAndHeroFilter,
    new CollectionFilterContext
    {
        OfferedCardIds = new[] { vanessaBronze.Id, vanessaSilver.Id, dooleyBronze.Id },
    }
);
AssertSequence(
    sourceAndHeroResult,
    new[] { vanessaBronze.Id },
    "Resolved offer pool should preserve AND semantics with hero and tier filters."
);

var sourceOwnedHeroFilter = new CollectionFilterState();
sourceOwnedHeroFilter.ToggleHero(CardHero.Vanessa);
var sourceOwnedHeroResult = CollectionFilterEngine.Apply(
    new[] { dooleyBronze, vanessaBronze },
    sourceOwnedHeroFilter,
    new CollectionFilterContext
    {
        OfferedCardIds = new[] { dooleyBronze.Id },
        ApplyHeroFilter = false,
    }
);
AssertSequence(
    sourceOwnedHeroResult,
    new[] { dooleyBronze.Id },
    "Selected source pools should not be cropped by the run-hero selector a second time."
);

var weaponSkill = Card(
    "Weapon Skill",
    CardTier.Bronze,
    type: CardType.Skill,
    tags: new[] { CardTag.Weapon }
);
var potionSkill = Card(
    "Potion Skill",
    CardTier.Bronze,
    type: CardType.Skill,
    tags: new[] { CardTag.Potion }
);
var skillTagFilter = new CollectionFilterState { ActiveType = CardType.Skill };
skillTagFilter.Tags.Add(CardTag.Weapon);
var skillTagResult = CollectionFilterEngine.Apply(
    new[] { potionSkill, weaponSkill, normal },
    skillTagFilter
);
AssertSequence(
    skillTagResult,
    new[] { weaponSkill.Id },
    "Player-facing type/tag filters should narrow the Skill tab."
);
var damageSkill = Card(
    "Damage Skill",
    CardTier.Bronze,
    type: CardType.Skill,
    hiddenTags: new[] { CardHiddenTag.Damage }
);
var shieldSkill = Card(
    "Shield Skill",
    CardTier.Bronze,
    type: CardType.Skill,
    hiddenTags: new[] { CardHiddenTag.Shield }
);
var damageShieldSkill = Card(
    "Damage Shield Skill",
    CardTier.Bronze,
    type: CardType.Skill,
    hiddenTags: new[] { CardHiddenTag.Damage, CardHiddenTag.Shield }
);
var skillKeywordFilter = new CollectionFilterState { ActiveType = CardType.Skill };
skillKeywordFilter.Keywords.Add(CardHiddenTag.Damage);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { shieldSkill, damageSkill, normal }, skillKeywordFilter),
    new[] { damageSkill.Id },
    "Skill keyword filters narrow skills by CardHiddenTag."
);
skillKeywordFilter.Keywords.Add(CardHiddenTag.Shield);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { shieldSkill, damageSkill, normal }, skillKeywordFilter),
    new[] { damageSkill.Id, shieldSkill.Id },
    "Multiple selected skill keywords OR together."
);
var allSkillKeywordFilter = new CollectionFilterState
{
    ActiveType = CardType.Skill,
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
allSkillKeywordFilter.Keywords.Add(CardHiddenTag.Damage);
allSkillKeywordFilter.Keywords.Add(CardHiddenTag.Shield);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldSkill, damageSkill, damageShieldSkill, normal },
        allSkillKeywordFilter
    ),
    new[] { damageShieldSkill.Id },
    "All keyword mode requires every selected skill keyword on the same card."
);

var weaponItem = Card("Weapon Item", CardTier.Bronze, tags: new[] { CardTag.Weapon });
var potionItem = Card("Potion Item", CardTier.Bronze, tags: new[] { CardTag.Potion });
var toolItem = Card("Tool Item", CardTier.Bronze, tags: new[] { CardTag.Tool });
var weaponPotionItem = Card(
    "Weapon Potion Item",
    CardTier.Bronze,
    tags: new[] { CardTag.Weapon, CardTag.Potion }
);
var itemTagFilter = new CollectionFilterState();
itemTagFilter.Tags.Add(CardTag.Weapon);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { potionItem, toolItem, weaponItem }, itemTagFilter),
    new[] { weaponItem.Id },
    "A single selected tag narrows items to cards carrying that tag."
);
itemTagFilter.Tags.Add(CardTag.Potion);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { potionItem, toolItem, weaponItem }, itemTagFilter),
    new[] { potionItem.Id, weaponItem.Id },
    "Multiple selected tags OR together, matching the other facet rows."
);
var allItemTagFilter = new CollectionFilterState { TagMatchMode = CollectionFacetMatchMode.All };
allItemTagFilter.Tags.Add(CardTag.Weapon);
allItemTagFilter.Tags.Add(CardTag.Potion);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { potionItem, toolItem, weaponItem, weaponPotionItem },
        allItemTagFilter
    ),
    new[] { weaponPotionItem.Id },
    "All tag mode requires every selected item tag on the same card."
);

var potionReferenceItem = Card(
    "Potion Reference Item",
    CardTier.Bronze,
    hiddenTags: new[] { CardHiddenTag.PotionReference }
);
var potionReferenceFilter = new CollectionFilterState();
potionReferenceFilter.Keywords.Add(CardHiddenTag.PotionReference);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { potionItem, potionReferenceItem }, potionReferenceFilter),
    new[] { potionReferenceItem.Id },
    "PotionReference should behave as its own hidden-tag keyword, separate from the Potion item tag."
);

var potionTagOnlyFilter = new CollectionFilterState();
potionTagOnlyFilter.Tags.Add(CardTag.Potion);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { potionItem, potionReferenceItem }, potionTagOnlyFilter),
    new[] { potionItem.Id },
    "Potion tag filtering should not match PotionReference-only cards."
);

var damageItem = Card(
    "Damage Item",
    CardTier.Bronze,
    tags: new[] { CardTag.Weapon },
    hiddenTags: new[] { CardHiddenTag.Damage }
);
var shieldItem = Card(
    "Shield Item",
    CardTier.Bronze,
    tags: new[] { CardTag.Weapon },
    hiddenTags: new[] { CardHiddenTag.Shield }
);
var damageShieldItem = Card(
    "Damage Shield Item",
    CardTier.Bronze,
    tags: new[] { CardTag.Weapon },
    hiddenTags: new[] { CardHiddenTag.Damage, CardHiddenTag.Shield }
);
var damageToolItem = Card(
    "Damage Tool Item",
    CardTier.Bronze,
    tags: new[] { CardTag.Tool },
    hiddenTags: new[] { CardHiddenTag.Damage }
);
var damageReferenceItem = Card(
    "Damage Reference Item",
    CardTier.Bronze,
    hiddenTags: new[] { CardHiddenTag.DamageReference }
);
var multicastItem = Card(
    "Multicast Item",
    CardTier.Bronze,
    mechanics: CollectionMechanic.Multicast
);
var damageMulticastItem = Card(
    "Damage Multicast Item",
    CardTier.Bronze,
    hiddenTags: new[] { CardHiddenTag.Damage },
    mechanics: CollectionMechanic.Multicast
);
var itemKeywordFilter = new CollectionFilterState();
itemKeywordFilter.Keywords.Add(CardHiddenTag.Damage);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldItem, damageToolItem, damageItem },
        itemKeywordFilter
    ),
    new[] { damageItem.Id, damageToolItem.Id },
    "Item keyword filters narrow items by CardHiddenTag."
);
itemKeywordFilter.Keywords.Add(CardHiddenTag.Shield);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldItem, damageToolItem, damageItem },
        itemKeywordFilter
    ),
    new[] { damageItem.Id, damageToolItem.Id, shieldItem.Id },
    "Multiple selected item keywords OR together."
);
var allItemKeywordFilter = new CollectionFilterState
{
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
allItemKeywordFilter.Keywords.Add(CardHiddenTag.Damage);
allItemKeywordFilter.Keywords.Add(CardHiddenTag.Shield);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldItem, damageToolItem, damageItem, damageShieldItem },
        allItemKeywordFilter
    ),
    new[] { damageShieldItem.Id },
    "All keyword mode requires every selected item keyword on the same card."
);
var multicastFilter = new CollectionFilterState();
multicastFilter.Mechanics.Add(CollectionMechanic.Multicast);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { damageItem, multicastItem, damageMulticastItem },
        multicastFilter
    ),
    new[] { damageMulticastItem.Id, multicastItem.Id },
    "Multicast filtering should match only cards carrying the cached Multicast mechanic."
);
var anyMulticastKeywordFilter = new CollectionFilterState();
anyMulticastKeywordFilter.Keywords.Add(CardHiddenTag.Damage);
anyMulticastKeywordFilter.Mechanics.Add(CollectionMechanic.Multicast);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { multicastItem, damageMulticastItem, damageItem, shieldItem },
        anyMulticastKeywordFilter
    ),
    new[] { damageItem.Id, damageMulticastItem.Id, multicastItem.Id },
    "Any keyword mode should OR Multicast with existing keywords."
);
var allMulticastKeywordFilter = new CollectionFilterState
{
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
allMulticastKeywordFilter.Keywords.Add(CardHiddenTag.Damage);
allMulticastKeywordFilter.Mechanics.Add(CollectionMechanic.Multicast);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { multicastItem, damageMulticastItem, damageItem },
        allMulticastKeywordFilter
    ),
    new[] { damageMulticastItem.Id },
    "All keyword mode should require Multicast and every other selected keyword on the same card."
);
var itemKeywordAndReferenceFilter = new CollectionFilterState();
itemKeywordAndReferenceFilter.Keywords.Add(CardHiddenTag.Damage);
itemKeywordAndReferenceFilter.Keywords.Add(CardHiddenTag.DamageReference);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { damageReferenceItem, shieldItem, damageItem },
        itemKeywordAndReferenceFilter
    ),
    new[] { damageItem.Id, damageReferenceItem.Id },
    "Keyword and reference selections share the same OR keyword facet."
);
var damageAndReferenceItem = Card(
    "Damage Reference Combo Item",
    CardTier.Bronze,
    hiddenTags: new[] { CardHiddenTag.Damage, CardHiddenTag.DamageReference }
);
var allItemKeywordAndReferenceFilter = new CollectionFilterState
{
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
allItemKeywordAndReferenceFilter.Keywords.Add(CardHiddenTag.Damage);
allItemKeywordAndReferenceFilter.Keywords.Add(CardHiddenTag.DamageReference);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { damageReferenceItem, damageItem, damageAndReferenceItem },
        allItemKeywordAndReferenceFilter
    ),
    new[] { damageAndReferenceItem.Id },
    "All keyword mode also requires curated reference keywords on the same card."
);
var itemTagAndKeywordFilter = new CollectionFilterState();
itemTagAndKeywordFilter.Tags.Add(CardTag.Weapon);
itemTagAndKeywordFilter.Keywords.Add(CardHiddenTag.Damage);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldItem, damageToolItem, damageItem },
        itemTagAndKeywordFilter
    ),
    new[] { damageItem.Id },
    "Item tags and item keywords combine as separate AND facets."
);

var instrumentTempoItem = InstrumentFacetCard();
var instrumentTempoWrongHero = InstrumentFacetCard(hero: CardHero.Dooley);
var instrumentTempoWrongTier = InstrumentFacetCard(tier: CardTier.Silver);
var instrumentTempoWrongSize = InstrumentFacetCard(size: CardSize.Large);
var instrumentTempoWrongTag = InstrumentFacetCard(tag: CardTag.Weapon);
var instrumentTempoWrongKeyword = InstrumentFacetCard(keyword: CardHiddenTag.TempoReference);
var instrumentTempoWrongSearch = InstrumentFacetCard(name: "Orchestral Practice");
var instrumentTempoNotOffered = InstrumentFacetCard(name: "Tempo Instrument Solo Encore");
var instrumentTempoItemFilter = new CollectionFilterState { SearchQuery = "instrument solo" };
instrumentTempoItemFilter.ToggleHero(CardHero.Vanessa);
instrumentTempoItemFilter.Tiers.Add(CardTier.Bronze);
instrumentTempoItemFilter.Sizes.Add(CardSize.Medium);
instrumentTempoItemFilter.Tags.Add(CardTag.Instrument);
instrumentTempoItemFilter.Keywords.Add(CardHiddenTag.Tempo);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[]
        {
            instrumentTempoWrongHero,
            instrumentTempoWrongTier,
            instrumentTempoWrongSize,
            instrumentTempoWrongTag,
            instrumentTempoWrongKeyword,
            instrumentTempoWrongSearch,
            instrumentTempoNotOffered,
            instrumentTempoItem,
        },
        instrumentTempoItemFilter,
        new CollectionFilterContext
        {
            OfferedCardIds = new[]
            {
                instrumentTempoItem.Id,
                instrumentTempoWrongHero.Id,
                instrumentTempoWrongTier.Id,
                instrumentTempoWrongSize.Id,
                instrumentTempoWrongTag.Id,
                instrumentTempoWrongKeyword.Id,
                instrumentTempoWrongSearch.Id,
            },
        }
    ),
    new[] { instrumentTempoItem.Id },
    "Instrument and Tempo item filters should intersect with hero, tier, size, search, and source."
);

var instrumentTempoReferenceSkill = InstrumentFacetCard(
    name: "Tempo Reference Instrument Skill",
    type: CardType.Skill,
    keyword: CardHiddenTag.TempoReference
);
var instrumentTempoReferenceWrongTagSkill = InstrumentFacetCard(
    name: "Tempo Reference Instrument Skill",
    type: CardType.Skill,
    tag: CardTag.Weapon,
    keyword: CardHiddenTag.TempoReference
);
var instrumentTempoReferenceWrongKeywordSkill = InstrumentFacetCard(
    name: "Tempo Reference Instrument Skill",
    type: CardType.Skill
);
var instrumentTempoReferenceWrongHeroSkill = InstrumentFacetCard(
    name: "Tempo Reference Instrument Skill",
    type: CardType.Skill,
    keyword: CardHiddenTag.TempoReference,
    hero: CardHero.Dooley
);
var instrumentTempoReferenceSkillFilter = new CollectionFilterState
{
    ActiveType = CardType.Skill,
    SearchQuery = "reference instrument",
};
instrumentTempoReferenceSkillFilter.ToggleHero(CardHero.Vanessa);
instrumentTempoReferenceSkillFilter.Tiers.Add(CardTier.Bronze);
instrumentTempoReferenceSkillFilter.Tags.Add(CardTag.Instrument);
instrumentTempoReferenceSkillFilter.Keywords.Add(CardHiddenTag.TempoReference);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[]
        {
            instrumentTempoReferenceWrongTagSkill,
            instrumentTempoReferenceWrongKeywordSkill,
            instrumentTempoReferenceWrongHeroSkill,
            instrumentTempoReferenceSkill,
        },
        instrumentTempoReferenceSkillFilter,
        new CollectionFilterContext
        {
            OfferedCardIds = new[]
            {
                instrumentTempoReferenceSkill.Id,
                instrumentTempoReferenceWrongTagSkill.Id,
                instrumentTempoReferenceWrongKeywordSkill.Id,
                instrumentTempoReferenceWrongHeroSkill.Id,
            },
        }
    ),
    new[] { instrumentTempoReferenceSkill.Id },
    "Instrument and TempoReference skill filters should intersect with hero, tier, search, and source."
);

var packageDestroyOnlyCard = Card(
    "Package Destroy Related",
    CardTier.Bronze,
    isPackage: true,
    mechanics: CollectionMechanic.Destroy
);
var packageDestroyAvailability = CollectionFacetAvailability.SnapshotFor(
    new[] { packageDestroyOnlyCard }
);
AssertFalse(
    packageDestroyAvailability.MechanicsFor(CardType.Item).Contains(CollectionMechanic.Destroy),
    "Package cards must never contribute Destroy chip availability."
);
AssertFalse(
    packageDestroyAvailability.MechanicsFor(CardType.Skill).Contains(CollectionMechanic.Destroy),
    "Package cards must never contribute Destroy chip availability on either tab."
);

var multicastOnlyMechanicCard = Card(
    "Alpha Multicast",
    CardTier.Bronze,
    mechanics: CollectionMechanic.Multicast
);
var destroyOnlyMechanicCard = Card(
    "Beta Destroy",
    CardTier.Bronze,
    mechanics: CollectionMechanic.Destroy
);
var multicastDestroyCard = Card(
    "Gamma Multicast Destroy",
    CardTier.Bronze,
    mechanics: CollectionMechanic.Multicast | CollectionMechanic.Destroy
);
var destroyMulticastAny = new CollectionFilterState();
destroyMulticastAny.Mechanics.Add(CollectionMechanic.Multicast);
destroyMulticastAny.Mechanics.Add(CollectionMechanic.Destroy);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { multicastDestroyCard, destroyOnlyMechanicCard, multicastOnlyMechanicCard },
        destroyMulticastAny
    ),
    new[] { multicastOnlyMechanicCard.Id, destroyOnlyMechanicCard.Id, multicastDestroyCard.Id },
    "Any mode should OR Destroy with Multicast."
);
var destroyMulticastAll = new CollectionFilterState
{
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
destroyMulticastAll.Mechanics.Add(CollectionMechanic.Multicast);
destroyMulticastAll.Mechanics.Add(CollectionMechanic.Destroy);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { multicastDestroyCard, destroyOnlyMechanicCard, multicastOnlyMechanicCard },
        destroyMulticastAll
    ),
    new[] { multicastDestroyCard.Id },
    "All mode should require both Destroy and Multicast on the same card."
);

var damageDestroyCard = Card(
    "Damage Destroy",
    CardTier.Bronze,
    hiddenTags: new[] { CardHiddenTag.Damage },
    mechanics: CollectionMechanic.Destroy
);
var destroyDamageAny = new CollectionFilterState();
destroyDamageAny.Keywords.Add(CardHiddenTag.Damage);
destroyDamageAny.Mechanics.Add(CollectionMechanic.Destroy);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { damageDestroyCard, destroyOnlyMechanicCard, damageItem },
        destroyDamageAny
    ),
    new[] { destroyOnlyMechanicCard.Id, damageDestroyCard.Id, damageItem.Id },
    "Any mode should OR Destroy with native hidden keywords."
);
var destroyDamageAll = new CollectionFilterState
{
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
destroyDamageAll.Keywords.Add(CardHiddenTag.Damage);
destroyDamageAll.Mechanics.Add(CollectionMechanic.Destroy);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { damageDestroyCard, destroyOnlyMechanicCard, damageItem },
        destroyDamageAll
    ),
    new[] { damageDestroyCard.Id },
    "All mode should require Destroy and native hidden keywords on the same card."
);

var primaryDestroyOrder = CollectionFacetAvailability.SnapshotFor(
    new[]
    {
        Card(
            "Damage Multicast Destroy Related",
            CardTier.Bronze,
            hiddenTags: new[] { CardHiddenTag.Damage, CardHiddenTag.DamageReference },
            mechanics: CollectionMechanic.Multicast | CollectionMechanic.Destroy
        ),
    }
);
AssertValues(
    primaryDestroyOrder
        .KeywordOptionsFor(CardType.Item)
        .Select(option => option.ToString())
        .ToArray(),
    new[]
    {
        nameof(CardHiddenTag.Damage),
        nameof(CollectionMechanic.Multicast),
        nameof(CollectionMechanic.Destroy),
        nameof(CardHiddenTag.DamageReference),
    },
    "Destroy should sort with Multicast in the primary area before Related."
);
var destroyOption = primaryDestroyOrder
    .KeywordOptionsFor(CardType.Item)
    .Single(option => option.Mechanic == CollectionMechanic.Destroy);
AssertFalse(destroyOption.IsRelated, "Destroy should never render in the Related subsection.");
AssertFalse(
    primaryDestroyOrder.ItemKeywords.Contains(CardHiddenTag.AbsorbDestroy),
    "AbsorbDestroy should not create a broad Destroy Related option."
);

AssertEqual(
    CardFacetOrder.PlayerFacingTags.Count,
    CardFacetOrder.PlayerFacingTags.Distinct().Count(),
    "Tag whitelist entries must be distinct."
);
AssertValues(
    CardFacetOrder.PlayerFacingTags.Select(tag => tag.ToString()).ToArray(),
    new[]
    {
        nameof(CardTag.Weapon),
        nameof(CardTag.Friend),
        nameof(CardTag.Aquatic),
        nameof(CardTag.Tool),
        nameof(CardTag.Vehicle),
        nameof(CardTag.Food),
        nameof(CardTag.Trap),
        nameof(CardTag.Toy),
        nameof(CardTag.Potion),
        nameof(CardTag.Reagent),
        nameof(CardTag.Relic),
        nameof(CardTag.Dragon),
        nameof(CardTag.Core),
        nameof(CardTag.Tech),
        nameof(CardTag.Dinosaur),
        nameof(CardTag.Apparel),
        nameof(CardTag.Merchant),
        nameof(CardTag.Property),
        nameof(CardTag.Loot),
        nameof(CardTag.Instrument),
        nameof(CardTag.Drone),
        nameof(CardTag.Ray),
    },
    "Tag whitelist should match the curated CollectionPanel tag order."
);
foreach (
    var mechanismTag in new[]
    {
        CardTag.Unsellable,
        CardTag.Unstashable,
        CardTag.Event,
        CardTag.Combat,
    }
)
    AssertFalse(
        CardFacetOrder.PlayerFacingTags.Contains(mechanismTag),
        $"Tag whitelist must exclude mechanism tag {mechanismTag}."
    );
foreach (
    var playerFacingTag in new[]
    {
        CardTag.Apparel,
        CardTag.Merchant,
        CardTag.Loot,
        CardTag.Weapon,
        CardTag.Instrument,
    }
)
    AssertTrue(
        CardFacetOrder.PlayerFacingTags.Contains(playerFacingTag),
        $"Tag whitelist should include player-facing type/tag {playerFacingTag}."
    );
foreach (var unusedTypeTag in new[] { CardTag.Ingredient, CardTag.Key, CardTag.Map, CardTag.Sigil })
    AssertFalse(
        CardFacetOrder.PlayerFacingTags.Contains(unusedTypeTag),
        $"Tag whitelist should exclude non-BazaarDB type/tag {unusedTypeTag}."
    );

AssertEqual(
    CollectionKeywordWhitelist.Ordered.Count,
    CollectionKeywordWhitelist.Ordered.Distinct().Count(),
    "Keyword whitelist entries must be distinct."
);
AssertValues(
    CollectionKeywordWhitelist.Ordered.Select(tag => tag.ToString()).ToArray(),
    new[]
    {
        nameof(CardHiddenTag.Quest),
        nameof(CardHiddenTag.Flying),
        nameof(CardHiddenTag.Haste),
        nameof(CardHiddenTag.Charge),
        nameof(CardHiddenTag.Cooldown),
        nameof(CardHiddenTag.Slow),
        nameof(CardHiddenTag.Freeze),
        nameof(CardHiddenTag.Damage),
        nameof(CardHiddenTag.Shield),
        nameof(CardHiddenTag.Heal),
        nameof(CardHiddenTag.Health),
        nameof(CardHiddenTag.Burn),
        nameof(CardHiddenTag.Poison),
        nameof(CardHiddenTag.Regen),
        nameof(CardHiddenTag.Crit),
        nameof(CardHiddenTag.Ammo),
        nameof(CardHiddenTag.Lifesteal),
        nameof(CardHiddenTag.Rage),
        nameof(CardHiddenTag.Gold),
        nameof(CardHiddenTag.Income),
        nameof(CardHiddenTag.Value),
        nameof(CardHiddenTag.Tempo),
        nameof(CardHiddenTag.QuestReference),
        nameof(CardHiddenTag.FlyingReference),
        nameof(CardHiddenTag.HasteReference),
        nameof(CardHiddenTag.CooldownReference),
        nameof(CardHiddenTag.SlowReference),
        nameof(CardHiddenTag.FreezeReference),
        nameof(CardHiddenTag.DamageReference),
        nameof(CardHiddenTag.ShieldReference),
        nameof(CardHiddenTag.HealReference),
        nameof(CardHiddenTag.HealthReference),
        nameof(CardHiddenTag.BurnReference),
        nameof(CardHiddenTag.PoisonReference),
        nameof(CardHiddenTag.RegenReference),
        nameof(CardHiddenTag.CritReference),
        nameof(CardHiddenTag.AmmoReference),
        nameof(CardHiddenTag.RageReference),
        nameof(CardHiddenTag.TempoReference),
        nameof(CardHiddenTag.EconomyReference),
        nameof(CardHiddenTag.PotionReference),
    },
    "Keyword whitelist should match the curated CollectionPanel keyword list."
);
foreach (
    var nonKeyword in new[]
    {
        CardHiddenTag.Merchant,
        CardHiddenTag.Package,
        CardHiddenTag.Unsellable,
    }
)
    AssertFalse(
        CollectionKeywordWhitelist.Ordered.Contains(nonKeyword),
        $"Keyword whitelist must exclude non-keyword tag {nonKeyword}."
    );
foreach (
    var referenceKeyword in new[]
    {
        CardHiddenTag.ChilledReference,
        CardHiddenTag.HeatedReference,
        CardHiddenTag.JoyReference,
        CardHiddenTag.TechReference,
    }
)
    AssertFalse(
        CollectionKeywordWhitelist.Ordered.Contains(referenceKeyword),
        $"Keyword whitelist should exclude non-curated reference tag {referenceKeyword}."
    );
foreach (
    var referenceKeyword in new[]
    {
        CardHiddenTag.DamageReference,
        CardHiddenTag.HealReference,
        CardHiddenTag.AmmoReference,
        CardHiddenTag.RageReference,
        CardHiddenTag.EconomyReference,
        CardHiddenTag.PotionReference,
    }
)
    AssertTrue(
        CollectionKeywordWhitelist.Ordered.Contains(referenceKeyword),
        $"Keyword whitelist should include curated reference tag {referenceKeyword}."
    );

AssertTrue(
    CollectionKeywordWhitelist.IsReferenceKeyword(CardHiddenTag.PotionReference),
    "PotionReference should start the keyword reference subsection like other reference keywords."
);
AssertTrue(
    CollectionKeywordWhitelist.IsReferenceKeyword(CardHiddenTag.TempoReference),
    "TempoReference should remain in the Related keyword subsection."
);
AssertFalse(
    CollectionKeywordWhitelist.IsReferenceKeyword(CardHiddenTag.Poison),
    "Base gameplay keywords should not be classified as reference keywords."
);
AssertFalse(
    CollectionKeywordWhitelist.IsRelatedKeyword(CardHiddenTag.Multicast),
    "Multicast should remain outside the Related keyword subsection."
);
AssertTrue(
    CollectionKeywordWhitelist.IsRelatedKeyword(CardHiddenTag.PotionReference),
    "Curated reference keywords should remain in the Related keyword subsection."
);
AssertFalse(
    CollectionKeywordWhitelist.IsRelatedKeyword(CardHiddenTag.Poison),
    "Base gameplay keywords should remain outside the Related keyword subsection."
);

AssertTrue(
    CardReferenceTags.TryResolveBase(CardHiddenTag.PotionReference, out var potionReferenceBase),
    "PotionReference should resolve to a base display tag."
);
AssertEqual(
    (CardTag?)CardTag.Potion,
    potionReferenceBase.CardTag,
    "PotionReference should render through the Potion card-tag display."
);
AssertEqual(
    (CardHiddenTag?)null,
    potionReferenceBase.HiddenTag,
    "PotionReference should not claim a hidden-tag base because no CardHiddenTag.Potion exists."
);

AssertTrue(
    CardReferenceTags.TryResolveBase(CardHiddenTag.PoisonReference, out var poisonReferenceBase),
    "Existing hidden-tag references should still resolve."
);
AssertEqual(
    (CardHiddenTag?)CardHiddenTag.Poison,
    poisonReferenceBase.HiddenTag,
    "PoisonReference should keep its Poison hidden-tag display base."
);
AssertEqual(
    (CardTag?)null,
    poisonReferenceBase.CardTag,
    "PoisonReference should not claim a card-tag display base."
);
AssertTrue(
    CardReferenceTags.TryResolveBase(CardHiddenTag.TempoReference, out var tempoReferenceBase),
    "TempoReference should resolve to the existing Tempo display base."
);
AssertEqual(
    (CardHiddenTag?)CardHiddenTag.Tempo,
    tempoReferenceBase.HiddenTag,
    "TempoReference should render with the Tempo hidden-tag typography."
);
AssertEqual(
    (CardTag?)null,
    tempoReferenceBase.CardTag,
    "TempoReference should not claim a card-tag display base."
);

AssertFalse(
    CardReferenceTags.TryResolveBase(CardHiddenTag.Poison, out _),
    "Non-reference hidden tags should not resolve through the reference base resolver."
);
foreach (
    var bazaarDbKeyword in new[]
    {
        CardHiddenTag.Ammo,
        CardHiddenTag.Crit,
        CardHiddenTag.Health,
        CardHiddenTag.Quest,
        CardHiddenTag.Value,
    }
)
    AssertTrue(
        CollectionKeywordWhitelist.Ordered.Contains(bazaarDbKeyword),
        $"Keyword whitelist should include BazaarDB keyword {bazaarDbKeyword}."
    );
foreach (
    var nonBazaarDbKeyword in new[]
    {
        CardHiddenTag.AbsorbDestroy,
        CardHiddenTag.AbsorbFreeze,
        CardHiddenTag.AbsorbSlow,
        CardHiddenTag.CanCrit,
        CardHiddenTag.Experience,
        CardHiddenTag.Level,
        CardHiddenTag.Reload,
        CardHiddenTag.Ticket,
    }
)
    AssertFalse(
        CollectionKeywordWhitelist.Ordered.Contains(nonBazaarDbKeyword),
        $"Keyword whitelist should exclude non-BazaarDB keyword {nonBazaarDbKeyword}."
    );
AssertFalse(
    CollectionKeywordWhitelist.Ordered.Contains(CardHiddenTag.Multicast),
    "The native keyword whitelist should not create a second Multicast option beside the structured mechanic."
);

var availableFacetCards = new[]
{
    Card(
        "Damage Weapon",
        CardTier.Bronze,
        tags: new[] { CardTag.Weapon, CardTag.Instrument, CardTag.Ingredient },
        hiddenTags: new[]
        {
            CardHiddenTag.Damage,
            CardHiddenTag.Tempo,
            CardHiddenTag.DamageReference,
            CardHiddenTag.CanCrit,
        }
    ),
    Card("Multicast Item", CardTier.Bronze, mechanics: CollectionMechanic.Multicast),
    Card(
        "Package Shield",
        CardTier.Bronze,
        isPackage: true,
        tags: new[] { CardTag.Tool },
        hiddenTags: new[] { CardHiddenTag.Package, CardHiddenTag.Shield }
    ),
    Card(
        "Skill Quest",
        CardTier.Bronze,
        type: CardType.Skill,
        hiddenTags: new[] { CardHiddenTag.Quest }
    ),
    Card(
        "Instrument Tempo Reference Skill",
        CardTier.Bronze,
        type: CardType.Skill,
        tags: new[] { CardTag.Instrument },
        hiddenTags: new[] { CardHiddenTag.TempoReference }
    ),
    Card("Potion Reference", CardTier.Bronze, hiddenTags: new[] { CardHiddenTag.PotionReference }),
};
var availableFacets = CollectionFacetAvailability.SnapshotFor(availableFacetCards);
AssertValues(
    availableFacets.ItemTags.Select(tag => tag.ToString()).ToArray(),
    new[] { nameof(CardTag.Weapon), nameof(CardTag.Instrument) },
    "Available item tags should include only non-package catalog tags that are player-facing."
);
AssertValues(
    availableFacets.SkillTags.Select(tag => tag.ToString()).ToArray(),
    new[] { nameof(CardTag.Instrument) },
    "Available skill tags should include player-facing tags carried by accepted skills."
);
AssertValues(
    availableFacets.ItemKeywords.Select(tag => tag.ToString()).ToArray(),
    new[]
    {
        nameof(CardHiddenTag.Damage),
        nameof(CardHiddenTag.Tempo),
        nameof(CardHiddenTag.DamageReference),
        nameof(CardHiddenTag.PotionReference),
    },
    "Available item keywords should include non-package catalog keywords and curated references from the same facet."
);
AssertValues(
    availableFacets.SkillKeywords.Select(tag => tag.ToString()).ToArray(),
    new[] { nameof(CardHiddenTag.Quest), nameof(CardHiddenTag.TempoReference) },
    "Available skill keywords should be computed independently from item keywords."
);
var legacyFacets = CollectionFacetAvailability.SnapshotFor(new[] { damageItem, damageSkill });
AssertFalse(
    legacyFacets.TagsFor(CardType.Item).Contains(CardTag.Instrument),
    "Legacy item catalogs should omit Instrument when no accepted item carries it."
);
AssertFalse(
    legacyFacets.TagsFor(CardType.Skill).Contains(CardTag.Instrument),
    "Legacy skill catalogs should omit Instrument when no accepted skill carries it."
);
AssertFalse(
    legacyFacets.KeywordsFor(CardType.Item).Contains(CardHiddenTag.Tempo),
    "Legacy item catalogs should omit Tempo when no accepted item carries it."
);
AssertFalse(
    legacyFacets.KeywordsFor(CardType.Skill).Contains(CardHiddenTag.TempoReference),
    "Legacy skill catalogs should omit TempoReference when no accepted skill carries it."
);
AssertFalse(
    legacyFacets.MechanicsFor(CardType.Item).Contains(CollectionMechanic.Multicast),
    "Item mechanic availability should omit Multicast when the item catalog has no Multicast template."
);
AssertFalse(
    availableFacets.MechanicsFor(CardType.Skill).Contains(CollectionMechanic.Multicast),
    "Skill mechanic availability should omit Multicast when only the item catalog contains it."
);

var queryCatalogCards = new[]
{
    Card(
        "Query Damage Weapon",
        CardTier.Bronze,
        tags: new[] { CardTag.Weapon },
        hiddenTags: new[] { CardHiddenTag.Damage },
        heroes: new[] { CardHero.Vanessa }
    ),
    Card(
        "Query Shield Tool",
        CardTier.Bronze,
        tags: new[] { CardTag.Tool },
        hiddenTags: new[] { CardHiddenTag.Shield },
        heroes: new[] { CardHero.Dooley }
    ),
};
var queryAvailability = new CollectionFacetAvailabilitySnapshot(
    new[] { CardTag.Weapon },
    Array.Empty<CardTag>(),
    new[] { CardHiddenTag.Damage },
    Array.Empty<CardHiddenTag>(),
    Array.Empty<CollectionMechanic>(),
    Array.Empty<CollectionMechanic>()
);
var querySource = Source(
    "merchant:query:vanessa",
    CollectionSourceKind.Merchant,
    availableHeroes: new[] { CardHero.Vanessa }
);
var queryCatalog = new DictionarySourceCatalog(querySource);
var queryResolver = new FakeOfferPoolResolver(
    new Dictionary<string, CollectionSourceOfferPoolResult>
    {
        [querySource.SourceKey] = CollectionSourceOfferPoolResult.Ready(
            new[] { queryCatalogCards[0].Id },
            null
        ),
    }
);
var queryFilter = new CollectionFilterState { SelectedSourceKey = querySource.SourceKey };
queryFilter.ToggleHero(CardHero.Vanessa);
queryFilter.Tags.Add(CardTag.Weapon);
queryFilter.Tags.Add(CardTag.Tool);
queryFilter.Keywords.Add(CardHiddenTag.Damage);
queryFilter.Keywords.Add(CardHiddenTag.Shield);
queryFilter.Mechanics.Add(CollectionMechanic.Multicast);
var queryResult = CollectionQuery.Run(
    queryCatalogCards,
    queryFilter,
    queryAvailability,
    queryCatalog,
    queryResolver
);
AssertSequence(
    queryResult.Cards,
    new[] { queryCatalogCards[0].Id },
    "CollectionQuery should return filter-engine ordered cards using resolved source offer pools."
);
AssertFalse(
    queryResult.Normalization.ClearSelectedSource,
    "A valid source selection should not request source clearing."
);
AssertValues(
    queryResult.Normalization.RetainedTags!.ToArray(),
    new[] { CardTag.Weapon },
    "CollectionQuery should return retained item tags when unavailable selected tags are trimmed."
);
AssertValues(
    queryResult.Normalization.RetainedKeywords!.ToArray(),
    new[] { CardHiddenTag.Damage },
    "CollectionQuery should return retained keywords when unavailable selected keywords are trimmed."
);
AssertEqual(
    0,
    queryResult.Normalization.RetainedMechanics!.Count,
    "CollectionQuery should trim unavailable mechanic selections through the same normalization."
);
AssertValues(
    queryFilter.Tags.ToArray(),
    new[] { CardTag.Weapon, CardTag.Tool },
    "CollectionQuery should not mutate selected tags."
);
AssertValues(
    queryFilter.Keywords.ToArray(),
    new[] { CardHiddenTag.Damage, CardHiddenTag.Shield },
    "CollectionQuery should not mutate selected keywords."
);
AssertTrue(
    queryFilter.Mechanics.Contains(CollectionMechanic.Multicast),
    "CollectionQuery should not mutate selected mechanics."
);
AssertEqual(
    querySource.SourceKey,
    queryFilter.SelectedSourceKey,
    "CollectionQuery should not mutate the selected source key."
);
AssertEqual(
    1,
    queryResolver.ResolveCount,
    "CollectionQuery should resolve a valid selected source once."
);

var neutralSelectedHeroSource = Source(
    "merchant:query:neutral",
    CollectionSourceKind.Merchant,
    heroMode: CollectionSourceHeroMode.SelectedHero
);
var neutralSelectedHeroResolver = new FakeOfferPoolResolver(
    new Dictionary<string, CollectionSourceOfferPoolResult>
    {
        [neutralSelectedHeroSource.SourceKey] = CollectionSourceOfferPoolResult.Ready(
            new[] { queryCatalogCards[0].Id },
            null
        ),
    }
);
CollectionQuery.Run(
    queryCatalogCards,
    new CollectionFilterState { SelectedSourceKey = neutralSelectedHeroSource.SourceKey },
    queryAvailability,
    new DictionarySourceCatalog(neutralSelectedHeroSource),
    neutralSelectedHeroResolver
);
AssertEqual(
    CardHero.Common,
    neutralSelectedHeroResolver.LastEffectiveHero,
    "A SelectedHero source should receive Common when no concrete hero chip is selected."
);
var neutralHeroSpecificSourceResult = CollectionQuery.Run(
    queryCatalogCards,
    new CollectionFilterState { SelectedSourceKey = querySource.SourceKey },
    queryAvailability,
    queryCatalog,
    queryResolver
);
AssertTrue(
    neutralHeroSpecificSourceResult.Normalization.ClearSelectedSource,
    "Entering neutral mode should prune a source that is available only to a concrete hero."
);

var allHeroesTrainerSource = Source(
    "trainer:query:all-heroes",
    CollectionSourceKind.Trainer,
    heroMode: CollectionSourceHeroMode.AllHeroes
);
var allHeroesTrainerCards = new[]
{
    Card(
        "All Heroes Vanessa Skill",
        CardTier.Bronze,
        type: CardType.Skill,
        heroes: new[] { CardHero.Vanessa }
    ),
    Card(
        "All Heroes Dooley Skill",
        CardTier.Bronze,
        type: CardType.Skill,
        heroes: new[] { CardHero.Dooley }
    ),
    Card(
        "All Heroes Common Skill",
        CardTier.Bronze,
        type: CardType.Skill,
        heroes: new[] { CardHero.Common }
    ),
};
var allHeroesTrainerResolver = new FakeOfferPoolResolver(
    new Dictionary<string, CollectionSourceOfferPoolResult>
    {
        [allHeroesTrainerSource.SourceKey] = CollectionSourceOfferPoolResult.Ready(
            allHeroesTrainerCards.Select(card => card.Id).ToArray(),
            null
        ),
    }
);
var allHeroesTrainerResult = CollectionQuery.Run(
    allHeroesTrainerCards,
    new CollectionFilterState
    {
        ActiveType = CardType.Skill,
        SelectedSourceKey = allHeroesTrainerSource.SourceKey,
    },
    CollectionFacetAvailability.SnapshotFor(allHeroesTrainerCards),
    new DictionarySourceCatalog(allHeroesTrainerSource),
    allHeroesTrainerResolver
);
AssertValues(
    allHeroesTrainerResult.Cards.Select(card => card.DisplayName).ToArray(),
    new[] { "All Heroes Common Skill", "All Heroes Dooley Skill", "All Heroes Vanessa Skill" },
    "An explicit AllHeroes source should retain its real cross-hero offer pool in neutral mode."
);

var unknownSourceFilter = new CollectionFilterState { SelectedSourceKey = "merchant:missing" };
var unknownSourceResult = CollectionQuery.Run(
    queryCatalogCards,
    unknownSourceFilter,
    queryAvailability,
    queryCatalog,
    queryResolver
);
AssertTrue(
    unknownSourceResult.Normalization.ClearSelectedSource,
    "CollectionQuery should request clearing an unknown selected source."
);
AssertEqual(
    "merchant:missing",
    unknownSourceFilter.SelectedSourceKey,
    "CollectionQuery should not directly clear unknown selected source keys."
);

var wrongKindFilter = new CollectionFilterState
{
    ActiveType = CardType.Skill,
    SelectedSourceKey = querySource.SourceKey,
};
var wrongKindResult = CollectionQuery.Run(
    queryCatalogCards,
    wrongKindFilter,
    queryAvailability,
    queryCatalog,
    queryResolver
);
AssertTrue(
    wrongKindResult.Normalization.ClearSelectedSource,
    "CollectionQuery should request clearing source selections whose kind does not match the active tab."
);
AssertEqual(
    querySource.SourceKey,
    wrongKindFilter.SelectedSourceKey,
    "CollectionQuery should not directly clear wrong-kind source selections."
);

var heroMismatchFilter = new CollectionFilterState { SelectedSourceKey = querySource.SourceKey };
heroMismatchFilter.ToggleHero(CardHero.Dooley);
var heroMismatchResult = CollectionQuery.Run(
    queryCatalogCards,
    heroMismatchFilter,
    queryAvailability,
    queryCatalog,
    queryResolver
);
AssertTrue(
    heroMismatchResult.Normalization.ClearSelectedSource,
    "CollectionQuery should request clearing selected sources that do not apply to the selected hero."
);
AssertEqual(
    querySource.SourceKey,
    heroMismatchFilter.SelectedSourceKey,
    "CollectionQuery should not directly clear hero-mismatched source selections."
);

var tagGateOffFilter = new CollectionFilterState { ActiveType = CardType.Skill };
tagGateOffFilter.Tags.Add(CardTag.Tool);
var tagGateOffResult = CollectionQuery.Run(
    queryCatalogCards,
    tagGateOffFilter,
    queryAvailability,
    queryCatalog,
    queryResolver
);
AssertEqual(
    0,
    tagGateOffResult.Normalization.RetainedTags?.Count,
    "CollectionQuery should prune a selected Skill tag that is unavailable in the accepted catalog."
);

var noSelectedFacetFilter = new CollectionFilterState();
var noSelectedFacetResult = CollectionQuery.Run(
    queryCatalogCards,
    noSelectedFacetFilter,
    queryAvailability,
    queryCatalog,
    queryResolver
);
AssertEqual(
    null,
    noSelectedFacetResult.Normalization.RetainedTags,
    "CollectionQuery should use null retained tags when no tags are selected."
);
AssertEqual(
    null,
    noSelectedFacetResult.Normalization.RetainedKeywords,
    "CollectionQuery should use null retained keywords when no keywords are selected."
);

var searchOutsideActiveFilters = Card(
    "Lighter",
    CardTier.Gold,
    size: CardSize.Large,
    tags: new[] { CardTag.Tool },
    hiddenTags: new[] { CardHiddenTag.Burn },
    heroes: new[] { CardHero.Dooley }
);
var searchGlobalFilter = new CollectionFilterState
{
    SelectedSourceKey = querySource.SourceKey,
    SearchQuery = "lighter",
};
searchGlobalFilter.ToggleHero(CardHero.Vanessa);
searchGlobalFilter.Tiers.Add(CardTier.Bronze);
searchGlobalFilter.Tags.Add(CardTag.Weapon);
searchGlobalFilter.Keywords.Add(CardHiddenTag.Damage);
searchGlobalFilter.Sizes.Add(CardSize.Small);
var searchGlobalResult = CollectionQuery.Run(
    new[] { queryCatalogCards[0], searchOutsideActiveFilters },
    searchGlobalFilter,
    queryAvailability,
    queryCatalog,
    queryResolver
);
AssertSequence(
    searchGlobalResult.Cards,
    Array.Empty<Guid>(),
    "Collection search should AND text with source, hero, day, tier, size, tag, and keyword filters."
);

var searchWithinFilters = new CollectionFilterState
{
    SelectedSourceKey = querySource.SourceKey,
    SearchQuery = "damage",
};
searchWithinFilters.ToggleHero(CardHero.Vanessa);
searchWithinFilters.Tags.Add(CardTag.Weapon);
searchWithinFilters.Keywords.Add(CardHiddenTag.Damage);
AssertSequence(
    CollectionQuery
        .Run(queryCatalogCards, searchWithinFilters, queryAvailability, queryCatalog, queryResolver)
        .Cards,
    new[] { queryCatalogCards[0].Id },
    "Collection search should return text matches that satisfy every selected filter."
);

var emptyTrimFilter = new CollectionFilterState();
emptyTrimFilter.Tags.Add(CardTag.Tool);
emptyTrimFilter.Keywords.Add(CardHiddenTag.Shield);
var emptyTrimResult = CollectionQuery.Run(
    queryCatalogCards,
    emptyTrimFilter,
    queryAvailability,
    queryCatalog,
    queryResolver
);
AssertEqual(
    0,
    emptyTrimResult.Normalization.RetainedTags!.Count,
    "CollectionQuery should return an empty non-null retained tag set when trimming clears every selected tag."
);
AssertEqual(
    0,
    emptyTrimResult.Normalization.RetainedKeywords!.Count,
    "CollectionQuery should return an empty non-null retained keyword set when trimming clears every selected keyword."
);

var vanessaExclusiveSkill = Card(
    "Vanessa Exclusive Skill",
    CardTier.Bronze,
    type: CardType.Skill,
    heroes: new[] { CardHero.Vanessa }
);
var sharedHeroSkill = Card(
    "Shared Hero Skill",
    CardTier.Bronze,
    type: CardType.Skill,
    heroes: new[] { CardHero.Vanessa, CardHero.Dooley }
);
var commonSkill = Card(
    "Common Skill",
    CardTier.Bronze,
    type: CardType.Skill,
    heroes: new[] { CardHero.Common }
);
var commonSharedSkill = Card(
    "Common Shared Skill",
    CardTier.Bronze,
    type: CardType.Skill,
    heroes: new[] { CardHero.Common, CardHero.Vanessa }
);
var missingHeroSkill = Card(
    "Missing Hero Skill",
    CardTier.Bronze,
    type: CardType.Skill,
    heroes: Array.Empty<CardHero>()
);
var emptyHeroSkillFilter = new CollectionFilterState { ActiveType = CardType.Skill };
var emptyHeroSkillResult = CollectionFilterEngine.Apply(
    new[]
    {
        sharedHeroSkill,
        commonSkill,
        commonSharedSkill,
        missingHeroSkill,
        vanessaExclusiveSkill,
    },
    emptyHeroSkillFilter
);
AssertSequence(
    emptyHeroSkillResult,
    new[] { commonSkill.Id },
    "No selected hero should show only Common-exclusive skills."
);
var exclusiveSkillFilter = new CollectionFilterState { ActiveType = CardType.Skill };
exclusiveSkillFilter.ToggleHero(CardHero.Vanessa);
var exclusiveSkillResult = CollectionFilterEngine.Apply(
    new[]
    {
        sharedHeroSkill,
        commonSkill,
        commonSharedSkill,
        missingHeroSkill,
        vanessaExclusiveSkill,
    },
    exclusiveSkillFilter
);
AssertSequence(
    exclusiveSkillResult,
    new[] { sharedHeroSkill.Id, vanessaExclusiveSkill.Id },
    "Skill hero filtering shows hero-exclusive plus general-shared (multi-hero, non-Common) skills; Common-scoped multi-hero skills stay hidden."
);

var exclusiveSkillSourceResult = CollectionFilterEngine.Apply(
    new[]
    {
        sharedHeroSkill,
        commonSkill,
        commonSharedSkill,
        missingHeroSkill,
        vanessaExclusiveSkill,
    },
    exclusiveSkillFilter,
    new CollectionFilterContext
    {
        OfferedCardIds = new[]
        {
            sharedHeroSkill.Id,
            commonSkill.Id,
            commonSharedSkill.Id,
            missingHeroSkill.Id,
            vanessaExclusiveSkill.Id,
        },
        ApplyHeroFilter = true,
    }
);
AssertSequence(
    exclusiveSkillSourceResult,
    new[] { sharedHeroSkill.Id, vanessaExclusiveSkill.Id },
    "Trainer/source pools AND with the widened skill hero scope, so shared skills surface in trainer pools too."
);

var commonSkillFilter = new CollectionFilterState { ActiveType = CardType.Skill };
var commonSkillResult = CollectionFilterEngine.Apply(
    new[]
    {
        sharedHeroSkill,
        commonSkill,
        commonSharedSkill,
        missingHeroSkill,
        vanessaExclusiveSkill,
    },
    commonSkillFilter
);
AssertSequence(
    commonSkillResult,
    new[] { commonSkill.Id },
    "Common skill filtering should only show skills explicitly scoped to Common/global — the general-shared bucket is empty by definition for Common."
);

AssertTrue(
    CollectionHeroScope.MatchesSkillHeroScope(
        new[] { CardHero.Vanessa, CardHero.Mak },
        CardHero.Vanessa
    ),
    "A {Vanessa, Mak} shared skill matches the Vanessa scope."
);
AssertFalse(
    CollectionHeroScope.MatchesSkillHeroScope(
        new[] { CardHero.Vanessa, CardHero.Mak },
        CardHero.Dooley
    ),
    "A {Vanessa, Mak} shared skill does not match an uninvolved hero."
);
AssertFalse(
    CollectionHeroScope.MatchesSkillHeroScope(
        new[] { CardHero.Common, CardHero.Vanessa },
        CardHero.Vanessa
    ),
    "Common-scoped multi-hero skills are not general-shared."
);

// --- Day filter: shared GameData MaximumTier gates StartingTier and ANDs with other dimensions. ---
var dayBronze = Card("Day Bronze", CardTier.Bronze);
var daySilver = Card("Day Silver", CardTier.Silver);
var dayGold = Card("Day Gold", CardTier.Gold);
var dayDiamond = Card("Day Diamond", CardTier.Diamond);
var dayLegendary = Card("Day Legendary", CardTier.Legendary);
var dayPool = new[] { dayBronze, daySilver, dayGold, dayDiamond, dayLegendary };

// The host derives the ceiling from GameData weights (GameDataDayTierTable.FromWeights, tested
// with the desktop resolver); the filter sees only the resulting tier.
var bronzeDayCeiling = CardTier.Bronze;
var goldDayCeiling = CardTier.Gold;
var diamondDayCeiling = CardTier.Diamond;

AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        new CollectionFilterState(),
        new CollectionFilterContext { DayMaximumTier = bronzeDayCeiling }
    ),
    new[] { dayBronze.Id },
    "A Bronze GameData ceiling keeps only Bronze-start cards."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        new CollectionFilterState(),
        new CollectionFilterContext { DayMaximumTier = goldDayCeiling }
    ),
    new[] { dayBronze.Id, daySilver.Id, dayGold.Id },
    "A Gold GameData ceiling keeps Bronze through Gold starts."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        new CollectionFilterState(),
        new CollectionFilterContext { DayMaximumTier = diamondDayCeiling }
    ),
    new[] { dayBronze.Id, daySilver.Id, dayGold.Id, dayDiamond.Id, dayLegendary.Id },
    "A Diamond GameData ceiling preserves Legendary-to-Diamond compatibility."
);
AssertSequence(
    CollectionFilterEngine.Apply(dayPool, new CollectionFilterState()),
    new[] { dayBronze.Id, daySilver.Id, dayGold.Id, dayDiamond.Id, dayLegendary.Id },
    "Missing or unavailable GameData fails open instead of guessing a ceiling."
);
var disabledDayFilter = new CollectionFilterState { UseRunDayFilter = false };
AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        disabledDayFilter,
        new CollectionFilterContext { DayMaximumTier = bronzeDayCeiling }
    ),
    new[] { dayBronze.Id, daySilver.Id, dayGold.Id, dayDiamond.Id, dayLegendary.Id },
    "Turning the Day filter off skips a trustworthy GameData ceiling."
);

var dayAndTierFilter = new CollectionFilterState();
dayAndTierFilter.Tiers.Add(CardTier.Diamond);
AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        dayAndTierFilter,
        new CollectionFilterContext { DayMaximumTier = goldDayCeiling }
    ),
    Array.Empty<Guid>(),
    "Manual Tier=Diamond ANDs with a Gold GameData ceiling to nothing."
);

AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        new CollectionFilterState(),
        new CollectionFilterContext
        {
            OfferedCardIds = new[] { dayGold.Id, dayLegendary.Id },
            DayMaximumTier = diamondDayCeiling,
        }
    ),
    new[] { dayGold.Id, dayLegendary.Id },
    "The GameData day predicate ANDs with the resolved offer pool."
);

// --- Day filter: fixed-tier sources (offer rule pins a starting tier) ignore the day gate. ---
AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        new CollectionFilterState(),
        new CollectionFilterContext
        {
            OfferedCardIds = new[] { dayGold.Id, dayDiamond.Id },
            SuppressDayGate = true,
            DayMaximumTier = bronzeDayCeiling,
        }
    ),
    new[] { dayGold.Id, dayDiamond.Id },
    "A fixed-tier source's pool is exempt from the day ceiling (Luxe on Day 1 still deals Diamond)."
);
var suppressedDayManualTier = new CollectionFilterState();
suppressedDayManualTier.Tiers.Add(CardTier.Gold);
AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        suppressedDayManualTier,
        new CollectionFilterContext
        {
            OfferedCardIds = new[] { dayGold.Id, dayDiamond.Id },
            SuppressDayGate = true,
            DayMaximumTier = bronzeDayCeiling,
        }
    ),
    new[] { dayGold.Id },
    "Suppressing the day gate leaves the manual Tier row in force."
);

var searchDamageReference = Card(
    "Internal Synergy",
    CardTier.Bronze,
    hiddenTags: new[] { CardHiddenTag.BurnReference },
    description: "When you <color=#00ffff>Slow</color>, Charge this {ability.0} seconds.",
    internalName: "Internal_Synergy_Card",
    artKey: "anglerfish_art"
);
var searchShield = Card(
    "Bulwark",
    CardTier.Bronze,
    hiddenTags: new[] { CardHiddenTag.Shield },
    description: "Gain {ability.0} Shield."
);
var searchChinese = Card(
    "深水琵琶鱼",
    CardTier.Bronze,
    description: "当你减速时，充能 {ability.0} 秒。"
);
var searchLighter = Card("Lighter", CardTier.Bronze, description: "Burn an item.");
var searchInitialism = Card("Molten Ball Blaster", CardTier.Bronze);
var searchRepeatedLetters = Card(
    "Dreaded Damage Dealer",
    CardTier.Bronze,
    internalName: "DreadedDamageDealer"
);
var searchLetterSoup = Card(
    "Lime",
    CardTier.Bronze,
    description: "Iguana Gold Honey Teapot Echo Rabbit."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchDamageReference, searchShield, searchChinese },
        new CollectionFilterState { SearchQuery = "slow charge" }
    ),
    new[] { searchDamageReference.Id },
    "Collection search should match normalized description text after stripping tooltip markup and ability placeholders."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchLighter, searchLetterSoup },
        new CollectionFilterState { SearchQuery = "lighter" }
    ),
    new[] { searchLighter.Id },
    "Collection search should not fuzzy-match a full card name across unrelated corpus words."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchLighter, searchLetterSoup },
        new CollectionFilterState { SearchQuery = "lightr" }
    ),
    Array.Empty<Guid>(),
    "Collection search should require a contiguous Latin substring instead of skipped letters."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchLighter },
        new CollectionFilterState { SearchQuery = "light" }
    ),
    new[] { searchLighter.Id },
    "Collection search should preserve conventional partial-word matching."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchInitialism, searchLighter },
        new CollectionFilterState { SearchQuery = "mbb" }
    ),
    new[] { searchInitialism.Id },
    "Collection search should match a card name by its word initialism."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchInitialism },
        new CollectionFilterState { SearchQuery = "mxb" }
    ),
    Array.Empty<Guid>(),
    "Initialism matching should compare word initials rather than skip arbitrary letters."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchRepeatedLetters },
        new CollectionFilterState { SearchQuery = "dddd" }
    ),
    Array.Empty<Guid>(),
    "Repeated Latin letters should not jump across a long internal word."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchDamageReference, searchShield },
        new CollectionFilterState { SearchQuery = "burn related" }
    ),
    new[] { searchDamageReference.Id },
    "Collection search should match internal reference hidden tags as related content."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchDamageReference, searchShield },
        new CollectionFilterState { SearchQuery = "angler" }
    ),
    new[] { searchDamageReference.Id },
    "Collection search should match internal art keys."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchDamageReference, searchShield },
        new CollectionFilterState { SearchQuery = "internal card" }
    ),
    new[] { searchDamageReference.Id },
    "Collection search should match contiguous internal-name terms with all query terms required."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchDamageReference, searchShield },
        new CollectionFilterState { SearchQuery = "ability" }
    ),
    Array.Empty<Guid>(),
    "Collection search should remove unresolved ability template tokens from the searchable text."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchDamageReference, searchShield, searchChinese },
        new CollectionFilterState { SearchQuery = "减速 充能" }
    ),
    new[] { searchChinese.Id },
    "Collection search should match Chinese description text without pinyin."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchDamageReference, searchShield, searchChinese },
        new CollectionFilterState { SearchQuery = "减速充能" }
    ),
    new[] { searchChinese.Id },
    "Collection search should match compact Chinese queries against normalized description text."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { searchDamageReference, searchShield },
        new CollectionFilterState { SearchQuery = "intnl" }
    ),
    Array.Empty<Guid>(),
    "Collection search should not treat Latin abbreviations as skipped-letter matches."
);
var prebuiltSearchText = Card("Surface", CardTier.Bronze, searchText: "localized fallback corpus");
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { prebuiltSearchText, searchShield },
        new CollectionFilterState { SearchQuery = "fallback corpus" }
    ),
    new[] { prebuiltSearchText.Id },
    "Collection search should honor prebuilt card search text."
);

// --- Card facet name maps: each host adapter maps its game enum by member name (ADR-0015). ---
var legacyHeroMap = new CardFacetNameMap<LegacyBuildHero, CardHero>(TheDragonsAliases());
AssertTrue(
    legacyHeroMap.TryToFacet(LegacyBuildHero.Hero8, out var legacyDragons)
        && legacyDragons == CardHero.TheDragons,
    "A legacy build's Hero8 should map to the canonical The Dragons facet."
);
AssertTrue(
    legacyHeroMap.TryToSource(CardHero.TheDragons, out var legacySource)
        && legacySource == LegacyBuildHero.Hero8,
    "The Dragons should map back to the only name a legacy build defines."
);
var canonicalHeroMap = new CardFacetNameMap<CanonicalBuildHero, CardHero>(TheDragonsAliases());
AssertTrue(
    canonicalHeroMap.TryToSource(CardHero.TheDragons, out var canonicalSource)
        && canonicalSource == CanonicalBuildHero.TheDragons,
    "A canonical build should map The Dragons to its own member."
);
var transitionalHeroMap = new CardFacetNameMap<TransitionalBuildHero, CardHero>(
    TheDragonsAliases()
);
AssertTrue(
    transitionalHeroMap.TryToSource(CardHero.TheDragons, out var transitionalSource)
        && transitionalSource == TransitionalBuildHero.TheDragons,
    "A build defining both names should prefer the canonical member, whatever its order."
);
AssertValues(
    transitionalHeroMap
        .ToFacetSet(new[] { TransitionalBuildHero.Hero8, TransitionalBuildHero.TheDragons })
        .ToArray(),
    new[] { CardHero.TheDragons },
    "Both names of one hero should collapse to one facet."
);
var heroWithoutAliases = new CardFacetNameMap<LegacyBuildHero, CardHero>();
AssertFalse(
    heroWithoutAliases.TryToFacet(LegacyBuildHero.Hero8, out _),
    "A game member without a facet or alias maps to nothing."
);
AssertValues(
    heroWithoutAliases.UnmappedSourceNames,
    new[] { nameof(LegacyBuildHero.Hero8) },
    "Unmapped game member names are reported for the vocabulary pin test."
);
AssertFalse(
    canonicalHeroMap.TryToSource(CardHero.Mak, out _),
    "A facet the game build lacks has no game value."
);

Console.WriteLine("CollectionFilterEngine checks passed.");

static Dictionary<string, CardHero> TheDragonsAliases() =>
    new(StringComparer.OrdinalIgnoreCase) { [TheDragonsHeroId.LegacyId] = CardHero.TheDragons };

static CollectionCardVm InstrumentFacetCard(
    string name = "Tempo Instrument Solo",
    CardType type = CardType.Item,
    CardTier tier = CardTier.Bronze,
    CardSize size = CardSize.Medium,
    CardTag tag = CardTag.Instrument,
    CardHiddenTag keyword = CardHiddenTag.Tempo,
    CardHero hero = CardHero.Vanessa
) =>
    Card(
        name,
        tier,
        type,
        size,
        tags: new[] { tag },
        hiddenTags: new[] { keyword },
        heroes: new[] { hero }
    );

static CollectionCardVm Card(
    string name,
    CardTier tier,
    CardType type = CardType.Item,
    CardSize size = CardSize.Medium,
    bool isPackage = false,
    IReadOnlyCollection<CardTag>? tags = null,
    IReadOnlyCollection<CardHiddenTag>? hiddenTags = null,
    IReadOnlyCollection<CardHero>? heroes = null,
    string? description = null,
    string? internalName = null,
    string? artKey = null,
    IReadOnlyDictionary<CardEnchantment, CollectionCardEnchantmentFacets>? enchantments = null,
    string? searchText = null,
    CollectionMechanic mechanics = CollectionMechanic.None
) =>
    new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Size = size,
        StartingTier = tier,
        Tags = tags ?? Array.Empty<CardTag>(),
        HiddenTags = hiddenTags ?? Array.Empty<CardHiddenTag>(),
        Mechanics = mechanics,
        DisplayName = name,
        Description = description ?? string.Empty,
        InternalName = internalName ?? name,
        ArtKey = artKey ?? string.Empty,
        IsPackage = isPackage,
        Heroes = heroes ?? new[] { CardHero.Common },
        Enchantments =
            enchantments ?? new Dictionary<CardEnchantment, CollectionCardEnchantmentFacets>(),
        SearchText = searchText ?? string.Empty,
    };

static CollectionSourceEntry Source(
    string sourceKey,
    CollectionSourceKind kind,
    IReadOnlyList<CardHero>? availableHeroes = null,
    bool suppressDayGate = false,
    CollectionSourceHeroMode? heroMode = null
) =>
    new(
        sourceKey,
        kind,
        sourceKey,
        availableHeroes ?? Array.Empty<CardHero>(),
        string.Empty,
        Guid.NewGuid(),
        Array.Empty<Guid>(),
        heroMode.HasValue ? new[] { HeroSegment(heroMode.Value) }
            : suppressDayGate ? new[] { FixedTierSegment() }
            : Array.Empty<CollectionSourceOfferSegment>(),
        "test",
        0,
        0
    );

static CollectionSourceOfferSegment FixedTierSegment() =>
    new(
        "fixed",
        CollectionSourceOfferSegmentKind.Normal,
        string.Empty,
        new CollectionSourceOfferRule(
            CollectionSourceHeroMode.AllHeroes,
            null,
            new CollectionSourceStartingTierRule(
                CollectionSourceStartingTierMode.Exact,
                CardTier.Gold
            ),
            Array.Empty<CardSize>(),
            Array.Empty<CardTag>(),
            Array.Empty<CardTag>(),
            Array.Empty<CardHiddenTag>(),
            false,
            Array.Empty<CardEnchantment>(),
            Array.Empty<CardTag>(),
            Array.Empty<CardHiddenTag>()
        )
    );

static CollectionSourceOfferSegment HeroSegment(CollectionSourceHeroMode heroMode) =>
    new(
        "hero",
        CollectionSourceOfferSegmentKind.Normal,
        string.Empty,
        new CollectionSourceOfferRule(
            heroMode,
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
    );

static void AssertSequence(
    IReadOnlyList<CollectionCardVm> actual,
    IReadOnlyList<Guid> expected,
    string message
)
{
    var actualIds = new Guid[actual.Count];
    for (var i = 0; i < actual.Count; i++)
        actualIds[i] = actual[i].Id;
    AssertValues(actualIds, expected, message);
}

static void AssertValues<T>(IReadOnlyList<T> actual, IReadOnlyList<T> expected, string message)
{
    if (actual.Count != expected.Count)
        throw new InvalidOperationException(
            $"{message} Expected {expected.Count} values, got {actual.Count}."
        );
    for (var i = 0; i < actual.Count; i++)
    {
        if (!EqualityComparer<T>.Default.Equals(actual[i], expected[i]))
            throw new InvalidOperationException(
                $"{message} At {i}: expected {expected[i]}, got {actual[i]}."
            );
    }
}

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

internal enum LegacyBuildHero
{
    Common,
    Vanessa,
    Hero8,
}

internal enum CanonicalBuildHero
{
    Common,
    TheDragons,
}

internal enum TransitionalBuildHero
{
    Common,
    TheDragons,
    Hero8,
}

internal sealed class DictionarySourceCatalog : ICollectionSourceCatalog
{
    private readonly Dictionary<string, CollectionSourceEntry> _entries = new(
        StringComparer.Ordinal
    );

    public DictionarySourceCatalog(params CollectionSourceEntry[] entries)
    {
        foreach (var entry in entries)
            _entries[entry.SourceKey] = entry;
    }

    public bool TryGetBySourceKey(string sourceKey, out CollectionSourceEntry? entry) =>
        _entries.TryGetValue(sourceKey, out entry);

    public IEnumerable<CollectionSourceEntry> For(CollectionSourceKind kind, CardHero effectiveHero)
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.Kind != kind)
                continue;
            if (!entry.IsVisibleForHero(effectiveHero))
                continue;
            yield return entry;
        }
    }
}

internal sealed class FakeOfferPoolResolver : ICollectionOfferPoolResolver
{
    private readonly IReadOnlyDictionary<string, CollectionSourceOfferPoolResult> _results;

    public FakeOfferPoolResolver(
        IReadOnlyDictionary<string, CollectionSourceOfferPoolResult> results
    )
    {
        _results = results;
    }

    public int ResolveCount { get; private set; }

    public CardHero? LastEffectiveHero { get; private set; }

    public CollectionSourceOfferPoolResult GetOrResolve(
        CollectionSourceEntry source,
        CardHero effectiveHero,
        IReadOnlyList<CollectionCardVm> catalogCards
    )
    {
        ResolveCount++;
        LastEffectiveHero = effectiveHero;
        return _results.TryGetValue(source.SourceKey, out var result)
            ? result
            : CollectionSourceOfferPoolResult.NoneSelected();
    }
}
