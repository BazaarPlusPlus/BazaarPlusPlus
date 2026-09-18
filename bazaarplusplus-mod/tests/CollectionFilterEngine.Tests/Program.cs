using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Enchantments;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Quests;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Effect.Trigger;
using BazaarGameShared.Domain.Tooltips;
using BazaarPlusPlus.Game.CardTags;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.GameInterop.DayTiers;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.GameInterop.TagTypography;

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
    EHero.Common,
    heroState.EffectiveHero,
    "A filter state without a concrete selection should query in the Common hero context."
);
heroState.ToggleHero(EHero.Vanessa);
AssertEqual(
    EHero.Vanessa,
    heroState.SelectedHero,
    "Selecting a concrete hero should select that hero."
);
heroState.ToggleHero(EHero.Dooley);
AssertEqual(
    EHero.Dooley,
    heroState.SelectedHero,
    "Selecting a second concrete hero should replace the first."
);
var neutralToggleValue = heroState.ToggleHero(EHero.Dooley);
AssertEqual(
    null,
    heroState.SelectedHero,
    "Toggling the only selected hero should clear the hero selection."
);
AssertEqual(
    EHero.Common,
    neutralToggleValue,
    "A toggle should return the post-toggle effective hero for persistence."
);
AssertEqual(
    EHero.Common,
    heroState.EffectiveHero,
    "Clearing the selected hero should restore the Common query context."
);
heroState.ToggleHero(EHero.Dooley);
AssertEqual(
    EHero.Dooley,
    heroState.SelectedHero,
    "Selecting a concrete hero from neutral mode should remain single-select."
);

var allHeroesSameHeroState = new CollectionFilterState();
allHeroesSameHeroState.ToggleHero(EHero.Vanessa);
allHeroesSameHeroState.ToggleAllHeroes();
allHeroesSameHeroState.ToggleHero(EHero.Vanessa);
AssertEqual(
    false,
    allHeroesSameHeroState.AllHeroesSelected,
    "Selecting the residual hero from all-heroes should exit all-heroes."
);
AssertEqual(
    EHero.Vanessa,
    allHeroesSameHeroState.SelectedHero,
    "Selecting the residual hero from all-heroes should preserve that hero as the scope."
);
AssertEqual(
    EHero.Vanessa,
    allHeroesSameHeroState.EffectiveHero,
    "Selecting the residual hero from all-heroes should preserve that effective hero."
);

var allHeroesDifferentHeroState = new CollectionFilterState();
allHeroesDifferentHeroState.ToggleHero(EHero.Vanessa);
allHeroesDifferentHeroState.ToggleAllHeroes();
allHeroesDifferentHeroState.ToggleHero(EHero.Dooley);
AssertEqual(
    false,
    allHeroesDifferentHeroState.AllHeroesSelected,
    "Selecting a different hero from all-heroes should exit all-heroes."
);
AssertEqual(
    EHero.Dooley,
    allHeroesDifferentHeroState.SelectedHero,
    "Selecting a different hero from all-heroes should use that hero as the scope."
);

var normalHeroToggleState = new CollectionFilterState();
normalHeroToggleState.ToggleHero(EHero.Vanessa);
normalHeroToggleState.ToggleHero(EHero.Vanessa);
AssertEqual(
    null,
    normalHeroToggleState.SelectedHero,
    "Selecting the active hero twice outside all-heroes should clear the hero scope."
);

var defaultSelection = CollectionPanelSelectionState.Default;
AssertEqual(
    EHero.Vanessa,
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
    CollectionPanelHeroPreference.Serialize(EHero.Dooley),
    "CollectionPanel hero preference should serialize enum names."
);
AssertTrue(
    CollectionPanelHeroPreference.TryParse("Dooley", out var parsedDooley)
        && parsedDooley == EHero.Dooley,
    "CollectionPanel hero preference should parse a supported concrete hero."
);
AssertTrue(
    CollectionPanelHeroPreference.TryParse("Common", out var parsedCommon)
        && parsedCommon == EHero.Common,
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
    EHero.Dooley,
    resolvedDooleyPreference.Hero,
    "Resolved preference state should retain its concrete hero."
);
AssertTrue(
    TheDragonsHeroIdentity.TryResolve(TheDragonsHeroIdentity.CanonicalId, out var dragonsHero),
    "The integrated identity adapter should resolve the canonical The Dragons preference."
);
var dragonsCatalogCards = new[]
{
    Card("Dragons Item", ETier.Bronze, heroes: new[] { dragonsHero }),
    Card("Dragons Skill", ETier.Bronze, type: ECardType.Skill, heroes: new[] { dragonsHero }),
};
AssertValues(
    CollectionHeroSelectionRoster.ResolveAvailableHeroes(
        CollectionCatalogReadiness.Loading,
        dragonsCatalogCards,
        _ =>
            throw new InvalidOperationException(
                "Loading catalogs must not probe transitional hero identity."
            )
    ),
    CollectionHeroSelectionRoster.BaseConcreteHeroes,
    "A loading catalog should expose only the seven established concrete heroes."
);
AssertValues(
    CollectionHeroSelectionRoster.ResolveAvailableHeroes(
        CollectionCatalogReadiness.Accepted,
        new[] { Card("Vanessa Item", ETier.Bronze, heroes: new[] { EHero.Vanessa }) }
    ),
    CollectionHeroSelectionRoster.BaseConcreteHeroes,
    "An accepted catalog without The Dragons content should not expose an empty hero chip."
);
AssertValues(
    CollectionHeroSelectionRoster.ResolveAvailableHeroes(
        CollectionCatalogReadiness.Accepted,
        new[]
        {
            Card("Dragons Package", ETier.Bronze, isPackage: true, heroes: new[] { dragonsHero }),
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
        EHero.Vanessa,
        EHero.Dooley,
        EHero.Pygmalien,
        EHero.Karnok,
        EHero.Mak,
        EHero.Stelle,
        EHero.Jules,
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
AssertValues(
    CollectionHeroSelectionRoster.ResolveAvailableHeroes(
        CollectionCatalogReadiness.Accepted,
        dragonsCatalogCards,
        name =>
            string.Equals(name, TheDragonsHeroIdentity.CanonicalId, StringComparison.Ordinal)
                ? dragonsHero
                : null
    ),
    acceptedDragonsRoster,
    "A future runtime exposing only the canonical enum name should retain The Dragons."
);
AssertValues(
    CollectionHeroSelectionRoster.ResolveAvailableHeroes(
        CollectionCatalogReadiness.Accepted,
        dragonsCatalogCards,
        name => string.Equals(name, "Hero8", StringComparison.Ordinal) ? dragonsHero : null
    ),
    acceptedDragonsRoster,
    "A legacy runtime exposing only the transitional enum name should retain The Dragons."
);
AssertValues(
    CollectionHeroSelectionRoster.ResolveAvailableHeroes(
        CollectionCatalogReadiness.Accepted,
        dragonsCatalogCards,
        _ => null
    ),
    CollectionHeroSelectionRoster.BaseConcreteHeroes,
    "A runtime that cannot resolve The Dragons should not expose an unusable hero chip."
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
    TheDragonsHeroIdentity.CanonicalId,
    resolvedDragonsPreference.CanonicalRaw,
    "A resolved legacy preference should migrate to the canonical The Dragons identity."
);
var dragonsIdentityFilter = new CollectionFilterState();
dragonsIdentityFilter.ToggleHero(dragonsHero);
var misleadingCommonItem = Card(
    "The Dragons Decoy",
    ETier.Bronze,
    heroes: new[] { EHero.Common },
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
dragonsIdentityFilter.ActiveType = ECardType.Skill;
var misleadingCommonSkill = Card(
    "Hero8 Skill Decoy",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Common },
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
    TheDragonsHeroIdentity.CanonicalId,
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
    EHero.Vanessa,
    selectionState.SelectedHero,
    "Applying the default selection should select Vanessa in the filter state."
);
AssertEqual(
    "merchant:jay-jay:global",
    selectionState.SelectedSourceKey,
    "Applying the default selection should select Jay Jay in the filter state."
);
AssertEqual(
    defaultSelection,
    selectionState.ToSelectionState(),
    "Filter state should round-trip the selected hero and merchant through the selection interface."
);
var legacyCommonSelection = new CollectionPanelSelectionState(
    EHero.Common,
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
    EHero.Common,
    selectionState.EffectiveHero,
    "Applying a legacy Common selection should retain Common as the effective query hero."
);
var runtimeSelection = new CollectionPanelSelectionState(
    EHero.Dooley,
    "merchant:jules:diamond:dooley+karnok+mak+pygmalien+stelle+vanessa",
    CollectionSourceKind.Merchant
);
selectionState.ApplySelection(runtimeSelection);
AssertEqual(
    EHero.Dooley,
    selectionState.SelectedHero,
    "Runtime selection should replace the previous selected hero."
);
AssertEqual(
    "merchant:jules:diamond:dooley+karnok+mak+pygmalien+stelle+vanessa",
    selectionState.SelectedSourceKey,
    "Runtime selection should replace the previous selected merchant."
);
AssertEqual(
    runtimeSelection,
    selectionState.ToSelectionState(),
    "Runtime selection should be readable back from the filter state."
);
var trainerSelection = new CollectionPanelSelectionState(
    EHero.Pygmalien,
    "trainer:mr-tuskari:pygmalien",
    CollectionSourceKind.Trainer
);
selectionState.SelectedSourceKey = "merchant:stale";
selectionState.ApplySelection(trainerSelection);
AssertEqual(
    ECardType.Skill,
    selectionState.ActiveType,
    "Applying a trainer runtime selection should route the panel to the Skill tab."
);
AssertEqual(
    "trainer:mr-tuskari:pygmalien",
    selectionState.SelectedSourceKey,
    "Applying a trainer runtime selection should store the trainer source key."
);
AssertEqual(
    trainerSelection,
    selectionState.ToSelectionState(),
    "Trainer source selection should round-trip through the selection interface."
);
selectionState.ApplySelection(runtimeSelection);
AssertEqual(
    ECardType.Item,
    selectionState.ActiveType,
    "Applying a merchant runtime selection should route the panel back to the Item tab."
);
AssertEqual(
    null,
    selectionState.GetSelectedSourceKey(ECardType.Skill),
    "A single selected source key should not read back as a stale source for the inactive tab."
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
    ECardType.Skill,
    sourceState.ActiveType,
    "Toggling a Skill source should set Skill active."
);
var topLevelModeState = new CollectionFilterState { ActiveType = ECardType.Skill };
AssertTrue(
    topLevelModeState.SelectActiveType(ECardType.Item),
    "Selecting Items from Skills should report a mode change."
);
AssertTrue(
    topLevelModeState.SelectActiveType(ECardType.Skill),
    "Selecting Skills from Items should report a mode change."
);
AssertEqual(
    ECardType.Skill,
    topLevelModeState.ActiveType,
    "Selecting Skills should activate the Skill card type."
);
AssertFalse(
    topLevelModeState.SelectActiveType(ECardType.Skill),
    "Selecting the already active Skills tab should be a no-op."
);

var itemDayPresentation = CollectionDayFilterPresentation.For(
    CollectionTabProfile.For(CollectionTabKind.Items),
    isSelected: true
);
AssertTrue(
    itemDayPresentation.IsVisible && itemDayPresentation.IsEnabled && itemDayPresentation.IsActive,
    "Normal item tabs should still show an enabled, highlighted day pill when selected."
);
var itemHeroPresentation = CollectionHeroFilterPresentation.For(
    CollectionTabProfile.For(CollectionTabKind.Items)
);
AssertTrue(
    itemHeroPresentation.IsVisible && itemHeroPresentation.IsEnabled,
    "Normal item tabs should still show an enabled hero row."
);

sourceState.ActiveType = ECardType.Item;
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
sourceState.ActiveType = ECardType.Skill;
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

var normal = Card("Normal", ETier.Bronze);
var package = Card("Starter Package", ETier.Silver, isPackage: true);
var bronzePackage = Card(
    "Bronze Package",
    ETier.Bronze,
    isPackage: true,
    heroes: new[] { EHero.Dooley },
    tags: new[] { ECardTag.Tool },
    hiddenTags: new[] { EHiddenTag.Package, EHiddenTag.Shield }
);

var defaultPackageResult = CollectionFilterEngine.Apply(
    new[] { package, normal },
    new CollectionFilterState()
);
AssertSequence(defaultPackageResult, new[] { normal.Id }, "Packages are excluded by default.");

var matchingPackageFilter = new CollectionFilterState();
matchingPackageFilter.ToggleHero(EHero.Dooley);
matchingPackageFilter.Tiers.Add(ETier.Bronze);
matchingPackageFilter.Sizes.Add(ECardSize.Medium);
matchingPackageFilter.Tags.Add(ECardTag.Tool);
matchingPackageFilter.Keywords.Add(EHiddenTag.Shield);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { bronzePackage, normal }, matchingPackageFilter),
    Array.Empty<Guid>(),
    "Package cards stay hidden from CollectionPanel even when their facets match."
);

var bronzeLarge = Card("A Bronze Large", ETier.Bronze, size: ECardSize.Large);
var bronzeSmall = Card("B Bronze Small", ETier.Bronze, size: ECardSize.Small);
var silverLarge = Card("C Silver Large", ETier.Silver, size: ECardSize.Large);
var silverSmall = Card("D Silver Small", ETier.Silver, size: ECardSize.Small);
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

var offerPoolItem = Card("Offer Pool Item", ETier.Bronze);
var offerPoolExcluded = Card("Offer Pool Excluded", ETier.Bronze);

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

var vanessaBronze = Card("Vanessa Bronze", ETier.Bronze, heroes: new[] { EHero.Vanessa });
var dooleyBronze = Card("Dooley Bronze", ETier.Bronze, heroes: new[] { EHero.Dooley });
var vanessaSilver = Card("Vanessa Silver", ETier.Silver, heroes: new[] { EHero.Vanessa });
var sourceAndHeroFilter = new CollectionFilterState();
sourceAndHeroFilter.ToggleHero(EHero.Vanessa);
sourceAndHeroFilter.Tiers.Add(ETier.Bronze);
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
sourceOwnedHeroFilter.ToggleHero(EHero.Vanessa);
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
    ETier.Bronze,
    type: ECardType.Skill,
    tags: new[] { ECardTag.Weapon }
);
var potionSkill = Card(
    "Potion Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    tags: new[] { ECardTag.Potion }
);
var skillTagFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
skillTagFilter.Tags.Add(ECardTag.Weapon);
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
    ETier.Bronze,
    type: ECardType.Skill,
    hiddenTags: new[] { EHiddenTag.Damage }
);
var shieldSkill = Card(
    "Shield Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    hiddenTags: new[] { EHiddenTag.Shield }
);
var damageShieldSkill = Card(
    "Damage Shield Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    hiddenTags: new[] { EHiddenTag.Damage, EHiddenTag.Shield }
);
var skillKeywordFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
skillKeywordFilter.Keywords.Add(EHiddenTag.Damage);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { shieldSkill, damageSkill, normal }, skillKeywordFilter),
    new[] { damageSkill.Id },
    "Skill keyword filters narrow skills by EHiddenTag."
);
skillKeywordFilter.Keywords.Add(EHiddenTag.Shield);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { shieldSkill, damageSkill, normal }, skillKeywordFilter),
    new[] { damageSkill.Id, shieldSkill.Id },
    "Multiple selected skill keywords OR together."
);
var allSkillKeywordFilter = new CollectionFilterState
{
    ActiveType = ECardType.Skill,
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
allSkillKeywordFilter.Keywords.Add(EHiddenTag.Damage);
allSkillKeywordFilter.Keywords.Add(EHiddenTag.Shield);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldSkill, damageSkill, damageShieldSkill, normal },
        allSkillKeywordFilter
    ),
    new[] { damageShieldSkill.Id },
    "All keyword mode requires every selected skill keyword on the same card."
);

