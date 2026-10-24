# ADR-0015: BPP-owned card facets, mapped by name in each host

Status: Accepted

## Context

ADR-0012 moves into Core only what compiles without game types. Collection and most of History use the game's card enums as their data model, not in passing: `EHero`, `ETier`, `ECardSize`, `ECardType`, `ECardTag`, `EHiddenTag`, and `EEnchantmentType`. The desktop host binds the Mono enums in `BazaarGameShared`. The Android host binds the interop copies in `Il2CppBazaarGameShared`, which have the same member names and values but are different CLR types. Neither type can cross into Core. The game also renamed a member during a transition: one hero is `Hero8` in some builds and `TheDragons` in others (`TheDragonsHeroId`). Issue #367 §2.3 asks for BPP-owned snapshots at the Core boundary.

## Decision

1. Core owns one enum per card property in `src/BazaarPlusPlus.Core/Cards/`: `CardHero`, `CardTier`, `CardSize`, `CardType`, `CardTag`, `CardHiddenTag`, and `CardEnchantment`, plus the two placements a captured PvP card records, `CardSection` and `CardSocket`. Their member names match the game's members, except that the hero is `CardHero.TheDragons` under either game name. `CardFacetOrder` owns the visible tier and size order, the board's socket order, and the player-facing tag order. `CardReferenceTags` owns which reference hidden tag points at which base tag.
2. Each host maps its game enums in one adapter, `DesktopCardFacets` or `AndroidCardFacets`. The adapter instantiates the Core `CardFacetNameMap<TGame, TFacet>` once per enum, and the map pairs members by name, with an alias table for `Hero8`. Neither host writes a per-member table. A game member with no facet maps to nothing: it is dropped from a card's sets, and a card whose type, size, or tier is unmapped is left out of the catalog. Answers that only the game build has, such as whether a hero exists, reach Core through small ports: `isHeroAvailable` for the source catalog and `ICollectionDayTierSource` for the Day gate.
3. All nine facets are closed enums, `CardHiddenTag` included even though it has 98 members. Every Core consumer of hidden tags is itself a closed BPP list (the keyword whitelist, the hidden-tag groups, the reference-tag table, and `Package`), so an open string type would gain nothing and would add a second mapping style. Vocabulary drift is caught by pin tests over both snapshots. `CollectionCardProjection.Tests` checks the desktop snapshot. `AndroidCardFacetVocabularyTests` reads the Interop Snapshot's metadata. Each fails naming the game member that has no facet.
4. A facet's integer value belongs to BPP. It is never persisted, uploaded, or cast to or from a game value. The persisted form of a facet is its member name, which equals the game's name and, for heroes, the canonical id already stored in SQLite rows and uploads. This decision does not change the wire shape of Bundle V6 or of the Run segment. `RunPayloadV6` keeps the game's integers for card `Type` and `Size` and the game's names for `Tier` and `Tags`. When a writer of those fields moves onto facets, the host adapter converts back to the game's value. Changing that shape is a separate contract change, together with its consumers.

   The PvP battle model (`PvpBattleCardSnapshot`) carries facets, and its stored forms keep names. A `battle_snapshots` document stores each facet's member name; a card the game gave no size (its unnamed value 0) stores `"0"`, reads back with no size, and reaches the Bundle as size 0 (`PvpBattleSnapshotsCodec`, `PvpBattleCardDocument.NoSize`). A Bundle card read back into a Ghost manifest maps its integers through the host's names (`IPvpBattleEvidenceBinding.TryCardEnumName`), with size 0 again a card without a size. Ghost and favorite payload files store every enum of their manifest by member name (`MessagePackGzipCodec`'s `enumsAsNames`), so a file written with integers does not decode.

## Guardrails

- Do not add `Unknown = 0` or mirror the game's integers in a facet: a zero member leaks into facet availability and the filter rows. Amended 2026-10-24: the combat facets copy the game's values, for the reason ADR-0019 gives in item 3.
- A host adds no switch from game enum to facet. Extend the alias table when the game renames a member, and add the facet member when the pin test names a new game member.
- Core code that needs a game-build answer takes a port; it never reflects on the game's enums.

## Relationship to ADR-0012

This decision adds no new boundary. It supplies the BPP-owned snapshot types that ADR-0012's guardrail requires before game-enum-typed features can move. Collection is the first consumer. History, LiveBuild, and the Android Collection panel reuse the same facets and adapters. ADR-0017 narrows this decision for item boards: sockets stay positional indexes, attributes stay game member names, and an unmapped tier or size renders instead of being left out.

## Evidence

- Facets and the map: `src/BazaarPlusPlus.Core/Cards/`
- Adapters: `src/BazaarPlusPlus/GameInterop/Cards/DesktopCardFacets.cs`, `src/BazaarPlusPlus.Android/GameInterop/AndroidCardFacets.cs`
- Desktop projection: `CollectionCardVmProjection` in `src/BazaarPlusPlus/Game/CollectionPanel/Data/`
- Map behavior and the Collection core: `tests/CollectionFilterEngine.Tests/`, `tests/CollectionViewState.Tests/` (both reference Core alone, no host source)
- Vocabulary pins: `tests/CollectionCardProjection.Tests/`, `tests/AndroidHost.Tests/AndroidCardFacetVocabularyTests.cs`
- PvP battle model and its stored forms: `src/BazaarPlusPlus.Core/Game/PvpBattles/PvpBattleSnapshotsCodec.cs`; the size-less card through the database and the Bundle in `tests/BundlePipeline.Tests/BattleSnapshotShapeTests.cs`; payload files by name in `tests/GhostBattleSync.Tests/PayloadFacetNameTests.cs`
