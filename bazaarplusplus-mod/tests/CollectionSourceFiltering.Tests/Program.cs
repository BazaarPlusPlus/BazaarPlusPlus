using System.Runtime.Loader;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.Heroes;
using BazaarPlusPlus.GameInterop.HeroPortraits;
using BazaarPlusPlus.TestSupport;

var ailaId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
var ailaId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
var globalMerchantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
var pygTrainerId = Guid.Parse("44444444-4444-4444-4444-444444444444");
var collisionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

var entries = BuildCatalog(
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
    """
);

AssertEqual(5, entries.Count, "Catalog build should keep valid v4 entries.");
AssertTrue(
    entries.All(entry => !string.IsNullOrWhiteSpace(entry.SourceKey)),
    "Every source entry should receive a stable source key."
);
AssertEqual(
    entries.Count,
    entries.Select(entry => entry.SourceKey).Distinct(StringComparer.Ordinal).Count(),
    "Source keys should be unique across current catalog identities."
);

var vanessaAila = entries.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Aila", StringComparison.Ordinal)
    && entry.AvailableHeroes.Contains(CardHero.Vanessa)
);
AssertEqual(
    "merchant:aila:vanessa",
    vanessaAila.SourceKey,
    "Generated source keys should drop source tier and include visible hero identity."
);
AssertValues(
    vanessaAila.SourceTemplateIds.ToArray(),
    new[] { ailaId1, ailaId2 },
    "Source identity must preserve all source template ids for encounter lookup."
);
AssertEqual(
    ailaId1,
    vanessaAila.PortraitTemplateId,
    "Portrait template id should be distinct but drawn from the source id set."
);

var nufuKeys = entries
    .Where(entry => entry.Kind == CollectionSourceKind.Merchant && entry.Name == "Nufu")
    .Select(entry => entry.SourceKey)
    .ToArray();
AssertEqual(2, nufuKeys.Length, "Fixture should exercise a forced source-key collision.");
AssertTrue(
    nufuKeys.All(key => key.StartsWith("merchant:nufu:global:", StringComparison.Ordinal)),
    "Colliding tier-less source keys should receive a template-id fingerprint suffix."
);

AssertThrows<InvalidOperationException>(
    () => BuildCatalog("""{ "schemaVersion": 1, "entries": [] }"""),
    "Schema v1 source catalogs should fail validation."
);
AssertThrows<InvalidOperationException>(
    () =>
        BuildCatalog(
            $$"""
            {
              "schemaVersion": 4,
              "groups": ["generalist"],
              "entries": [
                {
                  "name": "Broken",
                  "kind": "Merchant",
                  "group": "generalist",
                  "order": 0,
                  "availableHeroes": [],
                  "description": "Broken",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "Bogus" } }]
                }
              ]
            }
            """
        ),
    "Unknown enum values should fail catalog validation instead of silently defaulting."
);
AssertThrows<InvalidOperationException>(
    () =>
        BuildCatalog(
            $$"""
            {
              "schemaVersion": 4,
              "entries": [
                {
                  "name": "Broken",
                  "kind": "Merchant",
                  "group": "generalist",
                  "order": 0,
                  "availableHeroes": [],
                  "description": "Broken",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                }
              ]
            }
            """
        ),
    "Schema v4 source catalogs should require a top-level groups list."
);
AssertThrows<InvalidOperationException>(
    () =>
        BuildCatalog(
            $$"""
            {
              "schemaVersion": 4,
              "groups": ["generalist", "generalist"],
              "entries": [
                {
                  "name": "Broken",
                  "kind": "Merchant",
                  "group": "generalist",
                  "order": 0,
                  "availableHeroes": [],
                  "description": "Broken",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                }
              ]
            }
            """
        ),
    "Duplicate group ids should fail catalog validation."
);
AssertThrows<InvalidOperationException>(
    () =>
        BuildCatalog(
            $$"""
            {
              "schemaVersion": 4,
              "groups": ["generalist"],
              "entries": [
                {
                  "name": "Broken",
                  "kind": "Merchant",
                  "group": "typo",
                  "order": 0,
                  "availableHeroes": [],
                  "description": "Broken",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                }
              ]
            }
            """
        ),
    "Entry groups should fail validation when they are not declared in groups."
);
AssertThrows<InvalidOperationException>(
    () =>
        BuildCatalog(
            $$"""
            {
              "schemaVersion": 4,
              "groups": ["generalist"],
              "entries": [
                {
                  "name": "Broken",
                  "kind": "Merchant",
                  "group": "generalist",
                  "availableHeroes": [],
                  "description": "Broken",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                }
              ]
            }
            """
        ),
    "Entry order should be required instead of silently defaulting to zero."
);
AssertThrows<InvalidOperationException>(
    () =>
        BuildCatalog(
            $$"""
            {
              "schemaVersion": 4,
              "groups": ["generalist"],
              "entries": [
                {
                  "name": "One",
                  "kind": "Merchant",
                  "group": "generalist",
                  "order": 0,
                  "availableHeroes": [],
                  "description": "One",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                },
                {
                  "name": "Two",
                  "kind": "Trainer",
                  "group": "generalist",
                  "order": 1,
                  "availableHeroes": [],
                  "description": "Two",
                  "portraitTemplateId": "{{globalMerchantId}}",
                  "sourceTemplateIds": ["{{globalMerchantId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                }
              ]
            }
            """
        ),
    "A source template id should belong to exactly one collection source entry."
);

AssertEqual(
    CollectionSourceHeroParseStatus.Invalid,
    ParseHero("NotARealHero").Status,
    "Unknown hero names should remain invalid catalog data."
);
AssertEqual(
    CollectionSourceHeroParseStatus.KnownButUnavailable,
    ParseHero("TheDragons", hero => hero != CardHero.TheDragons).Status,
    "The canonical Dragons id should remain known when the current game enum does not expose it."
);
var catalogDragonsHero = CardHero.TheDragons;
AssertEqual(
    CollectionSourceHeroParseStatus.Resolved,
    ParseHero("Hero8", _ => true).Status,
    "Legacy JSON should resolve when a future runtime exposes only the canonical enum name."
);
AssertEqual(
    CollectionSourceHeroParseStatus.Resolved,
    ParseHero("TheDragons", _ => true).Status,
    "Canonical JSON should resolve when the current runtime exposes only the legacy enum name."
);
var legacyAliasSource = BuildDragonsAliasEntry("Hero8", _ => true);
var canonicalAliasSource = BuildDragonsAliasEntry("TheDragons", _ => true);
AssertEqual(
    canonicalAliasSource.SourceKey,
    legacyAliasSource.SourceKey,
    "Source keys should canonicalize The Dragons aliases."
);
AssertEqual(
    canonicalAliasSource.OfferRuleFingerprint,
    legacyAliasSource.OfferRuleFingerprint,
    "Offer-rule fingerprints should canonicalize The Dragons aliases."
);
AssertEqual(
    CollectionSourceOfferPoolCacheKey.Build(canonicalAliasSource, catalogDragonsHero),
    CollectionSourceOfferPoolCacheKey.Build(legacyAliasSource, catalogDragonsHero),
    "BPP-owned offer-pool cache keys should canonicalize source-rule aliases."
);
AssertTrue(
    canonicalAliasSource.OfferRuleFingerprint.Contains(
        TheDragonsHeroId.CanonicalId,
        StringComparison.Ordinal
    ),
    "The Dragons rule fingerprints should store the canonical identity."
);

var unavailableHeroCatalog = BuildCatalog(
    $$"""
    {
      "schemaVersion": 4,
      "groups": ["fixture"],
      "entries": [
        {
          "name": "Mixed",
          "kind": "Merchant",
          "group": "fixture",
          "order": 0,
          "availableHeroes": ["Vanessa", "TheDragons"],
          "description": "Mixed",
          "portraitTemplateId": "77777777-0000-0000-0000-000000000001",
          "sourceTemplateIds": ["77777777-0000-0000-0000-000000000001"],
          "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
        },
        {
          "name": "Unavailable allowlist",
          "kind": "Merchant",
          "group": "fixture",
          "order": 1,
          "availableHeroes": ["TheDragons"],
          "description": "Unavailable",
          "portraitTemplateId": "77777777-0000-0000-0000-000000000002",
          "sourceTemplateIds": ["77777777-0000-0000-0000-000000000002"],
          "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
        },
        {
          "name": "Unavailable fixed hero",
          "kind": "Merchant",
          "group": "fixture",
          "order": 2,
          "availableHeroes": [],
          "description": "Unavailable",
          "portraitTemplateId": "77777777-0000-0000-0000-000000000003",
          "sourceTemplateIds": ["77777777-0000-0000-0000-000000000003"],
          "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "FixedHero", "hero": "TheDragons" } }]
        },
        {
          "name": "Global",
          "kind": "Merchant",
          "group": "fixture",
          "order": 3,
          "availableHeroes": [],
          "description": "Global",
          "portraitTemplateId": "77777777-0000-0000-0000-000000000004",
          "sourceTemplateIds": ["77777777-0000-0000-0000-000000000004"],
          "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
        }
      ]
    }
    """,
    hero => hero != CardHero.TheDragons
);
AssertValues(
    unavailableHeroCatalog.Select(entry => entry.Name).ToArray(),
    new[] { "Mixed", "Global" },
    "Known-but-unavailable heroes should be removed from mixed allowlists, while all-unavailable allowlists and fixed-hero entries are skipped."
);
AssertValues(
    unavailableHeroCatalog.Single(entry => entry.Name == "Mixed").AvailableHeroes.ToArray(),
    new[] { CardHero.Vanessa },
    "Mixed allowlists should retain their resolved heroes."
);
AssertEqual(
    0,
    unavailableHeroCatalog.Single(entry => entry.Name == "Global").AvailableHeroes.Count,
    "A truly empty allowlist should preserve global visibility."
);
AssertThrows<InvalidOperationException>(
    () =>
        BuildCatalog(
            """
            {
              "schemaVersion": 4,
              "groups": ["fixture"],
              "entries": [
                {
                  "name": "Invalid hero",
                  "kind": "Merchant",
                  "group": "fixture",
                  "order": 0,
                  "availableHeroes": ["NotARealHero"],
                  "description": "Invalid",
                  "portraitTemplateId": "77777777-0000-0000-0000-000000000005",
                  "sourceTemplateIds": ["77777777-0000-0000-0000-000000000005"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "SelectedHero" } }]
                }
              ]
            }
            """,
            hero => hero != CardHero.TheDragons
        ),
    "Invalid hero configuration should fail the whole catalog instead of being skipped."
);
AssertThrows<InvalidOperationException>(
    () =>
        BuildHeroFieldCatalog(
            CollectionSourceHeroMode.AllHeroes,
            "TheDragons",
            hero => hero != CardHero.TheDragons
        ),
    "Known-but-unavailable hero fields should skip only FixedHero entries, not relax other rule modes."
);
AssertThrows<InvalidOperationException>(
    () =>
        BuildHeroFieldCatalog(
            CollectionSourceHeroMode.FixedHero,
            "NotARealHero",
            hero => hero != CardHero.TheDragons
        ),
    "Invalid FixedHero configuration should fail instead of being treated as unavailable."
);
AssertThrows<InvalidOperationException>(
    () =>
        BuildHeroValidationCatalog(
            """["TheDragons"]""",
            """[{ "key": "invalid", "kind": "Normal", "rule": { "heroMode": "FixedHero", "hero": "NotARealHero" } }]"""
        ),
    "An all-unavailable explicit allowlist should not mask an invalid hero in its offer rules."
);
AssertThrows<InvalidOperationException>(
    () =>
        BuildHeroValidationCatalog(
            "[]",
            """
            [
              { "key": "unavailable", "kind": "Normal", "rule": { "heroMode": "FixedHero", "hero": "TheDragons" } },
              { "key": "invalid", "kind": "Normal", "rule": { "heroMode": "FixedHero", "hero": "NotARealHero" } }
            ]
            """
        ),
    "An unavailable FixedHero segment should not mask an invalid hero in a later segment."
);
AssertThrows<InvalidOperationException>(
    () =>
        BuildHeroValidationCatalog(
            "[]",
            """
            [
              { "key": "invalid", "kind": "Normal", "rule": { "heroMode": "FixedHero", "hero": "NotARealHero" } },
              { "key": "unavailable", "kind": "Normal", "rule": { "heroMode": "FixedHero", "hero": "TheDragons" } }
            ]
            """
        ),
    "Invalid hero validation should be independent of segment ordering."
);

var currentCatalogJson = TestInputs.Fixture(
    "src/BazaarPlusPlus.Core/Data/CollectionSources/collection-sources.json"
);
var currentCatalog = BuildCatalog(currentCatalogJson);
AssertEqual(
    74,
    currentCatalog.Count,
    "Current source catalog should include the 74 verified sources."
);
AssertEqual(
    51,
    currentCatalog.Count(entry => entry.Kind == CollectionSourceKind.Merchant),
    "Current source catalog should include the 51 verified merchants."
);
AssertEqual(
    23,
    currentCatalog.Count(entry => entry.Kind == CollectionSourceKind.Trainer),
    "Current source catalog should include the 23 verified trainers."
);
AssertTrue(
    currentCatalog.Any(entry =>
        string.Equals(
            entry.SourceKey,
            CollectionPanelSelectionState.DefaultMerchantSourceKey,
            StringComparison.Ordinal
        )
    ),
    "Default selected merchant source key should exist in the current source catalog."
);
var aimbot = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Aimbot", StringComparison.Ordinal)
);
AssertEqual(
    0,
    aimbot.AvailableHeroes.Count,
    "Aimbot is a neutral merchant, so it carries no hero restriction."
);
AssertTrue(
    aimbot.AppliesToHero(catalogDragonsHero)
        && aimbot.AppliesToHero(CardHero.Common)
        && aimbot.AppliesToHero(CardHero.Pygmalien),
    "Aimbot should be available for every hero, including neutral and Pygmalien contexts."
);
var auditedDragonsSources = new[]
{
    "Dooley",
    "Jules",
    "Karnok",
    "Kev's Armory",
    "Shelter Shelby",
    "Uitar Center",
    "Mak",
    "Pygmalien",
    "Stelle",
    "The Tester",
    "Tok's Clocks",
    "Vanessa",
    "Fortis",
    "Regenald",
    "Slohmor Lumbra",
    "Vermir",
    "Zara",
};
foreach (var sourceName in auditedDragonsSources)
{
    AssertTrue(
        currentCatalog.Any(entry =>
            string.Equals(entry.Name, sourceName, StringComparison.Ordinal)
            && entry.AppliesToHero(catalogDragonsHero)
        ),
        $"{sourceName} should be visible for The Dragons after the static-source audit."
    );
}
var dragonsExcludedSources = new[] { "Herma", "Pol", "The Antiquarian" };
foreach (var sourceName in dragonsExcludedSources)
{
    AssertTrue(
        currentCatalog.All(entry =>
            !string.Equals(entry.Name, sourceName, StringComparison.Ordinal)
            || !entry.AppliesToHero(catalogDragonsHero)
        ),
        $"{sourceName} does not schedule The Dragons and should stay hidden for that hero."
    );
}
var dragonsMerchant = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "The Dragons", StringComparison.Ordinal)
);
AssertEqual(
    Guid.Parse("85cdd52b-12c2-4b64-b308-a5788f791b81"),
    dragonsMerchant.PortraitTemplateId,
    "The Dragons merchant should use its stable encounter template id."
);
AssertEqual(
    CollectionSourceHeroMode.FixedHero,
    PrimaryRule(dragonsMerchant).HeroMode,
    "The Dragons merchant should expose the hero's own item pool."
);
AssertEqual(
    catalogDragonsHero,
    PrimaryRule(dragonsMerchant).Hero,
    "The Dragons merchant fixed-hero rule should resolve through the canonical identity."
);
AssertValues(
    dragonsMerchant.AvailableHeroes.ToArray(),
    CollectionHeroSelectionRoster.BaseConcreteHeroes.ToArray(),
    "The Dragons merchant should be visible to the seven other concrete heroes."
);
AssertEqual(
    "other-hero",
    dragonsMerchant.Group,
    "The Dragons merchant should use the established other-hero source group."
);
AssertEqual(
    7,
    dragonsMerchant.Order,
    "The Dragons merchant should follow the seven existing hero merchants."
);
var dragonsOnlyItem = CatalogCard(
    Guid.Parse("eeee4444-0000-0000-0000-000000000001"),
    CardType.Item,
    [catalogDragonsHero]
);
var dragonsSharedItem = CatalogCard(
    Guid.Parse("eeee4444-0000-0000-0000-000000000002"),
    CardType.Item,
    [catalogDragonsHero, CardHero.Vanessa]
);
var vanessaOnlyItem = CatalogCard(
    Guid.Parse("eeee4444-0000-0000-0000-000000000003"),
    CardType.Item,
    [CardHero.Vanessa]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            dragonsMerchant,
            CardHero.Vanessa,
            new[] { dragonsOnlyItem, dragonsSharedItem, vanessaOnlyItem }
        )
        .OfferedCardIds,
    new[] { dragonsOnlyItem.Id, dragonsSharedItem.Id },
    "The Dragons merchant should offer every item that belongs to The Dragons."
);
var mamaBear = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Trainer
    && string.Equals(entry.Name, "Mama Bear", StringComparison.Ordinal)
);
AssertEqual(
    Guid.Parse("386dd351-d08e-49a5-9a38-52c4e73d45b2"),
    mamaBear.PortraitTemplateId,
    "Mama Bear should use the stable level-up trainer template id."
);
AssertValues(
    mamaBear.AvailableHeroes.ToArray(),
    new[] { catalogDragonsHero },
    "Mama Bear should be visible only for The Dragons."
);
AssertEqual(
    catalogDragonsHero,
    PrimaryRule(mamaBear).Hero,
    "Mama Bear should teach skills exclusive to The Dragons."
);
AssertEqual(7, mamaBear.Order, "Mama Bear should follow the seven existing hero trainers.");
var dragonsOnlySkill = CatalogCard(
    Guid.Parse("eeee5555-0000-0000-0000-000000000001"),
    CardType.Skill,
    [catalogDragonsHero]
);
var dragonsSharedSkill = CatalogCard(
    Guid.Parse("eeee5555-0000-0000-0000-000000000002"),
    CardType.Skill,
    [catalogDragonsHero, CardHero.Vanessa]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(mamaBear, catalogDragonsHero, new[] { dragonsOnlySkill, dragonsSharedSkill })
        .OfferedCardIds,
    new[] { dragonsOnlySkill.Id },
    "Mama Bear should match only skills exclusive to The Dragons."
);
var theTester = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "The Tester", StringComparison.Ordinal)
);
AssertValues(
    theTester.AvailableHeroes.ToArray(),
    new[] { CardHero.Dooley, CardHero.Stelle, catalogDragonsHero },
    "The Tester should be visible for Dooley, Stelle, and The Dragons."
);
var testerDooleyTech = CatalogCard(
    Guid.Parse("dddd1111-0000-0000-0000-000000000001"),
    CardType.Item,
    [CardHero.Dooley],
    tags: [CardTag.Tech]
);
var testerStelleTech = CatalogCard(
    Guid.Parse("dddd1111-0000-0000-0000-000000000002"),
    CardType.Item,
    [CardHero.Stelle],
    tags: [CardTag.Tech]
);
var testerVanessaTech = CatalogCard(
    Guid.Parse("dddd1111-0000-0000-0000-000000000003"),
    CardType.Item,
    [CardHero.Vanessa],
    tags: [CardTag.Tech]
);
var testerCommonTech = CatalogCard(
    Guid.Parse("dddd1111-0000-0000-0000-000000000004"),
    CardType.Item,
    [CardHero.Common],
    tags: [CardTag.Tech]
);
var testerDooleyTool = CatalogCard(
    Guid.Parse("dddd1111-0000-0000-0000-000000000005"),
    CardType.Item,
    [CardHero.Dooley],
    tags: [CardTag.Tool]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            theTester,
            CardHero.Dooley,
            new[]
            {
                testerDooleyTech,
                testerStelleTech,
                testerVanessaTech,
                testerCommonTech,
                testerDooleyTool,
            }
        )
        .OfferedCardIds,
    new[] { testerDooleyTech.Id },
    "The Tester should offer only Dooley Tech items when Dooley is selected."
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            theTester,
            CardHero.Stelle,
            new[]
            {
                testerDooleyTech,
                testerStelleTech,
                testerVanessaTech,
                testerCommonTech,
                testerDooleyTool,
            }
        )
        .OfferedCardIds,
    new[] { testerStelleTech.Id },
    "The Tester should offer only Stelle Tech items when Stelle is selected."
);
var privatePitchfork = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Private Pitchfork", StringComparison.Ordinal)
);
AssertEqual(
    Guid.Parse("15b88e74-024e-40a3-a811-aa5810e68ca2"),
    privatePitchfork.PortraitTemplateId,
    "Private Pitchfork should use the live merchant template id as its portrait id."
);
AssertValues(
    privatePitchfork.SourceTemplateIds.ToArray(),
    new[] { Guid.Parse("15b88e74-024e-40a3-a811-aa5810e68ca2") },
    "Private Pitchfork should index its live merchant template id."
);
var pitchforkNeutralItem = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000001"),
    CardType.Item,
    [CardHero.Common],
    tags: [CardTag.Tool]
);
var pitchforkNeutralLoot = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000002"),
    CardType.Item,
    [CardHero.Common],
    tags: [CardTag.Loot]
);
var pitchforkHeroItem = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000003"),
    CardType.Item,
    [CardHero.Vanessa],
    tags: [CardTag.Tool]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            privatePitchfork,
            CardHero.Vanessa,
            new[] { pitchforkNeutralItem, pitchforkNeutralLoot, pitchforkHeroItem }
        )
        .OfferedCardIds,
    new[] { pitchforkNeutralItem.Id },
    "Private Pitchfork should offer neutral non-Loot items only."
);

var stickybeans = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Stickybeans", StringComparison.Ordinal)
);
AssertEqual(
    Guid.Parse("d0276b47-be8a-4bbc-ab55-9f92b352480a"),
    stickybeans.PortraitTemplateId,
    "Stickybeans should use the live merchant template id as its portrait id."
);
AssertValues(
    stickybeans.AvailableHeroes.ToArray(),
    new[]
    {
        CardHero.Vanessa,
        CardHero.Dooley,
        CardHero.Pygmalien,
        CardHero.Karnok,
        CardHero.Mak,
        CardHero.Stelle,
        CardHero.Jules,
        catalogDragonsHero,
    },
    "Stickybeans should be visible for all eight concrete heroes and hidden for Common."
);
AssertTrue(
    stickybeans.AppliesToHero(catalogDragonsHero) && !stickybeans.IsVisibleForHero(CardHero.Common),
    "Stickybeans' Common card identity should not override its ExcludePlayerHero schedule semantics in the neutral rail."
);
AssertEqual(
    8,
    stickybeans.Order,
    "Stickybeans should follow The Dragons after the seven existing hero merchants."
);
var stickybeansCommon = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000001"),
    CardType.Item,
    [CardHero.Common]
);
var stickybeansVanessa = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000002"),
    CardType.Item,
    [CardHero.Vanessa]
);
var stickybeansDooley = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000003"),
    CardType.Item,
    [CardHero.Dooley]
);
var stickybeansSharedOther = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000004"),
    CardType.Item,
    [CardHero.Dooley, CardHero.Stelle]
);
var stickybeansSharedSelected = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000005"),
    CardType.Item,
    [CardHero.Vanessa, CardHero.Dooley]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            stickybeans,
            CardHero.Vanessa,
            new[]
            {
                stickybeansCommon,
                stickybeansVanessa,
                stickybeansDooley,
                stickybeansSharedOther,
                stickybeansSharedSelected,
            }
        )
        .OfferedCardIds,
    new[] { stickybeansDooley.Id, stickybeansSharedOther.Id },
    "Stickybeans should offer non-Common items that do not include the selected UI hero."
);
var stickybeansDragons = CatalogCard(
    Guid.Parse("eeee2222-0000-0000-0000-000000000006"),
    CardType.Item,
    [catalogDragonsHero]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            stickybeans,
            catalogDragonsHero,
            new[] { stickybeansCommon, stickybeansVanessa, stickybeansDragons }
        )
        .OfferedCardIds,
    new[] { stickybeansVanessa.Id },
    "Stickybeans should offer other-hero items for The Dragons while excluding Common and The Dragons items."
);
var aimbotDragonsCrit = CatalogCard(
    Guid.Parse("eeee3333-0000-0000-0000-000000000010"),
    CardType.Item,
    [catalogDragonsHero],
    hiddenTags: [CardHiddenTag.Crit]
);
var aimbotVanessaCrit = CatalogCard(
    Guid.Parse("eeee3333-0000-0000-0000-000000000011"),
    CardType.Item,
    [CardHero.Vanessa],
    hiddenTags: [CardHiddenTag.Crit]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(aimbot, catalogDragonsHero, new[] { aimbotDragonsCrit, aimbotVanessaCrit })
        .OfferedCardIds,
    new[] { aimbotDragonsCrit.Id, aimbotVanessaCrit.Id },
    "Aimbot should preserve its AllHeroes Crit pool when The Dragons is selected."
);
var malafang = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Trainer
    && string.Equals(entry.Name, "Malafang", StringComparison.Ordinal)
);
AssertEqual(
    8,
    malafang.AvailableHeroes.Count,
    "Malafang's trainer schedule should cover all eight concrete heroes."
);
var malafangNeutralBurn = CatalogCard(
    Guid.Parse("eeee3333-0000-0000-0000-000000000001"),
    CardType.Skill,
    [CardHero.Common],
    hiddenTags: [CardHiddenTag.Burn]
);
var malafangDragonsBurn = CatalogCard(
    Guid.Parse("eeee3333-0000-0000-0000-000000000002"),
    CardType.Skill,
    [catalogDragonsHero],
    hiddenTags: [CardHiddenTag.Burn]
);
AssertTrue(
    malafang.AppliesToHero(catalogDragonsHero) && !malafang.IsVisibleForHero(CardHero.Common),
    "Malafang should be visible for The Dragons but hidden in the neutral rail."
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(malafang, catalogDragonsHero, new[] { malafangNeutralBurn, malafangDragonsBurn })
        .OfferedCardIds,
    new[] { malafangDragonsBurn.Id },
    "Malafang should resolve The Dragons Burn skill pool when that hero is selected."
);
AssertEqual(
    currentCatalog.Count,
    currentCatalog.Select(entry => entry.SourceKey).Distinct(StringComparer.Ordinal).Count(),
    "Current source catalog keys should be unique."
);
AssertEqual(
    currentCatalog.SelectMany(entry => entry.SourceTemplateIds).Count(),
    currentCatalog.SelectMany(entry => entry.SourceTemplateIds).Distinct().Count(),
    "Current source template ids should be unique across source entries."
);
var eli = currentCatalog.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Eli", StringComparison.Ordinal)
);
var potionTaggedCard = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000001"),
    CardType.Item,
    [CardHero.Mak],
    tags: [CardTag.Potion]
);
var potionReferenceCard = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000002"),
    CardType.Item,
    [CardHero.Mak],
    hiddenTags: [CardHiddenTag.PotionReference]
);
var nonPotionCard = CatalogCard(
    Guid.Parse("eeee1111-0000-0000-0000-000000000003"),
    CardType.Item,
    [CardHero.Mak],
    tags: [CardTag.Tool]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(eli, CardHero.Mak, new[] { potionTaggedCard, potionReferenceCard, nonPotionCard })
        .OfferedCardIds,
    new[] { potionTaggedCard.Id, potionReferenceCard.Id },
    "Eli should include both Potion-tagged cards and PotionReference cards."
);
foreach (var source in currentCatalog)
{
    AssertTrue(source.PortraitTemplateId != Guid.Empty, $"{source.SourceKey} needs a portrait id.");
    AssertTrue(
        source.SourceTemplateIds.Contains(source.PortraitTemplateId),
        $"{source.SourceKey} should include portraitTemplateId in sourceTemplateIds."
    );
    AssertTrue(source.SourceTemplateIds.Count > 0, $"{source.SourceKey} needs source ids.");
    var primaryRule = PrimaryRule(source);
    if (primaryRule.HeroMode == CollectionSourceHeroMode.FixedHero)
        AssertTrue(primaryRule.Hero.HasValue, $"{source.SourceKey} needs a fixed hero.");
    if (primaryRule.HeroMode == CollectionSourceHeroMode.NeutralOnly)
        AssertFalse(
            primaryRule.Hero.HasValue,
            $"{source.SourceKey} neutral-only rules should not carry hero."
        );
}

var vanessaMerchantRoster = CollectionSourceRoster.Build(
    CollectionSourceCatalog.VisibleEntries(
        currentCatalog,
        CollectionSourceKind.Merchant,
        CardHero.Vanessa
    )
);
AssertNondecreasing(
    vanessaMerchantRoster.Select(item => item.Entry.GroupDisplayIndex).ToArray(),
    "Merchant roster should be sorted by declared group display order."
);
AssertValues(
    FirstGroupLayerCounts(vanessaMerchantRoster, 4),
    new[] { 3, 6, 6, 8 },
    "The visible Vanessa merchant roster should add The Dragons to the other-hero layer."
);
AssertValues(
    vanessaMerchantRoster.Where(item => item.BreakAfter).Select(item => item.Entry.Group).ToArray(),
    new[] { "generalist", "tier-specialist" },
    "Merchant roster should force row breaks only after generalist and tier layers."
);
var vanessaTrainerRoster = CollectionSourceRoster.Build(
    CollectionSourceCatalog.VisibleEntries(
        currentCatalog,
        CollectionSourceKind.Trainer,
        CardHero.Vanessa
    )
);
AssertValues(
    vanessaTrainerRoster.Take(6).Select(item => item.Entry.Name).ToArray(),
    new[] { "Old Zane", "Nufu", "Pip", "Argenta", "Orlin", "Adira" },
    "Trainer roster should start with the current hero's trainer, then Nufu and tier trainers."
);
AssertEqual(
    0,
    vanessaTrainerRoster.Count(item => item.BreakAfter),
    "Trainer roster should not force row breaks because its first row self-aligns to six chips."
);
var neutralOtherHeroSources = CollectionSourceCatalog
    .VisibleEntries(currentCatalog, CollectionSourceKind.Merchant, CardHero.Common)
    .Where(entry => entry.Group == "other-hero")
    .ToArray();
AssertEqual(
    0,
    neutralOtherHeroSources.Length,
    "The neutral source rail should hide fixed-hero merchants and Stickybeans."
);
var dragonsOtherHeroSources = CollectionSourceRoster
    .Build(
        CollectionSourceCatalog
            .VisibleEntries(currentCatalog, CollectionSourceKind.Merchant, catalogDragonsHero)
            .Where(entry => entry.Group == "other-hero")
    )
    .Select(item => item.Entry.Name)
    .ToArray();
AssertValues(
    dragonsOtherHeroSources,
    new[] { "Vanessa", "Dooley", "Pygmalien", "Karnok", "Mak", "Stelle", "Jules", "Stickybeans" },
    "The Dragons source rail should show the seven other hero merchants followed by Stickybeans."
);
var vanessaOtherHeroSources = CollectionSourceRoster
    .Build(
        CollectionSourceCatalog
            .VisibleEntries(currentCatalog, CollectionSourceKind.Merchant, CardHero.Vanessa)
            .Where(entry => entry.Group == "other-hero")
    )
    .Select(item => item.Entry.Name)
    .ToArray();
AssertValues(
    vanessaOtherHeroSources,
    new[]
    {
        "Dooley",
        "Pygmalien",
        "Karnok",
        "Mak",
        "Stelle",
        "Jules",
        "The Dragons",
        "Stickybeans",
    },
    "An existing hero's source rail should insert The Dragons after the seven legacy hero merchants and before Stickybeans."
);
var dragonsTrainerRoster = CollectionSourceRoster.Build(
    CollectionSourceCatalog.VisibleEntries(
        currentCatalog,
        CollectionSourceKind.Trainer,
        catalogDragonsHero
    )
);
AssertValues(
    dragonsTrainerRoster.Take(2).Select(item => item.Entry.Name).ToArray(),
    new[] { "Mama Bear", "Nufu" },
    "The Dragons trainer rail should start with Mama Bear and then the global trainer sequence."
);
AssertFalse(
    CollectionSourceCatalog
        .VisibleEntries(currentCatalog, CollectionSourceKind.Trainer, CardHero.Common)
        .Any(entry => entry.Name == "Malafang"),
    "The neutral trainer rail should hide Malafang because its schedule requires a concrete player hero."
);
AssertFalse(
    CollectionSourceCatalog
        .VisibleEntries(currentCatalog, CollectionSourceKind.Trainer, CardHero.Common)
        .Any(entry => entry.Name == "Mama Bear"),
    "The neutral trainer rail should hide Mama Bear."
);

var openOutsideRun = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: false,
    currentHero: CardHero.Dooley,
    currentEncounterTemplateId: vanessaAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries
);
AssertValues(
    CollectionHeroSelectionRoster.BaseConcreteHeroes,
    new[]
    {
        CardHero.Vanessa,
        CardHero.Dooley,
        CardHero.Pygmalien,
        CardHero.Karnok,
        CardHero.Mak,
        CardHero.Stelle,
        CardHero.Jules,
    },
    "The neutral/Common context should not render a Common hero chip."
);
AssertEqual(
    CollectionPanelSelectionState.Default,
    openOutsideRun,
    "Opening outside an in-game run should fall back to VAN + Jay Jay."
);
AssertTrue(
    TheDragonsHeroIdentity.TryResolve(
        TheDragonsHeroIdentity.CanonicalId,
        out var loadingDragonsHero
    ),
    "The integrated identity adapter should resolve The Dragons for loading preference coverage."
);
AssertTrue(
    HeroPortraitSpriteProvider.IsRenderableHero(loadingDragonsHero),
    "The shared portrait provider should attempt the native default-skin chain for The Dragons."
);
AssemblyLoadContext.Default.LoadFromAssemblyPath(
    Path.Combine(AppContext.BaseDirectory, "UnityEngine.CoreModule.dll")
);
var degradedDragonsPortrait = await HeroPortraitSpriteProvider.LoadDefaultPortraitAsync(
    loadingDragonsHero
);
AssertTrue(
    degradedDragonsPortrait?.Reason == HeroPortraitFailureReason.CollectionManagerUnavailable,
    "The shared portrait provider should return a degraded outcome instead of throwing when native services are unavailable."
);
AssertFalse(
    HeroPortraitSpriteProvider.TryGetCached(loadingDragonsHero, out _),
    "A pre-readiness native service failure should not poison the shared portrait cache."
);
var dragonsTooltip = CollectionPanelText.Hero(
    CardHero.TheDragons,
    DesktopCardFacets.TheDragonsDisplayName
);
AssertFalse(
    dragonsTooltip.Contains("Hero8", StringComparison.OrdinalIgnoreCase),
    "The Collection hero tooltip should never expose the transitional enum placeholder."
);
AssertTrue(
    dragonsTooltip.Contains("Dragons", StringComparison.OrdinalIgnoreCase),
    "The Collection hero tooltip should resolve The Dragons through the shared identity adapter."
);
var openWhileCatalogLoads = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: false,
    currentHero: null,
    currentEncounterTemplateId: null,
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries,
    rememberedPreference: CollectionPanelHeroPreference.ResolveStored(
        hasStoredValue: true,
        raw: "TheDragons",
        CollectionCatalogReadiness.Loading,
        CollectionHeroSelectionRoster.BaseConcreteHeroes
    )
);
AssertEqual(
    (CardHero?)CardHero.TheDragons,
    openWhileCatalogLoads.SelectedHero,
    "Opening while the catalog loads should preserve a known concrete preference until an accepted roster can normalize it."
);
var openOutsideRunWithRememberedHero = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: false,
    currentHero: CardHero.Dooley,
    currentEncounterTemplateId: vanessaAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries,
    rememberedPreference: CollectionPanelHeroPreference.ResolveStored(
        hasStoredValue: true,
        raw: "Mak",
        CollectionCatalogReadiness.Accepted,
        CollectionHeroSelectionRoster.BaseConcreteHeroes
    )
);
AssertEqual(
    new CollectionPanelSelectionState(
        CardHero.Mak,
        CollectionPanelSelectionState.DefaultMerchantSourceKey,
        CollectionSourceKind.Merchant
    ),
    openOutsideRunWithRememberedHero,
    "Opening outside a run should use the remembered explicit CollectionPanel hero."
);

var openOutsideRunWithRememberedCommon = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: false,
    currentHero: CardHero.Dooley,
    currentEncounterTemplateId: vanessaAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries,
    rememberedPreference: CollectionPanelHeroPreference.ResolveStored(
        hasStoredValue: true,
        raw: "Common",
        CollectionCatalogReadiness.Accepted,
        CollectionHeroSelectionRoster.BaseConcreteHeroes
    )
);
AssertEqual(
    new CollectionPanelSelectionState(
        null,
        CollectionPanelSelectionState.DefaultMerchantSourceKey,
        CollectionSourceKind.Merchant
    ),
    openOutsideRunWithRememberedCommon,
    "Opening outside a run should preserve Common as a remembered hero."
);
var openOutsideRunWithUnavailableHero = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: false,
    currentHero: null,
    currentEncounterTemplateId: null,
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries,
    rememberedPreference: CollectionPanelHeroPreference.ResolveStored(
        hasStoredValue: true,
        raw: "TheDragons",
        CollectionCatalogReadiness.Accepted,
        CollectionHeroSelectionRoster.BaseConcreteHeroes
    )
);
AssertEqual(
    new CollectionPanelSelectionState(
        null,
        CollectionPanelSelectionState.DefaultMerchantSourceKey,
        CollectionSourceKind.Merchant
    ),
    openOutsideRunWithUnavailableHero,
    "A known-but-unavailable preference should fall back to neutral for only the current session."
);
var openOutsideRunWithInvalidPreference = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: false,
    currentHero: null,
    currentEncounterTemplateId: null,
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries,
    rememberedPreference: CollectionPanelHeroPreference.ResolveStored(
        hasStoredValue: true,
        raw: "NotARealHero",
        CollectionCatalogReadiness.Accepted,
        CollectionHeroSelectionRoster.BaseConcreteHeroes
    )
);
AssertEqual(
    CollectionPanelSelectionState.Default,
    openOutsideRunWithInvalidPreference,
    "An invalid preference should behave like an absent first-open preference after deletion."
);
var dooleyAila = entries.Single(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Aila", StringComparison.Ordinal)
    && entry.AvailableHeroes.Contains(CardHero.Dooley)
);
var openOnCurrentMerchant = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: CardHero.Dooley,
    currentEncounterTemplateId: dooleyAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries
);
AssertEqual(
    new CollectionPanelSelectionState(
        CardHero.Dooley,
        dooleyAila.SourceKey,
        CollectionSourceKind.Merchant
    ),
    openOnCurrentMerchant,
    "Opening during a run should select the current concrete hero and merchant source."
);
var openInRunIgnoresRememberedHero = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: CardHero.Dooley,
    currentEncounterTemplateId: dooleyAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries,
    rememberedPreference: CollectionPanelHeroPreference.ResolveStored(
        hasStoredValue: true,
        raw: "Mak",
        CollectionCatalogReadiness.Accepted,
        CollectionHeroSelectionRoster.BaseConcreteHeroes
    )
);
AssertEqual(
    new CollectionPanelSelectionState(
        CardHero.Dooley,
        dooleyAila.SourceKey,
        CollectionSourceKind.Merchant
    ),
    openInRunIgnoresRememberedHero,
    "Opening during a run should keep the current run hero and source over a remembered hero."
);
var pygTrainer = entries.Single(entry =>
    entry.Kind == CollectionSourceKind.Trainer
    && string.Equals(entry.Name, "Professor Riggs", StringComparison.Ordinal)
);
var openOnCurrentTrainer = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: CardHero.Pygmalien,
    currentEncounterTemplateId: pygTrainer.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries
);
AssertEqual(
    new CollectionPanelSelectionState(
        CardHero.Pygmalien,
        pygTrainer.SourceKey,
        CollectionSourceKind.Trainer
    ),
    openOnCurrentTrainer,
    "Opening during a run at a trainer should select the trainer source and Skill tab."
);
var globalNufu = entries.First(entry =>
    entry.Kind == CollectionSourceKind.Merchant
    && string.Equals(entry.Name, "Nufu", StringComparison.Ordinal)
    && PrimaryRule(entry).HeroMode == CollectionSourceHeroMode.SelectedHero
);
var openOnChoiceMerchant = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: CardHero.Vanessa,
    currentEncounterTemplateId: null,
    choiceSelectionTemplateIds: new[] { globalMerchantId, pygTrainerId },
    entries
);
AssertEqual(
    new CollectionPanelSelectionState(
        CardHero.Vanessa,
        globalNufu.SourceKey,
        CollectionSourceKind.Merchant
    ),
    openOnChoiceMerchant,
    "Opening on the choice screen should select the first matching source in SelectionSet order."
);
var openOnChoiceTrainer = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: CardHero.Pygmalien,
    currentEncounterTemplateId: null,
    choiceSelectionTemplateIds: new[] { pygTrainerId, globalMerchantId },
    entries
);
AssertEqual(
    new CollectionPanelSelectionState(
        CardHero.Pygmalien,
        pygTrainer.SourceKey,
        CollectionSourceKind.Trainer
    ),
    openOnChoiceTrainer,
    "Opening on a trainer choice should select the trainer source when it is the first visible match."
);
var openWithoutMerchant = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: CardHero.Vanessa,
    currentEncounterTemplateId: pygTrainerId,
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries
);
AssertEqual(
    new CollectionPanelSelectionState(
        CardHero.Vanessa,
        CollectionPanelSelectionState.DefaultMerchantSourceKey,
        CollectionSourceKind.Merchant
    ),
    openWithoutMerchant,
    "Opening during a run without a usable source should keep the run hero and fall back to Jay Jay."
);
var openWithoutConcreteHero = CollectionPanelOpenSelectionResolver.Resolve(
    isInGameRun: true,
    currentHero: CardHero.Common,
    currentEncounterTemplateId: dooleyAila.SourceTemplateIds[0],
    choiceSelectionTemplateIds: Array.Empty<Guid>(),
    entries
);
AssertEqual(
    CollectionPanelSelectionState.Default,
    openWithoutConcreteHero,
    "Opening during a run without a concrete hero should fall back to VAN + Jay Jay."
);

var commonCacheKey = CollectionSourceOfferPoolCacheKey.Build(vanessaAila, CardHero.Common);
AssertTrue(
    commonCacheKey.StartsWith(vanessaAila.SourceKey + "|", StringComparison.Ordinal),
    "Source offer cache key should include the stable source key."
);
AssertTrue(
    commonCacheKey.Contains(ailaId1.ToString("N")[..12], StringComparison.Ordinal)
        && commonCacheKey.Contains(ailaId2.ToString("N")[..12], StringComparison.Ordinal),
    "Source offer cache key should include a fingerprint of all source template ids."
);
AssertTrue(
    commonCacheKey.Contains(vanessaAila.OfferRuleFingerprint, StringComparison.Ordinal),
    "Source offer cache key should include the v4 source rule fingerprint."
);
AssertTrue(
    commonCacheKey.EndsWith("|Common", StringComparison.Ordinal),
    "Neutral source offer cache keys should use the effective Common hero."
);
var heroCacheKey = CollectionSourceOfferPoolCacheKey.Build(vanessaAila, CardHero.Vanessa);
AssertFalse(
    string.Equals(heroCacheKey, commonCacheKey, StringComparison.Ordinal),
    "Source offer cache key should distinguish Common from concrete heroes."
);
AssertTrue(
    CollectionSourceOfferPoolCacheKey
        .Build(vanessaAila, CardHero.TheDragons)
        .EndsWith("|TheDragons", StringComparison.Ordinal),
    "BPP-owned source cache keys should use the canonical The Dragons identity."
);

var visibleForVanessa = CollectionSourceCatalog
    .VisibleEntries(entries, CollectionSourceKind.Merchant, CardHero.Vanessa)
    .Select(entry => entry.Name)
    .ToArray();
AssertValues(
    visibleForVanessa,
    new[] { "Aila", "Nufu", "Nufu" },
    "Visible merchant sources should include selected hero sources plus global sources."
);
var visibleForCommon = CollectionSourceCatalog
    .VisibleEntries(entries, CollectionSourceKind.Merchant, CardHero.Common)
    .Select(entry => entry.Name)
    .ToArray();
AssertValues(
    visibleForCommon,
    new[] { "Nufu", "Nufu" },
    "Common should behave like a selected hero and show only global merchant sources."
);
var visibleTrainers = CollectionSourceCatalog
    .VisibleEntries(entries, CollectionSourceKind.Trainer, CardHero.Vanessa)
    .ToArray();
AssertEqual(
    0,
    visibleTrainers.Length,
    "Item and Skill source selectors should stay kind-specific."
);

var selectedHeroRule = BuildSingleEntry(
    "Selected Hero",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "SelectedHero" }"""
);
var selectedHeroCards = new[]
{
    CatalogCard(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
        CardType.Item,
        [CardHero.Vanessa]
    ),
    CatalogCard(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
        CardType.Item,
        [CardHero.Common]
    ),
    CatalogCard(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
        CardType.Item,
        [CardHero.Dooley]
    ),
};
var selectedHeroResult = CollectionSourceOfferPoolResolver.Resolve(
    selectedHeroRule,
    CardHero.Vanessa,
    selectedHeroCards
);
AssertEqual(
    CollectionSourceOfferPoolStatus.Ready,
    selectedHeroResult.Status,
    "SelectedHero rules should resolve without runtime fallback."
);
AssertSet(
    selectedHeroResult.OfferedCardIds,
    new[] { selectedHeroCards[0].Id },
    "SelectedHero rules should match only the selected concrete hero, not Common cards."
);
var selectedCommonResult = CollectionSourceOfferPoolResolver.Resolve(
    selectedHeroRule,
    CardHero.Common,
    selectedHeroCards
);
AssertSet(
    selectedCommonResult.OfferedCardIds,
    new[] { selectedHeroCards[1].Id },
    "SelectedHero rules should match only Common cards when Common is selected."
);
var selectedHeroDisabledResult = CollectionSourceOfferPoolResolver.Resolve(
    selectedHeroRule,
    CardHero.Common,
    selectedHeroCards
);
AssertSet(
    selectedHeroDisabledResult.OfferedCardIds,
    new[] { selectedHeroCards[1].Id },
    "SelectedHero rules should resolve neutral mode against the effective Common hero."
);
var selectedHeroTrainerRule = BuildSingleEntry(
    "Selected Hero Trainer",
    CollectionSourceKind.Trainer,
    """{ "heroMode": "SelectedHero" }"""
);
var selectedHeroTrainerCards = new[]
{
    CatalogCard(
        Guid.Parse("aaaaaaaa-1000-0000-0000-000000000001"),
        CardType.Skill,
        [CardHero.Vanessa]
    ),
    CatalogCard(
        Guid.Parse("aaaaaaaa-1000-0000-0000-000000000002"),
        CardType.Skill,
        [CardHero.Vanessa, CardHero.Dooley]
    ),
    CatalogCard(
        Guid.Parse("aaaaaaaa-1000-0000-0000-000000000003"),
        CardType.Skill,
        [CardHero.Common]
    ),
    CatalogCard(
        Guid.Parse("aaaaaaaa-1000-0000-0000-000000000004"),
        CardType.Skill,
        Array.Empty<CardHero>()
    ),
};
var selectedHeroTrainerPool = CollectionSourceOfferPoolResolver.Resolve(
    selectedHeroTrainerRule,
    CardHero.Vanessa,
    selectedHeroTrainerCards
);
AssertSet(
    selectedHeroTrainerPool.OfferedCardIds,
    new[] { selectedHeroTrainerCards[0].Id, selectedHeroTrainerCards[1].Id },
    "Source pools may include shared selected-hero skills; final Skill filtering owns exclusivity."
);
var noneSelectedResult = CollectionSourceOfferPoolResolver.Resolve(
    source: null,
    effectiveHero: CardHero.Vanessa,
    selectedHeroCards
);
AssertEqual(
    CollectionSourceOfferPoolStatus.NoneSelected,
    noneSelectedResult.Status,
    "Resolver should report NoneSelected when no source chip is active."
);