var weaponItem = Card("Weapon Item", ETier.Bronze, tags: new[] { ECardTag.Weapon });
var potionItem = Card("Potion Item", ETier.Bronze, tags: new[] { ECardTag.Potion });
var toolItem = Card("Tool Item", ETier.Bronze, tags: new[] { ECardTag.Tool });
var weaponPotionItem = Card(
    "Weapon Potion Item",
    ETier.Bronze,
    tags: new[] { ECardTag.Weapon, ECardTag.Potion }
);
var itemTagFilter = new CollectionFilterState();
itemTagFilter.Tags.Add(ECardTag.Weapon);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { potionItem, toolItem, weaponItem }, itemTagFilter),
    new[] { weaponItem.Id },
    "A single selected tag narrows items to cards carrying that tag."
);
itemTagFilter.Tags.Add(ECardTag.Potion);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { potionItem, toolItem, weaponItem }, itemTagFilter),
    new[] { potionItem.Id, weaponItem.Id },
    "Multiple selected tags OR together, matching the other facet rows."
);
var allItemTagFilter = new CollectionFilterState { TagMatchMode = CollectionFacetMatchMode.All };
allItemTagFilter.Tags.Add(ECardTag.Weapon);
allItemTagFilter.Tags.Add(ECardTag.Potion);
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
    ETier.Bronze,
    hiddenTags: new[] { EHiddenTag.PotionReference }
);
var potionReferenceFilter = new CollectionFilterState();
potionReferenceFilter.Keywords.Add(EHiddenTag.PotionReference);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { potionItem, potionReferenceItem }, potionReferenceFilter),
    new[] { potionReferenceItem.Id },
    "PotionReference should behave as its own hidden-tag keyword, separate from the Potion item tag."
);

var potionTagOnlyFilter = new CollectionFilterState();
potionTagOnlyFilter.Tags.Add(ECardTag.Potion);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { potionItem, potionReferenceItem }, potionTagOnlyFilter),
    new[] { potionItem.Id },
    "Potion tag filtering should not match PotionReference-only cards."
);

var damageItem = Card(
    "Damage Item",
    ETier.Bronze,
    tags: new[] { ECardTag.Weapon },
    hiddenTags: new[] { EHiddenTag.Damage }
);
var shieldItem = Card(
    "Shield Item",
    ETier.Bronze,
    tags: new[] { ECardTag.Weapon },
    hiddenTags: new[] { EHiddenTag.Shield }
);
var damageShieldItem = Card(
    "Damage Shield Item",
    ETier.Bronze,
    tags: new[] { ECardTag.Weapon },
    hiddenTags: new[] { EHiddenTag.Damage, EHiddenTag.Shield }
);
var damageToolItem = Card(
    "Damage Tool Item",
    ETier.Bronze,
    tags: new[] { ECardTag.Tool },
    hiddenTags: new[] { EHiddenTag.Damage }
);
var damageReferenceItem = Card(
    "Damage Reference Item",
    ETier.Bronze,
    hiddenTags: new[] { EHiddenTag.DamageReference }
);
var multicastItem = Card("Multicast Item", ETier.Bronze, mechanics: CollectionMechanic.Multicast);
var damageMulticastItem = Card(
    "Damage Multicast Item",
    ETier.Bronze,
    hiddenTags: new[] { EHiddenTag.Damage },
    mechanics: CollectionMechanic.Multicast
);
var itemKeywordFilter = new CollectionFilterState();
itemKeywordFilter.Keywords.Add(EHiddenTag.Damage);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldItem, damageToolItem, damageItem },
        itemKeywordFilter
    ),
    new[] { damageItem.Id, damageToolItem.Id },
    "Item keyword filters narrow items by EHiddenTag."
);
itemKeywordFilter.Keywords.Add(EHiddenTag.Shield);
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
allItemKeywordFilter.Keywords.Add(EHiddenTag.Damage);
allItemKeywordFilter.Keywords.Add(EHiddenTag.Shield);
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
anyMulticastKeywordFilter.Keywords.Add(EHiddenTag.Damage);
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
allMulticastKeywordFilter.Keywords.Add(EHiddenTag.Damage);
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
itemKeywordAndReferenceFilter.Keywords.Add(EHiddenTag.Damage);
itemKeywordAndReferenceFilter.Keywords.Add(EHiddenTag.DamageReference);
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
    ETier.Bronze,
    hiddenTags: new[] { EHiddenTag.Damage, EHiddenTag.DamageReference }
);
var allItemKeywordAndReferenceFilter = new CollectionFilterState
{
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
allItemKeywordAndReferenceFilter.Keywords.Add(EHiddenTag.Damage);
allItemKeywordAndReferenceFilter.Keywords.Add(EHiddenTag.DamageReference);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { damageReferenceItem, damageItem, damageAndReferenceItem },
        allItemKeywordAndReferenceFilter
    ),
    new[] { damageAndReferenceItem.Id },
    "All keyword mode also requires curated reference keywords on the same card."
);
var itemTagAndKeywordFilter = new CollectionFilterState();
itemTagAndKeywordFilter.Tags.Add(ECardTag.Weapon);
itemTagAndKeywordFilter.Keywords.Add(EHiddenTag.Damage);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { shieldItem, damageToolItem, damageItem },
        itemTagAndKeywordFilter
    ),
    new[] { damageItem.Id },
    "Item tags and item keywords combine as separate AND facets."
);

var instrumentTempoItem = InstrumentFacetCard();
var instrumentTempoWrongHero = InstrumentFacetCard(hero: EHero.Dooley);
var instrumentTempoWrongTier = InstrumentFacetCard(tier: ETier.Silver);
var instrumentTempoWrongSize = InstrumentFacetCard(size: ECardSize.Large);
var instrumentTempoWrongTag = InstrumentFacetCard(tag: ECardTag.Weapon);
var instrumentTempoWrongKeyword = InstrumentFacetCard(keyword: EHiddenTag.TempoReference);
var instrumentTempoWrongSearch = InstrumentFacetCard(name: "Orchestral Practice");
var instrumentTempoNotOffered = InstrumentFacetCard(name: "Tempo Instrument Solo Encore");
var instrumentTempoItemFilter = new CollectionFilterState { SearchQuery = "instrument solo" };
instrumentTempoItemFilter.ToggleHero(EHero.Vanessa);
instrumentTempoItemFilter.Tiers.Add(ETier.Bronze);
instrumentTempoItemFilter.Sizes.Add(ECardSize.Medium);
instrumentTempoItemFilter.Tags.Add(ECardTag.Instrument);
instrumentTempoItemFilter.Keywords.Add(EHiddenTag.Tempo);
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
    type: ECardType.Skill,
    keyword: EHiddenTag.TempoReference
);
var instrumentTempoReferenceWrongTagSkill = InstrumentFacetCard(
    name: "Tempo Reference Instrument Skill",
    type: ECardType.Skill,
    tag: ECardTag.Weapon,
    keyword: EHiddenTag.TempoReference
);
var instrumentTempoReferenceWrongKeywordSkill = InstrumentFacetCard(
    name: "Tempo Reference Instrument Skill",
    type: ECardType.Skill
);
var instrumentTempoReferenceWrongHeroSkill = InstrumentFacetCard(
    name: "Tempo Reference Instrument Skill",
    type: ECardType.Skill,
    keyword: EHiddenTag.TempoReference,
    hero: EHero.Dooley
);
var instrumentTempoReferenceSkillFilter = new CollectionFilterState
{
    ActiveType = ECardType.Skill,
    SearchQuery = "reference instrument",
};
instrumentTempoReferenceSkillFilter.ToggleHero(EHero.Vanessa);
instrumentTempoReferenceSkillFilter.Tiers.Add(ETier.Bronze);
instrumentTempoReferenceSkillFilter.Tags.Add(ECardTag.Instrument);
instrumentTempoReferenceSkillFilter.Keywords.Add(EHiddenTag.TempoReference);
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

var derivedLifestealTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Size = ECardSize.Medium,
    InternalName = "Derived Lifesteal Weapon",
    ArtKey = "Assets/Cards/DerivedLifestealWeapon.png",
    Heroes = new HashSet<EHero> { EHero.Common },
    Tags = new HashSet<ECardTag> { ECardTag.Weapon },
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Lifesteal] = 100,
            },
        },
    },
};
var derivedLifestealVm = CollectionCardVm.From(derivedLifestealTemplate);
AssertTrue(
    derivedLifestealVm.HiddenTags.Contains(EHiddenTag.Lifesteal),
    "CollectionCardVm.From should derive Lifesteal from positive item attributes."
);
AssertFalse(
    derivedLifestealTemplate.HiddenTags.Contains(EHiddenTag.Lifesteal),
    "Derived Lifesteal projection should not mutate the source game template HiddenTags."
);
AssertValues(
    CollectionFacetAvailability
        .SnapshotFor(new[] { derivedLifestealVm })
        .ItemKeywords.Select(tag => tag.ToString())
        .ToArray(),
    new[] { nameof(EHiddenTag.Lifesteal) },
    "Available item keywords should include Lifesteal derived from item attributes."
);
var questTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Size = ECardSize.Medium,
    InternalName = "Quest Item Without Quest HiddenTag",
    ArtKey = "Assets/Cards/QuestItemWithoutQuestHiddenTag.png",
    Heroes = new HashSet<EHero> { EHero.Common },
    HiddenTags = new HashSet<EHiddenTag> { EHiddenTag.Charge },
    Quests = new List<TQuestGroup> { new() { Entries = new List<TQuestEntry> { new() } } },
};
var questVm = CollectionCardVm.From(questTemplate);
AssertTrue(
    questVm.HiddenTags.Contains(EHiddenTag.Quest),
    "CollectionCardVm.From should derive Quest from a non-empty item Quests graph."
);
AssertTrue(
    questVm.HiddenTags.Contains(EHiddenTag.Charge),
    "Quest projection should preserve the item's native hidden tags."
);
AssertFalse(
    questTemplate.HiddenTags.Contains(EHiddenTag.Quest),
    "Quest projection should not mutate the source game template HiddenTags."
);
var questFilter = new CollectionFilterState();
questFilter.Keywords.Add(EHiddenTag.Quest);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { questVm, derivedLifestealVm }, questFilter),
    new[] { questVm.Id },
    "Quest keyword filtering should include items whose Quests graph lacks a native Quest hidden tag."
);
var noLifestealTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Size = ECardSize.Medium,
    InternalName = "No Lifesteal Weapon",
    ArtKey = "Assets/Cards/NoLifestealWeapon.png",
    Heroes = new HashSet<EHero> { EHero.Common },
    Tags = new HashSet<ECardTag> { ECardTag.Weapon },
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Lifesteal] = 0,
            },
        },
    },
};
var noLifestealVm = CollectionCardVm.From(noLifestealTemplate);
var lifestealFilter = new CollectionFilterState();
lifestealFilter.Keywords.Add(EHiddenTag.Lifesteal);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { noLifestealVm, derivedLifestealVm }, lifestealFilter),
    new[] { derivedLifestealVm.Id },
    "Lifesteal keyword filtering should include derived Lifesteal VMs and exclude non-positive attributes."
);
var allLifestealFilter = new CollectionFilterState
{
    KeywordMatchMode = CollectionFacetMatchMode.All,
};
allLifestealFilter.Keywords.Add(EHiddenTag.Lifesteal);
AssertSequence(
    CollectionFilterEngine.Apply(new[] { noLifestealVm, derivedLifestealVm }, allLifestealFilter),
    new[] { derivedLifestealVm.Id },
    "All keyword mode should still match a VM with derived Lifesteal when Lifesteal is the only selected keyword."
);
var attributeOnlyMulticastTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    StartingTier = ETier.Bronze,
    Size = ECardSize.Medium,
    InternalName = "Attribute Only Multicast Item",
    ArtKey = "Assets/Cards/AttributeOnlyMulticastItem.png",
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = new TCardTier
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Multicast] = 3,
            },
        },
    },
};
var attributeOnlyMulticastVm = CollectionCardVm.From(attributeOnlyMulticastTemplate);
AssertFalse(
    attributeOnlyMulticastVm.HiddenTags.Contains(EHiddenTag.Multicast),
    "Structured Multicast projection should not mutate the native hidden-keyword facts."
);
AssertTrue(
    attributeOnlyMulticastVm.Mechanics.Has(CollectionMechanic.Multicast),
    "A supported tier with base Multicast greater than one should project the Multicast mechanic."
);

var baseOneMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(attributes: new() { [ECardAttributeType.Multicast] = 1 }),
    }
);
AssertFalse(
    CollectionCardVm.From(baseOneMulticastTemplate).Mechanics.Has(CollectionMechanic.Multicast),
    "Base Multicast one without an active modifier should not project Multicast."
);

var crossTierMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(attributes: new() { [ECardAttributeType.Multicast] = 1 }),
        [ETier.Silver] = Tier(attributes: new() { [ECardAttributeType.Multicast] = 3 }),
    }
);
AssertTrue(
    CollectionCardVm.From(crossTierMulticastTemplate).Mechanics.Has(CollectionMechanic.Multicast),
    "Multicast projection should inspect every supported tier and observe later-tier changes."
);

var activeAbilityMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "active-multicast" }),
    },
    abilities: new() { ["active-multicast"] = Ability(MulticastModifier()) }
);
AssertTrue(
    CollectionCardVm
        .From(activeAbilityMulticastTemplate)
        .Mechanics.Has(CollectionMechanic.Multicast),
    "An active base ability modifier should project Multicast."
);

var nestedAbilityMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "nested-multicast" }),
    },
    abilities: new()
    {
        ["nested-multicast"] = Ability(
            new TActionAnd
            {
                Actions = new List<ITAction>
                {
                    new TActionAnd { Actions = new List<ITAction> { MulticastModifier() } },
                },
            }
        ),
    }
);
AssertTrue(
    CollectionCardVm
        .From(nestedAbilityMulticastTemplate)
        .Mechanics.Has(CollectionMechanic.Multicast),
    "A Multicast modifier nested in combined actions should project Multicast."
);

var activeAuraMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(auraIds: new[] { "active-multicast-aura" }),
    },
    auras: new() { ["active-multicast-aura"] = Aura(MulticastAuraModifier()) }
);
AssertTrue(
    CollectionCardVm.From(activeAuraMulticastTemplate).Mechanics.Has(CollectionMechanic.Multicast),
    "An active base aura modifier should project Multicast."
);

var orphanMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    abilities: new() { ["orphan-multicast"] = Ability(MulticastModifier()) },
    auras: new() { ["orphan-multicast-aura"] = Aura(MulticastAuraModifier()) }
);
AssertFalse(
    CollectionCardVm.From(orphanMulticastTemplate).Mechanics.Has(CollectionMechanic.Multicast),
    "Unreferenced base abilities and auras should not project Multicast."
);

var enchantmentMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    enchantments: new()
    {
        [EEnchantmentType.Shiny] = new TEnchantment
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.Multicast] = 2,
            },
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["enchanted-multicast"] = Ability(MulticastModifier()),
            },
            Auras = new Dictionary<string, TCardAura>
            {
                ["enchanted-multicast-aura"] = Aura(MulticastAuraModifier()),
            },
        },
    }
);
AssertFalse(
    CollectionCardVm.From(enchantmentMulticastTemplate).Mechanics.Has(CollectionMechanic.Multicast),
    "Enchantment attributes, abilities, and auras should not pollute base Multicast facts."
);

var nativeAndDerivedMulticastTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(attributes: new() { [ECardAttributeType.Multicast] = 2 }),
    },
    hiddenTags: new() { EHiddenTag.Multicast }
);
var nativeAndDerivedMulticastVm = CollectionCardVm.From(nativeAndDerivedMulticastTemplate);
var nativeOnlyMulticastVm = CollectionCardVm.From(
    MechanicItemTemplate(
        new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
        hiddenTags: new() { EHiddenTag.Multicast }
    )
);
AssertTrue(
    nativeOnlyMulticastVm.Mechanics.Has(CollectionMechanic.Multicast),
    "A native Multicast hidden tag should map into the same structured mechanic fact."
);
var duplicateMulticastAvailability = CollectionFacetAvailability.SnapshotFor(
    new[] { nativeAndDerivedMulticastVm }
);
AssertFalse(
    duplicateMulticastAvailability.ItemKeywords.Contains(EHiddenTag.Multicast),
    "Native Multicast hidden tags should be normalized out of the native keyword availability."
);
AssertValues(
    duplicateMulticastAvailability.ItemMechanics.ToArray(),
    new[] { CollectionMechanic.Multicast },
    "Native and derived Multicast facts should collapse into one available mechanic."
);
AssertEqual(
    1,
    duplicateMulticastAvailability
        .KeywordOptionsFor(ECardType.Item)
        .Count(option => option.Mechanic == CollectionMechanic.Multicast),
    "Native and derived Multicast facts should render one Multicast chip."
);

var multicastSkillTemplate = new TCardSkill
{
    Id = Guid.NewGuid(),
    Type = ECardType.Skill,
    StartingTier = ETier.Bronze,
    InternalName = "Multicast Skill",
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "skill-multicast" }),
    },
    Abilities = new Dictionary<string, TCardAbility>
    {
        ["skill-multicast"] = Ability(MulticastModifier()),
    },
};
var multicastSkillVm = CollectionCardVm.From(multicastSkillTemplate);
var independentMulticastAvailability = CollectionFacetAvailability.SnapshotFor(
    new[] { attributeOnlyMulticastVm, multicastSkillVm }
);
AssertTrue(
    independentMulticastAvailability
        .MechanicsFor(ECardType.Item)
        .Contains(CollectionMechanic.Multicast),
    "Item Multicast availability should be computed from item catalog facts."
);
AssertTrue(
    independentMulticastAvailability
        .MechanicsFor(ECardType.Skill)
        .Contains(CollectionMechanic.Multicast),
    "Skill Multicast availability should be computed independently from skill catalog facts."
);

