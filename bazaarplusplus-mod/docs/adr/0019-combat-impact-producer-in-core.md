# ADR-0019: The Combat Impact producer in Core, on a BPP combat timeline

Status: Accepted

## Context

ADR-0008 kept the Combat Impact producer (`CombatImpactProjector`, `PeriodicEffectAttribution`, `CombatImpactTransformLineage`, and the `CombatImpactEntity` it reads) in the desktop host because it read `BazaarGameShared` types: the `CombatSim` message, its events and targets, eleven combat enums, and the attribute-modifier value graph. Only the desktop could run it, and its tests needed the Managed snapshot. Issue #417 asks for the producer in Core under ADR-0015's rule: BPP vocabulary, a name map per host, and pin tests against both hosts' enums.

## Decision

1. Core owns the combat vocabulary in `src/BazaarPlusPlus.Core/Combat/`. Eleven enums mirror the game's member names: `CombatActionType`, `CombatCardAttribute`, `CombatPlayerAttribute`, `Combatant`, `CombatCardStat`, `CombatHealthChange`, `CombatAttributeOperation`, `CombatDamageType`, `CombatListComparison`, `CombatEffectPriority`, and `CombatValueModifierMode`. `CombatTimeline` copies the replay: its frames, the events the producer reads, the health and attribute updates, and the card stats. `CombatValue` and `CombatAttributeModifier` copy the modifier value graph. The value kinds that attribution reads are copied whole. The other reference kinds are copied as `CombatOpaqueReference`, which keeps only the kind, and anything else becomes `CombatUnsupportedValue`. `CombatValueModifier.GetModifiedValue` repeats the game's `TValueModifier` arithmetic.
2. The producer and `CombatImpactEntity` live in Core and read only Core types. The desktop host keeps only adapters. `DesktopCombatTimeline` copies a `CombatSim`. `DesktopCombatFacets` maps the eleven enums through ADR-0015's `CardFacetNameMap`. The entity readers over game templates (`CombatImpactEntitySnapshotReader`, `CombatImpactAttributionRuleReader`, and the other readers in `src/BazaarPlusPlus/Game/PostCombatImpact/Data/`) write Core facets. An event or action with no Core member is dropped from the timeline, as are the game's condition observations (`CombatSimEventEffectTriggered`), which no rule reads.
3. Unlike ADR-0015's card facets, the combat facets copy the game's integer values. The producer keys transition claims by `(int)` of an attribute, side by side with `CombatImpactKind` values. It also orders sets and transitions by enum value, and its diagnostics carry attribute integers. Copying the values keeps every claim key and every order the same as before, which keeps the Combat Impact goldens byte-identical. The values still belong to BPP. No code casts between a combat facet and a game enum, and no value is persisted or sent.
4. The pin tests check names and values in both directions. `DesktopCombatFacetVocabularyTests` in `tests/CombatImpactAdapter.Tests/` checks the Managed snapshot. `AndroidCombatFacetVocabularyTests` in `tests/AndroidHost.Tests/` checks the Interop Snapshot's metadata. The Android host adds its own name map when it ports Post-Combat Impact. The pin already guarantees that the map will pair every member by name.

## Consequences

- `tests/PostCombatImpact.Tests/` references Core alone and runs in `mod::test-core`. The tests that decode game MessagePack fixtures or read game templates move to `tests/CombatImpactAdapter.Tests/`, which tests the compiled host.
- `tests/CombatImpact.Corpus/` still decodes game payloads and reads the game catalog, so it stays a host lane. It projects through `DesktopCombatTimeline`. Its evidence still names each modifier value by the game's value type.
- A game member Core lacks fails a pin test. Add the Core member with the game's value; do not add a switch to the adapter.

## Relationship to other decisions

This decision replaces ADR-0008's producer boundary, which said the producer stays per host. It extends ADR-0015 with combat facets and narrows that ADR's guardrail against mirroring the game's integers, for the reason given in item 3. Combat attributes are a closed enum, not ADR-0017's name-keyed bag, because the producer reads them.

## Evidence

- Vocabulary and timeline: `src/BazaarPlusPlus.Core/Combat/`
- Producer: `src/BazaarPlusPlus.Core/Game/PostCombatImpact/Data/`
- Desktop adapters: `src/BazaarPlusPlus/GameInterop/CombatSimulation/DesktopCombatTimeline.cs`, `src/BazaarPlusPlus/GameInterop/CombatSimulation/DesktopCombatFacets.cs`
- Tests: `tests/PostCombatImpact.Tests/` (Core only), `tests/CombatImpactAdapter.Tests/`, `tests/AndroidHost.Tests/AndroidCombatFacetVocabularyTests.cs`, and the corpus golden in `tests/ScenarioRunner.Tests/CombatImpactCorpusTests.cs`