var fixedHeroEntry = BuildSingleEntry(
    "Fixed Jules",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "FixedHero", "hero": "Jules" }"""
);
var fixedHeroCards = new[]
{
    CatalogCard(
        Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
        CardType.Item,
        [CardHero.Jules]
    ),
    CatalogCard(
        Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"),
        CardType.Item,
        [CardHero.Dooley]
    ),
};
var fixedHeroResult = CollectionSourceOfferPoolResolver.Resolve(
    fixedHeroEntry,
    CardHero.Dooley,
    fixedHeroCards
);
AssertSet(
    fixedHeroResult.OfferedCardIds,
    new[] { fixedHeroCards[0].Id },
    "FixedHero rules should use the source hero, not the selected UI hero."
);
var fixedHeroTrainerEntry = BuildSingleEntry(
    "Fixed Vanessa Trainer",
    CollectionSourceKind.Trainer,
    """{ "heroMode": "FixedHero", "hero": "Vanessa" }"""
);
var fixedHeroTrainerCards = new[]
{
    CatalogCard(
        Guid.Parse("bbbbbbbb-1000-0000-0000-000000000001"),
        CardType.Skill,
        [CardHero.Vanessa]
    ),
    CatalogCard(
        Guid.Parse("bbbbbbbb-1000-0000-0000-000000000002"),
        CardType.Skill,
        [CardHero.Vanessa, CardHero.Dooley]
    ),
    CatalogCard(
        Guid.Parse("bbbbbbbb-1000-0000-0000-000000000003"),
        CardType.Skill,
        [CardHero.Dooley]
    ),
};
var fixedHeroTrainerResult = CollectionSourceOfferPoolResolver.Resolve(
    fixedHeroTrainerEntry,
    CardHero.Dooley,
    fixedHeroTrainerCards
);
AssertSet(
    fixedHeroTrainerResult.OfferedCardIds,
    new[] { fixedHeroTrainerCards[0].Id },
    "FixedHero trainer rules should teach only skills exclusive to the fixed hero."
);

var otherHeroesEntry = BuildSingleEntry(
    "Other Heroes",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "OtherHeroes" }"""
);
AssertEqual(
    0,
    CollectionSourceCatalog
        .VisibleEntries(new[] { otherHeroesEntry }, CollectionSourceKind.Merchant, CardHero.Common)
        .Count(),
    "OtherHeroes-only sources should be hidden in the neutral/Common context."
);
var otherHeroCards = new[]
{
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"),
        CardType.Item,
        [CardHero.Common]
    ),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000002"),
        CardType.Item,
        [CardHero.Vanessa]
    ),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000003"),
        CardType.Item,
        [CardHero.Dooley]
    ),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000004"),
        CardType.Item,
        [CardHero.Dooley, CardHero.Stelle]
    ),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000005"),
        CardType.Item,
        [CardHero.Vanessa, CardHero.Dooley]
    ),
};
var otherHeroesForVanessa = CollectionSourceOfferPoolResolver.Resolve(
    otherHeroesEntry,
    CardHero.Vanessa,
    otherHeroCards
);
AssertSet(
    otherHeroesForVanessa.OfferedCardIds,
    new[] { otherHeroCards[2].Id, otherHeroCards[3].Id },
    "OtherHeroes rules should exclude Common cards and cards that include the selected UI hero."
);
var otherHeroesWithoutSelectedHero = CollectionSourceOfferPoolResolver.Resolve(
    otherHeroesEntry,
    CardHero.Common,
    otherHeroCards
);
AssertSet(
    otherHeroesWithoutSelectedHero.OfferedCardIds,
    Array.Empty<Guid>(),
    "OtherHeroes rules should return no cards in the neutral/Common context."
);