var primaryMulticastOrder = CollectionFacetAvailability.SnapshotFor(
    new[]
    {
        Card(
            "Damage Multicast Related",
            ETier.Bronze,
            hiddenTags: new[] { EHiddenTag.Damage, EHiddenTag.DamageReference },
            mechanics: CollectionMechanic.Multicast
        ),
    }
);
AssertValues(
    primaryMulticastOrder
        .KeywordOptionsFor(ECardType.Item)
        .Select(option => option.ToString())
        .ToArray(),
    new[]
    {
        nameof(EHiddenTag.Damage),
        nameof(CollectionMechanic.Multicast),
        nameof(EHiddenTag.DamageReference),
    },
    "Multicast should sort in the primary keyword area before the Related subsection."
);
AssertFalse(
    CollectionKeywordWhitelist.IsRelatedKeyword(EHiddenTag.Multicast),
    "The legacy Multicast hidden tag should no longer be classified as Related."
);

var rootDestroyTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "root-destroy" }),
    },
    abilities: new() { ["root-destroy"] = Ability(new TActionCardDestroy()) }
);
var rootDestroyVm = CollectionCardVm.From(rootDestroyTemplate);
AssertTrue(
    rootDestroyVm.Mechanics.Has(CollectionMechanic.Destroy),
    "A root TActionCardDestroy in an active base ability should project Destroy."
);

var nestedDestroyTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "nested-destroy" }),
    },
    abilities: new()
    {
        ["nested-destroy"] = Ability(
            new TActionAnd
            {
                Actions = new List<ITAction>
                {
                    new TActionAnd { Actions = new List<ITAction> { new TActionCardDestroy() } },
                },
            }
        ),
    }
);
var nestedDestroyVm = CollectionCardVm.From(nestedDestroyTemplate);
AssertTrue(
    nestedDestroyVm.Mechanics.Has(CollectionMechanic.Destroy),
    "A TActionCardDestroy nested in combined actions should project Destroy."
);

var orphanDestroyTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    abilities: new() { ["orphan-destroy"] = Ability(new TActionCardDestroy()) }
);
AssertFalse(
    CollectionCardVm.From(orphanDestroyTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "An unreferenced base destroy ability should not project Destroy."
);

var enchantmentDestroyTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    enchantments: new()
    {
        [EEnchantmentType.Shiny] = new TEnchantment
        {
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["enchanted-destroy"] = Ability(new TActionCardDestroy()),
            },
        },
    }
);
AssertFalse(
    CollectionCardVm.From(enchantmentDestroyTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "Enchantment-provided destroy abilities should not pollute base Destroy facts."
);

var transformDestroyedTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "transform-destroyed" }),
    },
    abilities: new()
    {
        ["transform-destroyed"] = Ability(
            new TActionCardTransformDestroyed
            {
                Abilities = new Dictionary<string, TCardAbility>
                {
                    ["derived-destroy"] = Ability(new TActionCardDestroy()),
                },
            }
        ),
    }
);
AssertTrue(
    CollectionCardVm.From(transformDestroyedTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "TActionCardTransformDestroyed on an active base ability should project Destroy; nested spawn-template abilities remain unwalked."
);

// Destruction-reaction triggers (#156): each of the three destroy-cluster triggers matches
// Destroy on its own, independent of the ability's action. Non-destroy actions below keep the
// assertion focused on the trigger surface.
var beforeDestroyedTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "before-destroyed" }),
    },
    abilities: new()
    {
        ["before-destroyed"] = Ability(
            new TActionCardModifyAttribute { AttributeType = ECardAttributeType.DamageAmount },
            new TTriggerOnBeforeCardDestroyed()
        ),
    }
);
AssertTrue(
    CollectionCardVm.From(beforeDestroyedTriggerTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "An active base ability triggered by TTriggerOnBeforeCardDestroyed should project Destroy."
);

var cardDestroyedTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "card-destroyed" }),
    },
    abilities: new()
    {
        ["card-destroyed"] = Ability(
            new TActionCardModifyAttribute { AttributeType = ECardAttributeType.DamageAmount },
            new TTriggerOnCardDestroyed()
        ),
    }
);
AssertTrue(
    CollectionCardVm.From(cardDestroyedTriggerTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "An active base ability triggered by TTriggerOnCardDestroyed should project Destroy."
);

var performedDestructionTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "performed-destruction" }),
    },
    abilities: new()
    {
        ["performed-destruction"] = Ability(
            new TActionCardModifyAttribute { AttributeType = ECardAttributeType.DamageAmount },
            new TTriggerOnCardPerformedDestruction()
        ),
    }
);
AssertTrue(
    CollectionCardVm
        .From(performedDestructionTriggerTemplate)
        .Mechanics.Has(CollectionMechanic.Destroy),
    "An active base ability triggered by TTriggerOnCardPerformedDestruction should project Destroy."
);

var orNestedDestructionTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "or-destroyed" }),
    },
    abilities: new()
    {
        ["or-destroyed"] = Ability(
            new TActionCardModifyAttribute { AttributeType = ECardAttributeType.DamageAmount },
            new TTriggerOr
            {
                Triggers = new List<TTriggerBase>
                {
                    new TTriggerOnCardFired(),
                    new TTriggerOnCardDestroyed(),
                },
            }
        ),
    }
);
AssertTrue(
    CollectionCardVm
        .From(orNestedDestructionTriggerTemplate)
        .Mechanics.Has(CollectionMechanic.Destroy),
    "A destruction trigger nested inside TTriggerOr should project Destroy even when other branches are unrelated."
);

var orphanDestructionTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    abilities: new()
    {
        ["orphan-destroyed"] = Ability(
            new TActionCardModifyAttribute { AttributeType = ECardAttributeType.DamageAmount },
            new TTriggerOnCardDestroyed()
        ),
    }
);
AssertFalse(
    CollectionCardVm
        .From(orphanDestructionTriggerTemplate)
        .Mechanics.Has(CollectionMechanic.Destroy),
    "An unreferenced base destruction-reaction ability should not project Destroy."
);

var enchantmentDestructionTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    enchantments: new()
    {
        [EEnchantmentType.Shiny] = new TEnchantment
        {
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["enchanted-destroyed"] = Ability(
                    new TActionCardModifyAttribute
                    {
                        AttributeType = ECardAttributeType.DamageAmount,
                    },
                    new TTriggerOnCardDestroyed()
                ),
            },
        },
    }
);
AssertFalse(
    CollectionCardVm
        .From(enchantmentDestructionTriggerTemplate)
        .Mechanics.Has(CollectionMechanic.Destroy),
    "Enchantment-provided destruction-reaction abilities should not pollute base Destroy facts."
);

var repairedTriggerTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier(abilityIds: new[] { "on-repaired" }) },
    abilities: new()
    {
        ["on-repaired"] = Ability(
            new TActionCardModifyAttribute { AttributeType = ECardAttributeType.DamageAmount },
            new TTriggerOnCardRepaired()
        ),
    }
);
AssertFalse(
    CollectionCardVm.From(repairedTriggerTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "TTriggerOnCardRepaired should not project Destroy; only destruction-reaction triggers are in this slice."
);

// Remaining destroy-cluster action / tag / attribute surfaces (#157).
var rootRepairTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier(abilityIds: new[] { "root-repair" }) },
    abilities: new() { ["root-repair"] = Ability(new TActionCardRepair()) }
);
AssertTrue(
    CollectionCardVm.From(rootRepairTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "A root TActionCardRepair in an active base ability should project Destroy."
);

var nestedRepairTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "nested-repair" }),
    },
    abilities: new()
    {
        ["nested-repair"] = Ability(
            new TActionAnd
            {
                Actions = new List<ITAction>
                {
                    new TActionAnd { Actions = new List<ITAction> { new TActionCardRepair() } },
                },
            }
        ),
    }
);
AssertTrue(
    CollectionCardVm.From(nestedRepairTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "A TActionCardRepair nested in combined actions should project Destroy."
);

var absorbDestroyTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    hiddenTags: new() { EHiddenTag.AbsorbDestroy }
);
AssertTrue(
    CollectionCardVm.From(absorbDestroyTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "The native AbsorbDestroy hidden tag should project Destroy."
);
AssertFalse(
    CollectionKeywordWhitelist.Ordered.Contains(EHiddenTag.AbsorbDestroy),
    "AbsorbDestroy must not appear as its own keyword chip; it surfaces only via Destroy."
);

var destroyImmunityTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(attributes: new() { [ECardAttributeType.DestroyImmunity] = 1 }),
    }
);
AssertTrue(
    CollectionCardVm.From(destroyImmunityTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "A positive base DestroyImmunity value at any supported tier should project Destroy."
);

var zeroDestroyImmunityTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(attributes: new() { [ECardAttributeType.DestroyImmunity] = 0 }),
    }
);
AssertFalse(
    CollectionCardVm.From(zeroDestroyImmunityTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "A zero base DestroyImmunity value should not project Destroy."
);

var enchantmentRepairTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    enchantments: new()
    {
        [EEnchantmentType.Shiny] = new TEnchantment
        {
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["enchanted-repair"] = Ability(new TActionCardRepair()),
            },
        },
    }
);
AssertFalse(
    CollectionCardVm.From(enchantmentRepairTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "Enchantment-provided repair abilities should not pollute base Destroy facts."
);

var enchantmentReplaceDestroyedTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    enchantments: new()
    {
        [EEnchantmentType.Shiny] = new TEnchantment
        {
            Abilities = new Dictionary<string, TCardAbility>
            {
                ["enchanted-transform-destroyed"] = Ability(new TActionCardTransformDestroyed()),
            },
        },
    }
);
AssertFalse(
    CollectionCardVm
        .From(enchantmentReplaceDestroyedTemplate)
        .Mechanics.Has(CollectionMechanic.Destroy),
    "Enchantment-provided replace-destroyed abilities should not pollute base Destroy facts."
);

var radiantDestroyImmunityTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() },
    enchantments: new()
    {
        [EEnchantmentType.Radiant] = new TEnchantment
        {
            Attributes = new Dictionary<ECardAttributeType, int>
            {
                [ECardAttributeType.DestroyImmunity] = 1,
            },
        },
    }
);
AssertFalse(
    CollectionCardVm.From(radiantDestroyImmunityTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "Radiant destroy immunity on an enchantment must not project base Destroy facts."
);

var tooltipOnlyDestroyTemplate = MechanicItemTemplate(
    new Dictionary<ETier, TCardTier> { [ETier.Bronze] = Tier() }
) with
{
    InternalName = "Destroy Tooltip Card",
    InternalDescription = "Destroy another item when this tooltip is rendered.",
};
AssertFalse(
    CollectionCardVm.From(tooltipOnlyDestroyTemplate).Mechanics.Has(CollectionMechanic.Destroy),
    "Destroy-related name or description text alone should not project Destroy."
);

var destroySkillTemplate = new TCardSkill
{
    Id = Guid.NewGuid(),
    Type = ECardType.Skill,
    StartingTier = ETier.Bronze,
    InternalName = "Destroy Skill",
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "skill-destroy" }),
    },
    Abilities = new Dictionary<string, TCardAbility>
    {
        ["skill-destroy"] = Ability(new TActionCardDestroy()),
    },
};
var destroySkillVm = CollectionCardVm.From(destroySkillTemplate);
var itemDestroyAvailability = CollectionFacetAvailability.SnapshotFor(new[] { rootDestroyVm });
AssertTrue(
    itemDestroyAvailability.MechanicsFor(ECardType.Item).Contains(CollectionMechanic.Destroy),
    "Item Destroy availability should be derived from item catalog facts."
);
AssertFalse(
    itemDestroyAvailability.MechanicsFor(ECardType.Skill).Contains(CollectionMechanic.Destroy),
    "Item Destroy facts should not leak into Skill availability."
);
var skillDestroyAvailability = CollectionFacetAvailability.SnapshotFor(new[] { destroySkillVm });
AssertTrue(
    skillDestroyAvailability.MechanicsFor(ECardType.Skill).Contains(CollectionMechanic.Destroy),
    "Skill Destroy availability should be derived independently from skill catalog facts."
);
AssertFalse(
    skillDestroyAvailability.MechanicsFor(ECardType.Item).Contains(CollectionMechanic.Destroy),
    "Skill Destroy facts should not leak into Item availability."
);

// #158: related-only Destroy facts (no TActionCardDestroy) still make the chip available.
// Item/Skill stay independent; packages never contribute.
var relatedOnlyRepairVm = CollectionCardVm.From(rootRepairTemplate);
var relatedOnlyItemAvailability = CollectionFacetAvailability.SnapshotFor(
    new[] { relatedOnlyRepairVm }
);
AssertTrue(
    relatedOnlyItemAvailability.MechanicsFor(ECardType.Item).Contains(CollectionMechanic.Destroy),
    "A card that matches Destroy only through a related mechanism should make the Destroy chip available on its tab."
);
AssertFalse(
    relatedOnlyItemAvailability.MechanicsFor(ECardType.Skill).Contains(CollectionMechanic.Destroy),
    "Related-only Item Destroy facts should not leak into Skill availability."
);

var relatedOnlySkillTemplate = new TCardSkill
{
    Id = Guid.NewGuid(),
    Type = ECardType.Skill,
    StartingTier = ETier.Bronze,
    InternalName = "Repair Skill",
    HiddenTags = new HashSet<EHiddenTag>(),
    Tiers = new Dictionary<ETier, TCardTier>
    {
        [ETier.Bronze] = Tier(abilityIds: new[] { "skill-repair" }),
    },
    Abilities = new Dictionary<string, TCardAbility>
    {
        ["skill-repair"] = Ability(new TActionCardRepair()),
    },
};
var relatedOnlySkillVm = CollectionCardVm.From(relatedOnlySkillTemplate);
var relatedOnlySkillAvailability = CollectionFacetAvailability.SnapshotFor(
    new[] { relatedOnlySkillVm }
);
AssertTrue(
    relatedOnlySkillAvailability.MechanicsFor(ECardType.Skill).Contains(CollectionMechanic.Destroy),
    "Skill related-only Destroy availability should be derived independently from skill catalog facts."
);
AssertFalse(
    relatedOnlySkillAvailability.MechanicsFor(ECardType.Item).Contains(CollectionMechanic.Destroy),
    "Related-only Skill Destroy facts should not leak into Item availability."
);

var packageDestroyOnlyCard = Card(
    "Package Destroy Related",
    ETier.Bronze,
    isPackage: true,
    mechanics: CollectionMechanic.Destroy
);
var packageDestroyAvailability = CollectionFacetAvailability.SnapshotFor(
    new[] { packageDestroyOnlyCard }
);
AssertFalse(
    packageDestroyAvailability.MechanicsFor(ECardType.Item).Contains(CollectionMechanic.Destroy),
    "Package cards must never contribute Destroy chip availability."
);
AssertFalse(
    packageDestroyAvailability.MechanicsFor(ECardType.Skill).Contains(CollectionMechanic.Destroy),
    "Package cards must never contribute Destroy chip availability on either tab."
);

var multicastOnlyMechanicCard = Card(
    "Alpha Multicast",
    ETier.Bronze,
    mechanics: CollectionMechanic.Multicast
);
var destroyOnlyMechanicCard = Card(
    "Beta Destroy",
    ETier.Bronze,
    mechanics: CollectionMechanic.Destroy
);
var multicastDestroyCard = Card(
    "Gamma Multicast Destroy",
    ETier.Bronze,
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
    ETier.Bronze,
    hiddenTags: new[] { EHiddenTag.Damage },
    mechanics: CollectionMechanic.Destroy
);
var destroyDamageAny = new CollectionFilterState();
destroyDamageAny.Keywords.Add(EHiddenTag.Damage);
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
destroyDamageAll.Keywords.Add(EHiddenTag.Damage);
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
            ETier.Bronze,
            hiddenTags: new[] { EHiddenTag.Damage, EHiddenTag.DamageReference },
            mechanics: CollectionMechanic.Multicast | CollectionMechanic.Destroy
        ),
    }
);
AssertValues(
    primaryDestroyOrder
        .KeywordOptionsFor(ECardType.Item)
        .Select(option => option.ToString())
        .ToArray(),
    new[]
    {
        nameof(EHiddenTag.Damage),
        nameof(CollectionMechanic.Multicast),
        nameof(CollectionMechanic.Destroy),
        nameof(EHiddenTag.DamageReference),
    },
    "Destroy should sort with Multicast in the primary area before Related."
);
var destroyOption = primaryDestroyOrder
    .KeywordOptionsFor(ECardType.Item)
    .Single(option => option.Mechanic == CollectionMechanic.Destroy);
AssertFalse(destroyOption.IsRelated, "Destroy should never render in the Related subsection.");
AssertFalse(
    primaryDestroyOrder.ItemKeywords.Contains(EHiddenTag.AbsorbDestroy),
    "AbsorbDestroy should not create a broad Destroy Related option."
);

AssertEqual(
    PlayerFacingCardTags.Ordered.Count,
    PlayerFacingCardTags.Ordered.Distinct().Count(),
    "Tag whitelist entries must be distinct."
);
AssertValues(
    PlayerFacingCardTags.Ordered.Select(tag => tag.ToString()).ToArray(),
    new[]
    {
        nameof(ECardTag.Weapon),
        nameof(ECardTag.Friend),
        nameof(ECardTag.Aquatic),
        nameof(ECardTag.Tool),
        nameof(ECardTag.Vehicle),
        nameof(ECardTag.Food),
        nameof(ECardTag.Trap),
        nameof(ECardTag.Toy),
        nameof(ECardTag.Potion),
        nameof(ECardTag.Reagent),
        nameof(ECardTag.Relic),
        nameof(ECardTag.Dragon),
        nameof(ECardTag.Core),
        nameof(ECardTag.Tech),
        nameof(ECardTag.Dinosaur),
        nameof(ECardTag.Apparel),
        nameof(ECardTag.Merchant),
        nameof(ECardTag.Property),
        nameof(ECardTag.Loot),
        nameof(ECardTag.Instrument),
        nameof(ECardTag.Drone),
        nameof(ECardTag.Ray),
    },
    "Tag whitelist should match the curated CollectionPanel tag order."
);
foreach (
    var mechanismTag in new[]
    {
        ECardTag.Unsellable,
        ECardTag.Unstashable,
        ECardTag.Event,
        ECardTag.Combat,
    }
)
    AssertFalse(
        PlayerFacingCardTags.Ordered.Contains(mechanismTag),
        $"Tag whitelist must exclude mechanism tag {mechanismTag}."
    );
foreach (
    var playerFacingTag in new[]
    {
        ECardTag.Apparel,
        ECardTag.Merchant,
        ECardTag.Loot,
        ECardTag.Weapon,
        ECardTag.Instrument,
    }
)
    AssertTrue(
        PlayerFacingCardTags.Ordered.Contains(playerFacingTag),
        $"Tag whitelist should include player-facing type/tag {playerFacingTag}."
    );
foreach (
    var unusedTypeTag in new[] { ECardTag.Ingredient, ECardTag.Key, ECardTag.Map, ECardTag.Sigil }
)
    AssertFalse(
        PlayerFacingCardTags.Ordered.Contains(unusedTypeTag),
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
        nameof(EHiddenTag.Quest),
        nameof(EHiddenTag.Flying),
        nameof(EHiddenTag.Haste),
        nameof(EHiddenTag.Charge),
        nameof(EHiddenTag.Cooldown),
        nameof(EHiddenTag.Slow),
        nameof(EHiddenTag.Freeze),
        nameof(EHiddenTag.Damage),
        nameof(EHiddenTag.Shield),
        nameof(EHiddenTag.Heal),
        nameof(EHiddenTag.Health),
        nameof(EHiddenTag.Burn),
        nameof(EHiddenTag.Poison),
        nameof(EHiddenTag.Regen),
        nameof(EHiddenTag.Crit),
        nameof(EHiddenTag.Ammo),
        nameof(EHiddenTag.Lifesteal),
        nameof(EHiddenTag.Rage),
        nameof(EHiddenTag.Gold),
        nameof(EHiddenTag.Income),
        nameof(EHiddenTag.Value),
        nameof(EHiddenTag.Tempo),
        nameof(EHiddenTag.QuestReference),
        nameof(EHiddenTag.FlyingReference),
        nameof(EHiddenTag.HasteReference),
        nameof(EHiddenTag.CooldownReference),
        nameof(EHiddenTag.SlowReference),
        nameof(EHiddenTag.FreezeReference),
        nameof(EHiddenTag.DamageReference),
        nameof(EHiddenTag.ShieldReference),
        nameof(EHiddenTag.HealReference),
        nameof(EHiddenTag.HealthReference),
        nameof(EHiddenTag.BurnReference),
        nameof(EHiddenTag.PoisonReference),
        nameof(EHiddenTag.RegenReference),
        nameof(EHiddenTag.CritReference),
        nameof(EHiddenTag.AmmoReference),
        nameof(EHiddenTag.RageReference),
        nameof(EHiddenTag.TempoReference),
        nameof(EHiddenTag.EconomyReference),
        nameof(EHiddenTag.PotionReference),
    },
    "Keyword whitelist should match the curated CollectionPanel keyword list."
);
foreach (var nonKeyword in new[] { EHiddenTag.Merchant, EHiddenTag.Package, EHiddenTag.Unsellable })
    AssertFalse(
        CollectionKeywordWhitelist.Ordered.Contains(nonKeyword),
        $"Keyword whitelist must exclude non-keyword tag {nonKeyword}."
    );
foreach (
    var referenceKeyword in new[]
    {
        EHiddenTag.ChilledReference,
        EHiddenTag.HeatedReference,
        EHiddenTag.JoyReference,
        EHiddenTag.TechReference,
    }
)
    AssertFalse(
        CollectionKeywordWhitelist.Ordered.Contains(referenceKeyword),
        $"Keyword whitelist should exclude non-curated reference tag {referenceKeyword}."
    );
foreach (
    var referenceKeyword in new[]
    {
        EHiddenTag.DamageReference,
        EHiddenTag.HealReference,
        EHiddenTag.AmmoReference,
        EHiddenTag.RageReference,
        EHiddenTag.EconomyReference,
        EHiddenTag.PotionReference,
    }
)
    AssertTrue(
        CollectionKeywordWhitelist.Ordered.Contains(referenceKeyword),
        $"Keyword whitelist should include curated reference tag {referenceKeyword}."
    );