var allHeroEntry = BuildSingleEntry(
    "All Crit",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "AllHeroes", "hiddenTagsAny": ["Crit"] }"""
);
var allHeroCards = new[]
{
    CatalogCard(
        Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
        CardType.Item,
        [CardHero.Vanessa],
        hiddenTags: [CardHiddenTag.Crit]
    ),
    CatalogCard(
        Guid.Parse("cccccccc-0000-0000-0000-000000000002"),
        CardType.Item,
        [CardHero.Dooley],
        hiddenTags: [CardHiddenTag.Crit]
    ),
    CatalogCard(
        Guid.Parse("cccccccc-0000-0000-0000-000000000003"),
        CardType.Item,
        [CardHero.Dooley]
    ),
};
var allHeroResult = CollectionSourceOfferPoolResolver.Resolve(
    allHeroEntry,
    CardHero.Vanessa,
    allHeroCards
);
AssertSet(
    allHeroResult.OfferedCardIds,
    allHeroCards.Take(2).Select(card => card.Id).ToArray(),
    "AllHeroes rules should ignore the selected UI hero and still apply other predicates."
);

var neutralEntry = BuildSingleEntry(
    "Neutral Bronze",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "NeutralOnly", "startingTier": { "mode": "AtMost", "tier": "Bronze" } }"""
);
var neutralCards = new[]
{
    CatalogCard(
        Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
        CardType.Item,
        [CardHero.Common],
        tier: CardTier.Bronze
    ),
    CatalogCard(
        Guid.Parse("dddddddd-0000-0000-0000-000000000002"),
        CardType.Item,
        [CardHero.Common],
        tier: CardTier.Silver
    ),
    CatalogCard(
        Guid.Parse("dddddddd-0000-0000-0000-000000000003"),
        CardType.Item,
        [CardHero.Vanessa],
        tier: CardTier.Bronze
    ),
};
var neutralResult = CollectionSourceOfferPoolResolver.Resolve(
    neutralEntry,
    CardHero.Vanessa,
    neutralCards
);
AssertSet(
    neutralResult.OfferedCardIds,
    new[] { neutralCards[0].Id },
    "NeutralOnly rules should require Common cards and still enforce starting tier."
);