AssertTrue(
    CollectionKeywordWhitelist.IsReferenceKeyword(EHiddenTag.PotionReference),
    "PotionReference should start the keyword reference subsection like other reference keywords."
);
AssertTrue(
    CollectionKeywordWhitelist.IsReferenceKeyword(EHiddenTag.TempoReference),
    "TempoReference should remain in the Related keyword subsection."
);
AssertFalse(
    CollectionKeywordWhitelist.IsReferenceKeyword(EHiddenTag.Poison),
    "Base gameplay keywords should not be classified as reference keywords."
);
AssertFalse(
    CollectionKeywordWhitelist.IsRelatedKeyword(EHiddenTag.Multicast),
    "Multicast should remain outside the Related keyword subsection."
);
AssertTrue(
    CollectionKeywordWhitelist.IsRelatedKeyword(EHiddenTag.PotionReference),
    "Curated reference keywords should remain in the Related keyword subsection."
);
AssertFalse(
    CollectionKeywordWhitelist.IsRelatedKeyword(EHiddenTag.Poison),
    "Base gameplay keywords should remain outside the Related keyword subsection."
);

AssertTrue(
    ReferenceTagBaseResolver.TryResolve(EHiddenTag.PotionReference, out var potionReferenceBase),
    "PotionReference should resolve to a base display tag."
);
AssertEqual(
    (ECardTag?)ECardTag.Potion,
    potionReferenceBase.CardTag,
    "PotionReference should render through the Potion card-tag display."
);
AssertEqual(
    (EHiddenTag?)null,
    potionReferenceBase.HiddenTag,
    "PotionReference should not claim a hidden-tag base because no EHiddenTag.Potion exists."
);

AssertTrue(
    ReferenceTagBaseResolver.TryResolve(EHiddenTag.PoisonReference, out var poisonReferenceBase),
    "Existing hidden-tag references should still resolve."
);
AssertEqual(
    (EHiddenTag?)EHiddenTag.Poison,
    poisonReferenceBase.HiddenTag,
    "PoisonReference should keep its Poison hidden-tag display base."
);
AssertEqual(
    (ECardTag?)null,
    poisonReferenceBase.CardTag,
    "PoisonReference should not claim a card-tag display base."
);
AssertTrue(
    ReferenceTagBaseResolver.TryResolve(EHiddenTag.TempoReference, out var tempoReferenceBase),
    "TempoReference should resolve to the existing Tempo display base."
);
AssertEqual(
    (EHiddenTag?)EHiddenTag.Tempo,
    tempoReferenceBase.HiddenTag,
    "TempoReference should render with the Tempo hidden-tag typography."
);
AssertEqual(
    (ECardTag?)null,
    tempoReferenceBase.CardTag,
    "TempoReference should not claim a card-tag display base."
);

AssertFalse(
    ReferenceTagBaseResolver.TryResolve(EHiddenTag.Poison, out _),
    "Non-reference hidden tags should not resolve through the reference base resolver."
);
foreach (
    var bazaarDbKeyword in new[]
    {
        EHiddenTag.Ammo,
        EHiddenTag.Crit,
        EHiddenTag.Health,
        EHiddenTag.Quest,
        EHiddenTag.Value,
    }
)
    AssertTrue(
        CollectionKeywordWhitelist.Ordered.Contains(bazaarDbKeyword),
        $"Keyword whitelist should include BazaarDB keyword {bazaarDbKeyword}."
    );
foreach (
    var nonBazaarDbKeyword in new[]
    {
        EHiddenTag.AbsorbDestroy,
        EHiddenTag.AbsorbFreeze,
        EHiddenTag.AbsorbSlow,
        EHiddenTag.CanCrit,
        EHiddenTag.Experience,
        EHiddenTag.Level,
        EHiddenTag.Reload,
        EHiddenTag.Ticket,
    }
)
    AssertFalse(
        CollectionKeywordWhitelist.Ordered.Contains(nonBazaarDbKeyword),
        $"Keyword whitelist should exclude non-BazaarDB keyword {nonBazaarDbKeyword}."
    );
AssertFalse(
    CollectionKeywordWhitelist.Ordered.Contains(EHiddenTag.Multicast),
    "The native keyword whitelist should not create a second Multicast option beside the structured mechanic."
);

var availableFacetCards = new[]
{
    Card(
        "Damage Weapon",
        ETier.Bronze,
        tags: new[] { ECardTag.Weapon, ECardTag.Instrument, ECardTag.Ingredient },
        hiddenTags: new[]
        {
            EHiddenTag.Damage,
            EHiddenTag.Tempo,
            EHiddenTag.DamageReference,
            EHiddenTag.CanCrit,
        }
    ),
    Card("Multicast Item", ETier.Bronze, mechanics: CollectionMechanic.Multicast),
    Card(
        "Package Shield",
        ETier.Bronze,
        isPackage: true,
        tags: new[] { ECardTag.Tool },
        hiddenTags: new[] { EHiddenTag.Package, EHiddenTag.Shield }
    ),
    Card(
        "Skill Quest",
        ETier.Bronze,
        type: ECardType.Skill,
        hiddenTags: new[] { EHiddenTag.Quest }
    ),
    Card(
        "Instrument Tempo Reference Skill",
        ETier.Bronze,
        type: ECardType.Skill,
        tags: new[] { ECardTag.Instrument },
        hiddenTags: new[] { EHiddenTag.TempoReference }
    ),
    Card("Potion Reference", ETier.Bronze, hiddenTags: new[] { EHiddenTag.PotionReference }),
};
var availableFacets = CollectionFacetAvailability.SnapshotFor(availableFacetCards);
AssertValues(
    availableFacets.ItemTags.Select(tag => tag.ToString()).ToArray(),
    new[] { nameof(ECardTag.Weapon), nameof(ECardTag.Instrument) },
    "Available item tags should include only non-package catalog tags that are player-facing."
);
AssertValues(
    availableFacets.SkillTags.Select(tag => tag.ToString()).ToArray(),
    new[] { nameof(ECardTag.Instrument) },
    "Available skill tags should include player-facing tags carried by accepted skills."
);
AssertValues(
    availableFacets.ItemKeywords.Select(tag => tag.ToString()).ToArray(),
    new[]
    {
        nameof(EHiddenTag.Damage),
        nameof(EHiddenTag.Tempo),
        nameof(EHiddenTag.DamageReference),
        nameof(EHiddenTag.PotionReference),
    },
    "Available item keywords should include non-package catalog keywords and curated references from the same facet."
);
AssertValues(
    availableFacets.SkillKeywords.Select(tag => tag.ToString()).ToArray(),
    new[] { nameof(EHiddenTag.Quest), nameof(EHiddenTag.TempoReference) },
    "Available skill keywords should be computed independently from item keywords."
);
var legacyFacets = CollectionFacetAvailability.SnapshotFor(new[] { damageItem, damageSkill });
AssertFalse(
    legacyFacets.TagsFor(ECardType.Item).Contains(ECardTag.Instrument),
    "Legacy item catalogs should omit Instrument when no accepted item carries it."
);
AssertFalse(
    legacyFacets.TagsFor(ECardType.Skill).Contains(ECardTag.Instrument),
    "Legacy skill catalogs should omit Instrument when no accepted skill carries it."
);
AssertFalse(
    legacyFacets.KeywordsFor(ECardType.Item).Contains(EHiddenTag.Tempo),
    "Legacy item catalogs should omit Tempo when no accepted item carries it."
);
AssertFalse(
    legacyFacets.KeywordsFor(ECardType.Skill).Contains(EHiddenTag.TempoReference),
    "Legacy skill catalogs should omit TempoReference when no accepted skill carries it."
);
AssertFalse(
    legacyFacets.MechanicsFor(ECardType.Item).Contains(CollectionMechanic.Multicast),
    "Item mechanic availability should omit Multicast when the item catalog has no Multicast template."
);
AssertFalse(
    availableFacets.MechanicsFor(ECardType.Skill).Contains(CollectionMechanic.Multicast),
    "Skill mechanic availability should omit Multicast when only the item catalog contains it."
);

var queryCatalogCards = new[]
{
    Card(
        "Query Damage Weapon",
        ETier.Bronze,
        tags: new[] { ECardTag.Weapon },
        hiddenTags: new[] { EHiddenTag.Damage },
        heroes: new[] { EHero.Vanessa }
    ),
    Card(
        "Query Shield Tool",
        ETier.Bronze,
        tags: new[] { ECardTag.Tool },
        hiddenTags: new[] { EHiddenTag.Shield },
        heroes: new[] { EHero.Dooley }
    ),
};
var queryAvailability = new CollectionFacetAvailabilitySnapshot(
    new[] { ECardTag.Weapon },
    Array.Empty<ECardTag>(),
    new[] { EHiddenTag.Damage },
    Array.Empty<EHiddenTag>(),
    Array.Empty<CollectionMechanic>(),
    Array.Empty<CollectionMechanic>()
);
var querySource = Source(
    "merchant:query:vanessa",
    CollectionSourceKind.Merchant,
    availableHeroes: new[] { EHero.Vanessa }
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
queryFilter.ToggleHero(EHero.Vanessa);
queryFilter.Tags.Add(ECardTag.Weapon);
queryFilter.Tags.Add(ECardTag.Tool);
queryFilter.Keywords.Add(EHiddenTag.Damage);
queryFilter.Keywords.Add(EHiddenTag.Shield);
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
    new[] { ECardTag.Weapon },
    "CollectionQuery should return retained item tags when unavailable selected tags are trimmed."
);
AssertValues(
    queryResult.Normalization.RetainedKeywords!.ToArray(),
    new[] { EHiddenTag.Damage },
    "CollectionQuery should return retained keywords when unavailable selected keywords are trimmed."
);
AssertEqual(
    0,
    queryResult.Normalization.RetainedMechanics!.Count,
    "CollectionQuery should trim unavailable mechanic selections through the same normalization."
);
AssertValues(
    queryFilter.Tags.ToArray(),
    new[] { ECardTag.Weapon, ECardTag.Tool },
    "CollectionQuery should not mutate selected tags."
);
AssertValues(
    queryFilter.Keywords.ToArray(),
    new[] { EHiddenTag.Damage, EHiddenTag.Shield },
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
    EHero.Common,
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
        ETier.Bronze,
        type: ECardType.Skill,
        heroes: new[] { EHero.Vanessa }
    ),
    Card(
        "All Heroes Dooley Skill",
        ETier.Bronze,
        type: ECardType.Skill,
        heroes: new[] { EHero.Dooley }
    ),
    Card(
        "All Heroes Common Skill",
        ETier.Bronze,
        type: ECardType.Skill,
        heroes: new[] { EHero.Common }
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
        ActiveType = ECardType.Skill,
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
    ActiveType = ECardType.Skill,
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
heroMismatchFilter.ToggleHero(EHero.Dooley);
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

var tagGateOffFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
tagGateOffFilter.Tags.Add(ECardTag.Tool);
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
    ETier.Gold,
    size: ECardSize.Large,
    tags: new[] { ECardTag.Tool },
    hiddenTags: new[] { EHiddenTag.Burn },
    heroes: new[] { EHero.Dooley }
);
var searchGlobalFilter = new CollectionFilterState
{
    SelectedSourceKey = querySource.SourceKey,
    SearchQuery = "lighter",
};
searchGlobalFilter.ToggleHero(EHero.Vanessa);
searchGlobalFilter.Tiers.Add(ETier.Bronze);
searchGlobalFilter.Tags.Add(ECardTag.Weapon);
searchGlobalFilter.Keywords.Add(EHiddenTag.Damage);
searchGlobalFilter.Sizes.Add(ECardSize.Small);
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
searchWithinFilters.ToggleHero(EHero.Vanessa);
searchWithinFilters.Tags.Add(ECardTag.Weapon);
searchWithinFilters.Keywords.Add(EHiddenTag.Damage);
AssertSequence(
    CollectionQuery
        .Run(queryCatalogCards, searchWithinFilters, queryAvailability, queryCatalog, queryResolver)
        .Cards,
    new[] { queryCatalogCards[0].Id },
    "Collection search should return text matches that satisfy every selected filter."
);