// NeutralOnly ignores the selected UI hero entirely: it returns Common/neutral cards
// regardless of which concrete hero (or none) is selected. This is an INTENTIONAL,
// documented divergence from the pre-migration description switch, which ANDed the UI
// hero filter and returned an empty pool when a concrete hero was selected (the sole
// NeutralOnly source, Curio). See the design doc "NeutralOnly migration note (Curio)".
var neutralHeroEntry = BuildSingleEntry(
    "Neutral Hero Invariance",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "NeutralOnly" }"""
);
var neutralCommonCard = CatalogCard(
    Guid.Parse("d0000000-0000-0000-0000-000000000001"),
    CardType.Item,
    [CardHero.Common]
);
var neutralVanessaCard = CatalogCard(
    Guid.Parse("d0000000-0000-0000-0000-000000000002"),
    CardType.Item,
    [CardHero.Vanessa]
);
var neutralHeroCards = new[] { neutralCommonCard, neutralVanessaCard };
var neutralWithVanessa = CollectionSourceOfferPoolResolver
    .Resolve(neutralHeroEntry, CardHero.Vanessa, neutralHeroCards)
    .OfferedCardIds;
AssertSet(
    neutralWithVanessa,
    new[] { neutralCommonCard.Id },
    "NeutralOnly must return only Common cards and exclude the selected concrete hero's card "
        + "(intentional divergence from the old description switch, which returned empty)."
);
var neutralWithNoHero = CollectionSourceOfferPoolResolver
    .Resolve(neutralHeroEntry, CardHero.Common, neutralHeroCards)
    .OfferedCardIds;
AssertSet(
    neutralWithNoHero,
    neutralWithVanessa.ToArray(),
    "NeutralOnly output must be invariant to the effective hero (Common vs Vanessa)."
);
var neutralWithOtherHero = CollectionSourceOfferPoolResolver
    .Resolve(neutralHeroEntry, CardHero.Dooley, neutralHeroCards)
    .OfferedCardIds;
AssertSet(
    neutralWithOtherHero,
    neutralWithVanessa.ToArray(),
    "NeutralOnly output must be invariant to the selected UI hero (Dooley vs Vanessa)."
);

var exactTierEntry = BuildSingleEntry(
    "Exact Gold",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "AllHeroes", "startingTier": { "mode": "Exact", "tier": "Gold" } }"""
);
var exactTierCards = new[]
{
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"),
        CardType.Item,
        [CardHero.Common],
        tier: CardTier.Silver
    ),
    CatalogCard(
        Guid.Parse("eeeeeeee-0000-0000-0000-000000000002"),
        CardType.Item,
        [CardHero.Common],
        tier: CardTier.Gold
    ),
};
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(exactTierEntry, CardHero.Vanessa, exactTierCards)
        .OfferedCardIds,
    new[] { exactTierCards[1].Id },
    "Exact starting-tier rules should not behave like AtMost."
);

var complexEntry = BuildSingleEntry(
    "Complex",
    CollectionSourceKind.Merchant,
    """
    {
      "heroMode": "AllHeroes",
      "sizesAny": ["Small"],
      "tagsAny": ["Tool"],
      "tagsNone": ["Weapon"],
      "hiddenTagsAny": ["Burn"],
      "enchantableOnly": true
    }
    """
);
var complexCards = new[]
{
    CatalogCard(
        Guid.Parse("ffffffff-0000-0000-0000-000000000001"),
        CardType.Item,
        [CardHero.Common],
        tags: [CardTag.Tool],
        hiddenTags: [CardHiddenTag.Burn],
        size: CardSize.Small,
        isEnchantable: true
    ),
    CatalogCard(
        Guid.Parse("ffffffff-0000-0000-0000-000000000002"),
        CardType.Item,
        [CardHero.Common],
        tags: [CardTag.Tool, CardTag.Weapon],
        hiddenTags: [CardHiddenTag.Burn],
        size: CardSize.Small,
        isEnchantable: true
    ),
    CatalogCard(
        Guid.Parse("ffffffff-0000-0000-0000-000000000003"),
        CardType.Item,
        [CardHero.Common],
        tags: [CardTag.Tool],
        hiddenTags: [CardHiddenTag.Burn],
        size: CardSize.Medium,
        isEnchantable: true
    ),
};
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(complexEntry, CardHero.Vanessa, complexCards)
        .OfferedCardIds,
    new[] { complexCards[0].Id },
    "Structured rules should AND size, tag include/exclude, hidden tag, and enchantable predicates."
);