var emptyTrimFilter = new CollectionFilterState();
emptyTrimFilter.Tags.Add(ECardTag.Tool);
emptyTrimFilter.Keywords.Add(EHiddenTag.Shield);
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
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Vanessa }
);
var sharedHeroSkill = Card(
    "Shared Hero Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Vanessa, EHero.Dooley }
);
var commonSkill = Card(
    "Common Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Common }
);
var commonSharedSkill = Card(
    "Common Shared Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: new[] { EHero.Common, EHero.Vanessa }
);
var missingHeroSkill = Card(
    "Missing Hero Skill",
    ETier.Bronze,
    type: ECardType.Skill,
    heroes: Array.Empty<EHero>()
);
var emptyHeroSkillFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
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
var exclusiveSkillFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
exclusiveSkillFilter.ToggleHero(EHero.Vanessa);
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

var commonSkillFilter = new CollectionFilterState { ActiveType = ECardType.Skill };
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
    CollectionHeroScope.MatchesSkillHeroScope(new[] { EHero.Vanessa, EHero.Mak }, EHero.Vanessa),
    "A {Vanessa, Mak} shared skill matches the Vanessa scope."
);
AssertFalse(
    CollectionHeroScope.MatchesSkillHeroScope(new[] { EHero.Vanessa, EHero.Mak }, EHero.Dooley),
    "A {Vanessa, Mak} shared skill does not match an uninvolved hero."
);
AssertFalse(
    CollectionHeroScope.MatchesSkillHeroScope(new[] { EHero.Common, EHero.Vanessa }, EHero.Vanessa),
    "Common-scoped multi-hero skills are not general-shared."
);

var debugTemplateClassification = CollectionCardClassifier.Classify(
    ECardType.Item,
    ESpawnEligibility.Always,
    "Assets/Cards/Debug.png",
    "[DEBUG] Item"
);
AssertFalse(
    debugTemplateClassification.IsCatalogCard,
    "Debug-marked templates do not enter the catalog even when they have art."
);
AssertEqual(
    CollectionCardEligibilityReason.DebugTemplate,
    debugTemplateClassification.EligibilityReason,
    "Debug-marked templates should report a reasoned rejection."
);
var templateNameClassification = CollectionCardClassifier.Classify(
    ECardType.Item,
    ESpawnEligibility.Always,
    "Assets/Cards/Template.png",
    "[TEMPLATE] Item"
);
AssertFalse(
    templateNameClassification.IsCatalogCard,
    "Template-marked entries do not enter the catalog even when they have art."
);
AssertEqual(
    CollectionCardEligibilityReason.TemplateInternalName,
    templateNameClassification.EligibilityReason,
    "Template-marked entries should report a reasoned rejection."
);
var acceptedSkillClassification = CollectionCardClassifier.Classify(
    ECardType.Skill,
    ESpawnEligibility.Always,
    "Assets/Cards/Skill.png",
    "Real Skill"
);
AssertTrue(
    acceptedSkillClassification.IsCatalogCard,
    "Normal Item/Skill cards with art are catalog cards."
);
AssertEqual(
    CollectionCardEligibilityReason.Accepted,
    acceptedSkillClassification.EligibilityReason,
    "Accepted catalog cards should report the accepted reason."
);
var placeholderSkillClassification = CollectionCardClassifier.Classify(
    ECardType.Skill,
    ESpawnEligibility.Always,
    "Icon_Skill_Placeholder.png",
    "Aggressive Mutations"
);
AssertFalse(
    placeholderSkillClassification.IsCatalogCard,
    "Placeholder skill art is not a catalog-ready skill."
);
AssertEqual(
    CollectionCardEligibilityReason.PlaceholderArtKey,
    placeholderSkillClassification.EligibilityReason,
    "Placeholder art should report a reasoned rejection."
);
var musicNoteSocketClassification = CollectionCardClassifier.Classify(
    ECardType.SocketEffect,
    ESpawnEligibility.Always,
    "Assets/Cards/MusicNote.png",
    "Music Note A"
);
AssertFalse(
    musicNoteSocketClassification.IsCatalogCard,
    "Music Note socket effects should remain outside the Item/Skill catalog."
);
AssertEqual(
    CollectionCardEligibilityReason.UnsupportedType,
    musicNoteSocketClassification.EligibilityReason,
    "Music Note socket effects should remain excluded by their non-Item/Skill card type."
);
var musicNotePlaceholderClassification = CollectionCardClassifier.Classify(
    ECardType.Item,
    ESpawnEligibility.Always,
    "Icon_Music_Note_Placeholder.png",
    "Music Note Placeholder"
);
AssertFalse(
    musicNotePlaceholderClassification.IsCatalogCard,
    "Music Note placeholder art should not enter the catalog even if misclassified as an Item."
);
AssertEqual(
    CollectionCardEligibilityReason.PlaceholderArtKey,
    musicNotePlaceholderClassification.EligibilityReason,
    "Music Note placeholder art should remain excluded by the placeholder-art rule."
);
AssertFalse(
    CollectionCardClassifier
        .Classify(ECardType.Skill, ESpawnEligibility.Always, "Placeholder", "[SKILL TEMPLATE]")
        .IsCatalogCard,
    "Skill template placeholders do not enter the catalog."
);
var materialArtClassification = CollectionCardClassifier.Classify(
    ECardType.Item,
    ESpawnEligibility.Always,
    "Assets/Cards/LegacyItem.mat",
    "Legacy Material Item"
);
AssertFalse(
    materialArtClassification.IsCatalogCard,
    "Legacy material art keys do not enter the catalog."
);
AssertEqual(
    CollectionCardEligibilityReason.MaterialArtKey,
    materialArtClassification.EligibilityReason,
    "Material art keys should report a reasoned rejection."
);
AssertFalse(
    CollectionCardClassifier
        .Classify(
            ECardType.Item,
            ESpawnEligibility.Always,
            "Assets/Cards/Template.png",
            "[SMALL ITEM TEMPLATE]"
        )
        .IsCatalogCard,
    "Bracketed item template names do not enter the catalog."
);
AssertEqual(
    CollectionCardEligibilityReason.InvalidArtKey,
    CollectionCardClassifier
        .Classify(ECardType.Item, ESpawnEligibility.Always, "Invalid", "Real Item")
        .EligibilityReason,
    "The literal Invalid art key should report a reasoned rejection."
);
AssertEqual(
    CollectionCardEligibilityReason.MissingArtKey,
    CollectionCardClassifier
        .Classify(ECardType.Item, ESpawnEligibility.Always, "", "Real Item")
        .EligibilityReason,
    "A missing art key should report a reasoned rejection."
);
AssertEqual(
    CollectionCardEligibilityReason.UnsupportedType,
    CollectionCardClassifier
        .Classify((ECardType)999, ESpawnEligibility.Always, "Assets/Cards/Event.png", "Event")
        .EligibilityReason,
    "Unsupported card types should report a reasoned rejection."
);
var catalogEligibilityCases = new[]
{
    (
        Name: "Item Always",
        Template: CatalogTemplate(ECardType.Item, ESpawnEligibility.Always),
        ExpectedCatalogCard: true,
        ExpectedReason: CollectionCardEligibilityReason.Accepted
    ),
    (
        Name: "Skill Always",
        Template: CatalogTemplate(ECardType.Skill, ESpawnEligibility.Always),
        ExpectedCatalogCard: true,
        ExpectedReason: CollectionCardEligibilityReason.Accepted
    ),
    (
        Name: "Item GuidOnly",
        Template: CatalogTemplate(ECardType.Item, ESpawnEligibility.GuidOnly),
        ExpectedCatalogCard: true,
        ExpectedReason: CollectionCardEligibilityReason.Accepted
    ),
    (
        Name: "Skill GuidOnly",
        Template: CatalogTemplate(ECardType.Skill, ESpawnEligibility.GuidOnly),
        ExpectedCatalogCard: true,
        ExpectedReason: CollectionCardEligibilityReason.Accepted
    ),
    (
        Name: "Item Never",
        Template: CatalogTemplate(ECardType.Item, ESpawnEligibility.Never),
        ExpectedCatalogCard: false,
        ExpectedReason: CollectionCardEligibilityReason.NeverSpawnEligibility
    ),
    (
        Name: "Skill Never",
        Template: CatalogTemplate(ECardType.Skill, ESpawnEligibility.Never),
        ExpectedCatalogCard: false,
        ExpectedReason: CollectionCardEligibilityReason.NeverSpawnEligibility
    ),
};
foreach (var testCase in catalogEligibilityCases)
{
    var classification = CollectionCardClassifier.Classify(testCase.Template);
    AssertEqual(
        testCase.ExpectedCatalogCard,
        classification.IsCatalogCard,
        $"{testCase.Name} should have the expected catalog eligibility."
    );
    AssertEqual(
        testCase.ExpectedReason,
        classification.EligibilityReason,
        $"{testCase.Name} should report the expected catalog eligibility reason."
    );
}
var hiddenTagPackageTemplate = new TCardItem
{
    Id = Guid.NewGuid(),
    Type = ECardType.Item,
    SpawningEligibility = ESpawnEligibility.GuidOnly,
    ArtKey = "Assets/Cards/Bundle.png",
    InternalName = "Vanessa Starter Bundle",
    HiddenTags = new HashSet<EHiddenTag> { EHiddenTag.Package },
};
var hiddenTagPackageClassification = CollectionCardClassifier.Classify(hiddenTagPackageTemplate);
AssertTrue(
    hiddenTagPackageClassification.IsCatalogCard,
    "GuidOnly package templates remain eligible for the catalog classifier."
);
AssertTrue(
    hiddenTagPackageClassification.IsPackage,
    "HiddenTag.Package marks package templates as hidden from CollectionPanel."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { CollectionCardVm.From(hiddenTagPackageTemplate, hiddenTagPackageClassification) },
        new CollectionFilterState()
    ),
    Array.Empty<Guid>(),
    "GuidOnly package templates remain hidden by the existing package visibility rule."
);
var packageNameWithoutTagTemplate = new TCardItem
{
    Type = ECardType.Item,
    ArtKey = "Assets/Cards/Package.png",
    InternalName = "Vanessa Starter Package",
};
AssertFalse(
    CollectionCardClassifier.Classify(packageNameWithoutTagTemplate).IsPackage,
    "Package-like names no longer mark packages without HiddenTag.Package."
);

// --- Day filter: shared GameData MaximumTier gates StartingTier and ANDs with other dimensions. ---
var dayBronze = Card("Day Bronze", ETier.Bronze);
var daySilver = Card("Day Silver", ETier.Silver);
var dayGold = Card("Day Gold", ETier.Gold);
var dayDiamond = Card("Day Diamond", ETier.Diamond);
var dayLegendary = Card("Day Legendary", ETier.Legendary);
var dayPool = new[] { dayBronze, daySilver, dayGold, dayDiamond, dayLegendary };
var bronzeDayTable = GameDataDayTierTable.FromWeights(1f, 0f, 0f, 0f)!;
var goldDayTable = GameDataDayTierTable.FromWeights(0.7f, 0.2f, 0.09f, 0f)!;
var diamondDayTable = GameDataDayTierTable.FromWeights(0.89f, 0.1f, 0f, 0.01f)!;

AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        new CollectionFilterState(),
        new CollectionFilterContext { DayTiers = bronzeDayTable }
    ),
    new[] { dayBronze.Id },
    "A Bronze GameData ceiling keeps only Bronze-start cards."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        new CollectionFilterState(),
        new CollectionFilterContext { DayTiers = goldDayTable }
    ),
    new[] { dayBronze.Id, daySilver.Id, dayGold.Id },
    "The highest positive GameData tier is the ceiling even when its probability is smaller."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        new CollectionFilterState(),
        new CollectionFilterContext { DayTiers = diamondDayTable }
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
        new CollectionFilterContext { DayTiers = bronzeDayTable }
    ),
    new[] { dayBronze.Id, daySilver.Id, dayGold.Id, dayDiamond.Id, dayLegendary.Id },
    "Turning the Day filter off skips a trustworthy GameData ceiling."
);

var dayAndTierFilter = new CollectionFilterState();
dayAndTierFilter.Tiers.Add(ETier.Diamond);
AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        dayAndTierFilter,
        new CollectionFilterContext { DayTiers = goldDayTable }
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
            DayTiers = diamondDayTable,
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
            DayTiers = bronzeDayTable,
        }
    ),
    new[] { dayGold.Id, dayDiamond.Id },
    "A fixed-tier source's pool is exempt from the day ceiling (Luxe on Day 1 still deals Diamond)."
);
var suppressedDayManualTier = new CollectionFilterState();
suppressedDayManualTier.Tiers.Add(ETier.Gold);
AssertSequence(
    CollectionFilterEngine.Apply(
        dayPool,
        suppressedDayManualTier,
        new CollectionFilterContext
        {
            OfferedCardIds = new[] { dayGold.Id, dayDiamond.Id },
            SuppressDayGate = true,
            DayTiers = bronzeDayTable,
        }
    ),
    new[] { dayGold.Id },
    "Suppressing the day gate leaves the manual Tier row in force."
);

var searchDamageReference = Card(
    "Internal Synergy",
    ETier.Bronze,
    hiddenTags: new[] { EHiddenTag.BurnReference },
    description: "When you <color=#00ffff>Slow</color>, Charge this {ability.0} seconds.",
    internalName: "Internal_Synergy_Card",
    artKey: "anglerfish_art"
);
var searchShield = Card(
    "Bulwark",
    ETier.Bronze,
    hiddenTags: new[] { EHiddenTag.Shield },
    description: "Gain {ability.0} Shield."
);
var searchChinese = Card(
    "深水琵琶鱼",
    ETier.Bronze,
    description: "当你减速时，充能 {ability.0} 秒。"
);
var searchLighter = Card("Lighter", ETier.Bronze, description: "Burn an item.");
var searchInitialism = Card("Molten Ball Blaster", ETier.Bronze);
var searchRepeatedLetters = Card(
    "Dreaded Damage Dealer",
    ETier.Bronze,
    internalName: "DreadedDamageDealer"
);
var searchLetterSoup = Card(
    "Lime",
    ETier.Bronze,
    description: "Iguana Gold Honey Teapot Echo Rabbit."
);
var tooltipOnlySearchVm = CollectionCardVm.From(
    new TCardItem
    {
        Id = Guid.NewGuid(),
        Type = ECardType.Item,
        StartingTier = ETier.Bronze,
        Size = ECardSize.Small,
        InternalName = "Tooltip Search Probe",
        ArtKey = "tooltip-search-probe",
        Heroes = new HashSet<EHero> { EHero.Common },
        Localization = new TCardLocalization
        {
            Title = new TLocalizableText { Text = "Tooltip Search Probe" },
            Tooltips = new List<TTooltip>
            {
                new()
                {
                    Content = new TLocalizableText
                    {
                        Text = "Deal <color=#ff0000>Burn</color> to enemies {ability.0}.",
                    },
                },
            },
        },
    }
);
var localizationHashSearchVm = CollectionCardVm.From(
    new TCardItem
    {
        Id = Guid.NewGuid(),
        Type = ECardType.Skill,
        StartingTier = ETier.Bronze,
        Size = ECardSize.Medium,
        InternalName = "Hash Search Probe",
        ArtKey = "hash-search-probe",
        Heroes = new HashSet<EHero> { EHero.Common },
        Localization = new TCardLocalization
        {
            Title = new TLocalizableText
            {
                Key = "69d1f37d94ddc3150f8dd44d12d53ced",
                Text = "Gumball Machine",
            },
        },
    }
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
        new[] { tooltipOnlySearchVm },
        new CollectionFilterState { SearchQuery = "burn enemies" }
    ),
    new[] { tooltipOnlySearchVm.Id },
    "Collection search should match normalized tooltip text when a card has no description."
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
        new[] { localizationHashSearchVm },
        new CollectionFilterState { ActiveType = ECardType.Skill, SearchQuery = "ddddddddd" }
    ),
    Array.Empty<Guid>(),
    "Collection search should not expose opaque localization hashes as searchable content."
);
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { localizationHashSearchVm },
        new CollectionFilterState { ActiveType = ECardType.Skill, SearchQuery = "gumball" }
    ),
    new[] { localizationHashSearchVm.Id },
    "Removing localization hashes should preserve authored fallback text search."
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
var prebuiltSearchText = Card("Surface", ETier.Bronze, searchText: "localized fallback corpus");
AssertSequence(
    CollectionFilterEngine.Apply(
        new[] { prebuiltSearchText, searchShield },
        new CollectionFilterState { SearchQuery = "fallback corpus" }
    ),
    new[] { prebuiltSearchText.Id },
    "Collection search should honor prebuilt card search text."
);

Console.WriteLine("CollectionFilterEngine checks passed.");

static TCardBase CatalogTemplate(ECardType type, ESpawnEligibility spawningEligibility) =>
    type switch
    {
        ECardType.Item => new TCardItem
        {
            Type = ECardType.Item,
            SpawningEligibility = spawningEligibility,
            ArtKey = "Assets/Cards/CatalogItem.png",
            InternalName = "Catalog Item",
        },
        ECardType.Skill => new TCardSkill
        {
            Type = ECardType.Skill,
            SpawningEligibility = spawningEligibility,
            ArtKey = "Assets/Cards/CatalogSkill.png",
            InternalName = "Catalog Skill",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

static CollectionCardVm InstrumentFacetCard(
    string name = "Tempo Instrument Solo",
    ECardType type = ECardType.Item,
    ETier tier = ETier.Bronze,
    ECardSize size = ECardSize.Medium,
    ECardTag tag = ECardTag.Instrument,
    EHiddenTag keyword = EHiddenTag.Tempo,
    EHero hero = EHero.Vanessa
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
    ETier tier,
    ECardType type = ECardType.Item,
    ECardSize size = ECardSize.Medium,
    bool isPackage = false,
    IReadOnlyCollection<ECardTag>? tags = null,
    IReadOnlyCollection<EHiddenTag>? hiddenTags = null,
    IReadOnlyCollection<EHero>? heroes = null,
    string? description = null,
    string? internalName = null,
    string? artKey = null,
    IReadOnlyDictionary<EEnchantmentType, CollectionCardEnchantmentFacets>? enchantments = null,
    string? searchText = null,
    CollectionMechanic mechanics = CollectionMechanic.None
) =>
    new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Size = size,
        StartingTier = tier,
        Tags = tags ?? Array.Empty<ECardTag>(),
        HiddenTags = hiddenTags ?? Array.Empty<EHiddenTag>(),
        Mechanics = mechanics,
        DisplayName = name,
        Description = description ?? string.Empty,
        InternalName = internalName ?? name,
        ArtKey = artKey ?? string.Empty,
        IsPackage = isPackage,
        Heroes = heroes ?? new[] { EHero.Common },
        Enchantments =
            enchantments ?? new Dictionary<EEnchantmentType, CollectionCardEnchantmentFacets>(),
        SearchText = searchText ?? string.Empty,
    };

static TCardItem MechanicItemTemplate(
    Dictionary<ETier, TCardTier> tiers,
    Dictionary<string, TCardAbility>? abilities = null,
    Dictionary<string, TCardAura>? auras = null,
    Dictionary<EEnchantmentType, TEnchantment>? enchantments = null,
    HashSet<EHiddenTag>? hiddenTags = null
) =>
    new()
    {
        Id = Guid.NewGuid(),
        Type = ECardType.Item,
        StartingTier = ETier.Bronze,
        Size = ECardSize.Medium,
        InternalName = "Multicast Projection Item",
        ArtKey = "Assets/Cards/MulticastProjectionItem.png",
        HiddenTags = hiddenTags ?? new HashSet<EHiddenTag>(),
        Tiers = tiers,
        Abilities = abilities ?? new Dictionary<string, TCardAbility>(),
        Auras = auras ?? new Dictionary<string, TCardAura>(),
        Enchantments = enchantments,
    };

static TCardTier Tier(
    Dictionary<ECardAttributeType, int>? attributes = null,
    IReadOnlyCollection<string>? abilityIds = null,
    IReadOnlyCollection<string>? auraIds = null
) =>
    new()
    {
        Attributes = attributes ?? new Dictionary<ECardAttributeType, int>(),
        AbilityIds = abilityIds == null ? new HashSet<string>() : new HashSet<string>(abilityIds),
        AuraIds = auraIds == null ? new HashSet<string>() : new HashSet<string>(auraIds),
    };

static TCardAbility Ability(ITAction action, TTriggerBase? trigger = null) =>
    new()
    {
        Id = Guid.NewGuid().ToString(),
        Action = action,
        Trigger = trigger ?? new TTriggerOnCardFired(),
    };

static TCardAura Aura(ITAuraAction action) =>
    new() { Id = Guid.NewGuid().ToString(), Action = action };

static TActionCardModifyAttribute MulticastModifier() =>
    new() { AttributeType = ECardAttributeType.Multicast };

static TAuraActionCardModifyAttribute MulticastAuraModifier() =>
    new() { AttributeType = ECardAttributeType.Multicast };

static CollectionSourceEntry Source(
    string sourceKey,
    CollectionSourceKind kind,
    IReadOnlyList<EHero>? availableHeroes = null,
    bool suppressDayGate = false,
    CollectionSourceHeroMode? heroMode = null
) =>
    new(
        sourceKey,
        kind,
        sourceKey,
        availableHeroes ?? Array.Empty<EHero>(),
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
                ETier.Gold
            ),
            Array.Empty<ECardSize>(),
            Array.Empty<ECardTag>(),
            Array.Empty<ECardTag>(),
            Array.Empty<EHiddenTag>(),
            false,
            Array.Empty<EEnchantmentType>(),
            Array.Empty<ECardTag>(),
            Array.Empty<EHiddenTag>()
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
            Array.Empty<ECardSize>(),
            Array.Empty<ECardTag>(),
            Array.Empty<ECardTag>(),
            Array.Empty<EHiddenTag>(),
            false,
            Array.Empty<EEnchantmentType>(),
            Array.Empty<ECardTag>(),
            Array.Empty<EHiddenTag>()
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

    public IEnumerable<CollectionSourceEntry> For(CollectionSourceKind kind, EHero effectiveHero)
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

    public EHero? LastEffectiveHero { get; private set; }

    public CollectionSourceOfferPoolResult GetOrResolve(
        CollectionSourceEntry source,
        EHero effectiveHero,
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