var segmentedEntry = BuildEntryWithSegments(
    "Segmented",
    CollectionSourceKind.Merchant,
    """
    [
      {
        "key": "normal-tool",
        "kind": "Normal",
        "rule": { "heroMode": "SelectedHero", "tagsAny": ["Tool"] }
      },
      {
        "key": "rare-turbo",
        "kind": "Enchanted",
        "rarityLabel": "Rare",
        "rule": { "heroMode": "SelectedHero", "enchantmentTypesAny": ["Turbo"] }
      }
    ]
    """
);
var segmentedNormal = CatalogCard(
    Guid.Parse("11111111-aaaa-0000-0000-000000000001"),
    CardType.Item,
    [CardHero.Vanessa],
    tags: [CardTag.Tool]
);
var segmentedEnchanted = CatalogCard(
    Guid.Parse("11111111-aaaa-0000-0000-000000000002"),
    CardType.Item,
    [CardHero.Vanessa],
    enchantments: Enchantments(CardEnchantment.Turbo)
);
var segmentedBoth = CatalogCard(
    Guid.Parse("11111111-aaaa-0000-0000-000000000003"),
    CardType.Item,
    [CardHero.Vanessa],
    tags: [CardTag.Tool],
    enchantments: Enchantments(CardEnchantment.Turbo)
);
var segmentedExcluded = CatalogCard(
    Guid.Parse("11111111-aaaa-0000-0000-000000000004"),
    CardType.Item,
    [CardHero.Dooley],
    tags: [CardTag.Tool],
    enchantments: Enchantments(CardEnchantment.Turbo)
);
var segmentedResult = CollectionSourceOfferPoolResolver.Resolve(
    segmentedEntry,
    CardHero.Vanessa,
    new[] { segmentedNormal, segmentedEnchanted, segmentedBoth, segmentedExcluded }
);
AssertSet(
    segmentedResult.OfferedCardIds,
    new[] { segmentedNormal.Id, segmentedEnchanted.Id, segmentedBoth.Id },
    "Segments should OR together while each segment keeps its own AND predicates."
);
AssertEqual(
    CollectionSourceOfferSegmentKind.Normal,
    segmentedResult.OfferMatchesByCardId[segmentedNormal.Id][0].SegmentKind,
    "Normal-only matches should retain the normal segment attribution."
);
AssertEqual(
    CardEnchantment.Turbo,
    segmentedResult.OfferMatchesByCardId[segmentedEnchanted.Id][0].EnchantmentType,
    "Enchanted matches should retain the matched enchantment type."
);
AssertEqual(
    2,
    segmentedResult.OfferMatchesByCardId[segmentedBoth.Id].Count,
    "A card that matches both normal and enchanted segments should keep both match reasons."
);

var hiddenGroupEntry = BuildSingleEntry(
    "Hidden Group",
    CollectionSourceKind.Merchant,
    """{ "heroMode": "AllHeroes", "hiddenTagGroupsAny": ["Crit"] }"""
);
var hiddenGroupCard = CatalogCard(
    Guid.Parse("11111111-bbbb-0000-0000-000000000001"),
    CardType.Item,
    [CardHero.Dooley],
    hiddenTags: [CardHiddenTag.CritReference]
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(hiddenGroupEntry, CardHero.Vanessa, new[] { hiddenGroupCard })
        .OfferedCardIds,
    new[] { hiddenGroupCard.Id },
    "hiddenTagGroupsAny should expand base/reference hidden tags during catalog parsing."
);

var enchantmentFacetEntry = BuildEntryWithSegments(
    "Enchantment Facets",
    CollectionSourceKind.Merchant,
    """
    [
      {
        "key": "rare-tagged",
        "kind": "Enchanted",
        "rarityLabel": "Rare",
        "rule": {
          "heroMode": "SelectedHero",
          "enchantmentTagsAny": ["Weapon"],
          "enchantmentHiddenTagsAny": ["Haste"]
        }
      }
    ]
    """
);
var enchantmentFacetMatch = CatalogCard(
    Guid.Parse("11111111-cccc-0000-0000-000000000001"),
    CardType.Item,
    [CardHero.Vanessa],
    enchantments: EnchantmentWithFacets(
        CardEnchantment.Turbo,
        new[] { CardTag.Weapon },
        new[] { CardHiddenTag.Haste }
    )
);
var enchantmentFacetMiss = CatalogCard(
    Guid.Parse("11111111-cccc-0000-0000-000000000002"),
    CardType.Item,
    [CardHero.Vanessa],
    enchantments: EnchantmentWithFacets(
        CardEnchantment.Turbo,
        new[] { CardTag.Weapon },
        new[] { CardHiddenTag.Slow }
    )
);
AssertSet(
    CollectionSourceOfferPoolResolver
        .Resolve(
            enchantmentFacetEntry,
            CardHero.Vanessa,
            new[] { enchantmentFacetMatch, enchantmentFacetMiss }
        )
        .OfferedCardIds,
    new[] { enchantmentFacetMatch.Id },
    "enchantmentTagsAny and enchantmentHiddenTagsAny should match the same TEnchantment facets."
);

var specialistEnchantments = new Dictionary<string, CardEnchantment>
{
    ["Knightshade"] = CardEnchantment.Toxic,
    ["Hef"] = CardEnchantment.Fiery,
    ["Chronos"] = CardEnchantment.Turbo,
    ["Freiya"] = CardEnchantment.Icy,
    ["Cobweb"] = CardEnchantment.Heavy,
};
foreach (var pair in specialistEnchantments)
{
    var specialist = currentCatalog.Single(entry =>
        entry.Kind == CollectionSourceKind.Merchant
        && string.Equals(entry.Name, pair.Key, StringComparison.Ordinal)
    );
    AssertEqual(
        2,
        specialist.OfferSegments.Count,
        $"{pair.Key} should expose normal and rare enchanted source segments."
    );
    var enchantedSegment = specialist.OfferSegments.Single(segment =>
        segment.Kind == CollectionSourceOfferSegmentKind.Enchanted
    );
    AssertValues(
        enchantedSegment.Rule.EnchantmentTypesAny.ToArray(),
        new[] { pair.Value },
        $"{pair.Key} rare segment should use the expected enchantment type."
    );

    var candidate = CatalogCard(
        GuidFromIndex(pair.Key, 99),
        CardType.Item,
        [CardHero.Vanessa],
        enchantments: Enchantments(pair.Value)
    );
    var specialistResult = CollectionSourceOfferPoolResolver.Resolve(
        specialist,
        CardHero.Vanessa,
        new[] { candidate }
    );
    AssertSet(
        specialistResult.OfferedCardIds,
        new[] { candidate.Id },
        $"{pair.Key} rare segment should match by TCardItem.Enchantments keys without base hidden tags."
    );
    AssertEqual(
        pair.Value,
        specialistResult.OfferMatchesByCardId[candidate.Id][0].EnchantmentType,
        $"{pair.Key} rare match should carry the matched enchantment type."
    );
}

foreach (var sourceEntry in currentCatalog)
{
    var selectedHero = RepresentativeSelectedHero(sourceEntry);
    var match = MatchingCard(sourceEntry, selectedHero, GuidFromIndex(sourceEntry.SourceKey, 1));
    var nonMatch = MatchingCard(
        sourceEntry,
        selectedHero ?? CardHero.Common,
        GuidFromIndex(sourceEntry.SourceKey, 2),
        sourceEntry.Kind == CollectionSourceKind.Merchant ? CardType.Skill : CardType.Item
    );
    var probePool = CollectionSourceOfferPoolResolver.Resolve(
        sourceEntry,
        selectedHero ?? CardHero.Common,
        new[] { match, nonMatch }
    );
    AssertEqual(
        CollectionSourceOfferPoolStatus.Ready,
        probePool.Status,
        $"Current source catalog entry should resolve as Ready: {sourceEntry.SourceKey}."
    );
    AssertTrue(
        probePool.OfferedCardIds.Contains(match.Id),
        $"Representative match should be included for {sourceEntry.SourceKey}."
    );
    AssertFalse(
        probePool.OfferedCardIds.Contains(nonMatch.Id),
        $"Representative non-match should be excluded for {sourceEntry.SourceKey}."
    );
}

Console.WriteLine("Collection source filtering checks passed.");

static CollectionSourceEntry BuildSingleEntry(
    string name,
    CollectionSourceKind kind,
    string offerRuleJson
)
{
    return BuildEntryWithSegments(
        name,
        kind,
        $$"""[{ "key": "normal", "kind": "Normal", "rule": {{offerRuleJson}} }]"""
    );
}

static CollectionSourceEntry BuildDragonsAliasEntry(
    string heroAlias,
    Func<CardHero, bool> isHeroAvailable
)
{
    const string sourceId = "77777777-0000-0000-0000-000000000006";
    return BuildCatalog(
            $$"""
            {
              "schemaVersion": 4,
              "groups": ["fixture"],
              "entries": [
                {
                  "name": "Alias source",
                  "kind": "Merchant",
                  "group": "fixture",
                  "order": 0,
                  "availableHeroes": ["{{heroAlias}}"],
                  "description": "Alias fixture",
                  "portraitTemplateId": "{{sourceId}}",
                  "sourceTemplateIds": ["{{sourceId}}"],
                  "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "FixedHero", "hero": "{{heroAlias}}" } }]
                }
              ]
            }
            """,
            isHeroAvailable
        )
        .Single();
}

static IReadOnlyList<CollectionSourceEntry> BuildCatalog(
    string json,
    Func<CardHero, bool>? isHeroAvailable = null
) => CollectionSourceCatalog.Build(json, isHeroAvailable ?? DesktopCardFacets.IsHeroAvailable);

static CollectionSourceHeroParseResult ParseHero(
    string? value,
    Func<CardHero, bool>? isAvailable = null
) => CollectionSourceHeroParser.Parse(value, isAvailable ?? DesktopCardFacets.IsHeroAvailable);

static IReadOnlyList<CollectionSourceEntry> BuildHeroFieldCatalog(
    CollectionSourceHeroMode heroMode,
    string hero,
    Func<CardHero, bool> isHeroAvailable
)
{
    const string sourceId = "77777777-0000-0000-0000-000000000007";
    return BuildCatalog(
        $$"""
        {
          "schemaVersion": 4,
          "groups": ["fixture"],
          "entries": [
            {
              "name": "Hero field",
              "kind": "Merchant",
              "group": "fixture",
              "order": 0,
              "availableHeroes": [],
              "description": "Hero field fixture",
              "portraitTemplateId": "{{sourceId}}",
              "sourceTemplateIds": ["{{sourceId}}"],
              "offerSegments": [{ "key": "normal", "kind": "Normal", "rule": { "heroMode": "{{heroMode}}", "hero": "{{hero}}" } }]
            }
          ]
        }
        """,
        isHeroAvailable
    );
}

static IReadOnlyList<CollectionSourceEntry> BuildHeroValidationCatalog(
    string availableHeroesJson,
    string offerSegmentsJson
)
{
    const string sourceId = "77777777-0000-0000-0000-000000000008";
    return BuildCatalog(
        $$"""
        {
          "schemaVersion": 4,
          "groups": ["fixture"],
          "entries": [
            {
              "name": "Validation order",
              "kind": "Merchant",
              "group": "fixture",
              "order": 0,
              "availableHeroes": {{availableHeroesJson}},
              "description": "Validation order fixture",
              "portraitTemplateId": "{{sourceId}}",
              "sourceTemplateIds": ["{{sourceId}}"],
              "offerSegments": {{offerSegmentsJson}}
            }
          ]
        }
        """,
        hero => hero != CardHero.TheDragons
    );
}

static CollectionSourceEntry BuildEntryWithSegments(
    string name,
    CollectionSourceKind kind,
    string offerSegmentsJson
)
{
    var id = GuidFromIndex(name, 7);
    return BuildCatalog(
            $$"""
            {
              "schemaVersion": 4,
              "groups": ["fixture"],
              "entries": [
                {
                  "name": "{{name}}",
                  "kind": "{{kind}}",
                  "group": "fixture",
                  "order": 0,
                  "availableHeroes": [],
                  "description": "{{name}} fixture",
                  "portraitTemplateId": "{{id}}",
                  "sourceTemplateIds": ["{{id}}"],
                  "offerSegments": {{offerSegmentsJson}}
                }
              ]
            }
            """
        )
        .Single();
}

static CardHero? RepresentativeSelectedHero(CollectionSourceEntry entry)
{
    if (PrimaryRule(entry).HeroMode != CollectionSourceHeroMode.SelectedHero)
        return CardHero.Vanessa;
    if (entry.AvailableHeroes.Count > 0)
        return entry.AvailableHeroes[0];
    return CardHero.Vanessa;
}

static CollectionCardVm MatchingCard(
    CollectionSourceEntry entry,
    CardHero? selectedHero,
    Guid id,
    CardType? cardType = null
)
{
    var rule = PrimaryRule(entry);
    var tags = rule.TagsAny.Count > 0 ? new[] { rule.TagsAny[0] } : Array.Empty<CardTag>();
    if (rule.TagsNone.Contains(CardTag.Tool) && tags.Length == 0)
        tags = new[] { CardTag.Food };
    else if (tags.Length == 0)
        tags = new[] { CardTag.Tool };

    return new CollectionCardVm
    {
        Id = id,
        Type =
            cardType
            ?? (entry.Kind == CollectionSourceKind.Merchant ? CardType.Item : CardType.Skill),
        Size = rule.SizesAny.Count > 0 ? rule.SizesAny[0] : CardSize.Medium,
        StartingTier = rule.StartingTier?.Tier ?? CardTier.Bronze,
        Heroes = rule.HeroMode switch
        {
            CollectionSourceHeroMode.SelectedHero => selectedHero.HasValue
                ? new[] { selectedHero.Value }
                : new[] { CardHero.Dooley },
            CollectionSourceHeroMode.FixedHero => new[] { rule.Hero!.Value },
            CollectionSourceHeroMode.NeutralOnly => new[] { CardHero.Common },
            _ => new[] { CardHero.Dooley },
        },
        Tags = tags,
        HiddenTags =
            rule.HiddenTagsAny.Count > 0
                ? new[] { rule.HiddenTagsAny[0] }
                : Array.Empty<CardHiddenTag>(),
        DisplayName = id.ToString("N"),
        InternalName = id.ToString("N"),
        ArtKey = id.ToString("N"),
        IsEnchantable = rule.EnchantableOnly,
        Enchantments = rule.EnchantableOnly
            ? Enchantments(CardEnchantment.Toxic)
            : new Dictionary<CardEnchantment, CollectionCardEnchantmentFacets>(),
    };
}

static CollectionSourceOfferRule PrimaryRule(CollectionSourceEntry entry) =>
    entry.OfferSegments[0].Rule;

static CollectionCardVm CatalogCard(
    Guid id,
    CardType type,
    IEnumerable<CardHero> heroes,
    IEnumerable<CardTag>? tags = null,
    IEnumerable<CardHiddenTag>? hiddenTags = null,
    CardSize size = CardSize.Medium,
    CardTier tier = CardTier.Bronze,
    bool isEnchantable = false,
    IReadOnlyDictionary<CardEnchantment, CollectionCardEnchantmentFacets>? enchantments = null
) =>
    new()
    {
        Id = id,
        Type = type,
        Size = size,
        StartingTier = tier,
        Heroes = heroes.ToArray(),
        Tags = tags?.ToArray() ?? Array.Empty<CardTag>(),
        HiddenTags = hiddenTags?.ToArray() ?? Array.Empty<CardHiddenTag>(),
        DisplayName = id.ToString("N"),
        InternalName = id.ToString("N"),
        ArtKey = id.ToString("N"),
        IsEnchantable = isEnchantable || (enchantments != null && enchantments.Count > 0),
        Enchantments =
            enchantments
            ?? (
                isEnchantable
                    ? Enchantments(CardEnchantment.Toxic)
                    : new Dictionary<CardEnchantment, CollectionCardEnchantmentFacets>()
            ),
    };

static IReadOnlyDictionary<CardEnchantment, CollectionCardEnchantmentFacets> Enchantments(
    params CardEnchantment[] types
)
{
    var result = new Dictionary<CardEnchantment, CollectionCardEnchantmentFacets>();
    foreach (var type in types)
        result[type] = new CollectionCardEnchantmentFacets(
            type,
            Array.Empty<CardTag>(),
            Array.Empty<CardHiddenTag>()
        );
    return result;
}

static IReadOnlyDictionary<CardEnchantment, CollectionCardEnchantmentFacets> EnchantmentWithFacets(
    CardEnchantment type,
    IReadOnlyCollection<CardTag> tags,
    IReadOnlyCollection<CardHiddenTag> hiddenTags
) =>
    new Dictionary<CardEnchantment, CollectionCardEnchantmentFacets>
    {
        [type] = new(type, tags, hiddenTags),
    };

static Guid GuidFromIndex(string seed, int index)
{
    var bytes = System.Text.Encoding.UTF8.GetBytes(seed + "|" + index);
    var buffer = new byte[16];
    for (var i = 0; i < bytes.Length; i++)
        buffer[i % buffer.Length] = (byte)(buffer[i % buffer.Length] ^ bytes[i]);
    return new Guid(buffer);
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

static int[] FirstGroupLayerCounts(IReadOnlyList<CollectionSourceRosterItem> roster, int layerCount)
{
    var result = new List<int>(layerCount);
    string? currentGroup = null;
    var count = 0;
    foreach (var item in roster)
    {
        if (currentGroup == null)
        {
            currentGroup = item.Entry.Group;
            count = 1;
            continue;
        }

        if (string.Equals(currentGroup, item.Entry.Group, StringComparison.Ordinal))
        {
            count++;
            continue;
        }

        result.Add(count);
        if (result.Count == layerCount)
            break;
        currentGroup = item.Entry.Group;
        count = 1;
    }

    if (result.Count < layerCount && currentGroup != null)
        result.Add(count);

    return result.ToArray();
}

static void AssertNondecreasing(IReadOnlyList<int> actual, string message)
{
    for (var i = 1; i < actual.Count; i++)
    {
        if (actual[i] < actual[i - 1])
            throw new InvalidOperationException(
                $"{message} Value at {i} ({actual[i]}) is less than value at {i - 1} ({actual[i - 1]})."
            );
    }
}

static void AssertSet<T>(
    IReadOnlyCollection<T> actual,
    IReadOnlyCollection<T> expected,
    string message
)
{
    var actualSet = new HashSet<T>(actual);
    var expectedSet = new HashSet<T>(expected);
    if (!actualSet.SetEquals(expectedSet))
        throw new InvalidOperationException(
            $"{message} Expected [{string.Join(", ", expectedSet)}], got [{string.Join(", ", actualSet)}]."
        );
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
