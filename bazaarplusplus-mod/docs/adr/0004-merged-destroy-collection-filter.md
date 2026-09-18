# ADR-0004: One Destroy chip covers the full destroy-mechanic cluster

Status: Accepted

## Decision

The Collection Panel exposes one `CollectionMechanic.Destroy` flag and one primary-section chip. A card matches when its base template contains any of these facts:

| Surface | Match |
| --- | --- |
| Action | `TActionCardDestroy`, `TActionCardRepair`, or `TActionCardTransformDestroyed`, including under `TActionAnd` |
| Trigger | `TTriggerOnBeforeCardDestroyed`, `TTriggerOnCardDestroyed`, or `TTriggerOnCardPerformedDestruction`, including under `TTriggerOr` |
| Hidden tag | `EHiddenTag.AbsorbDestroy`, mapped into Destroy rather than exposed as a keyword chip |
| Attribute | Positive base `ECardAttributeType.DestroyImmunity` at any supported tier |

The projection excludes `TTriggerOnCardRepaired`, abilities spawned by `TActionCardTransformDestroyed`, enchantment-provided facts, text matches, and inactive abilities absent from every tier's `AbilityIds`. Product copy stays on the game's native Destroy label; no second “Destroy Related” chip exists.

## Why

Players search for the whole destruction mechanic, not only active `TActionCardDestroy` effects. Splitting reactions, repair, replacement, absorb, and immunity into another chip would divide one mental category and make Any/All combinations necessary.

## Guardrails

- Project once at the catalog VM boundary through [`CollectionCardVm.From`](../../src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionCardVm.From.cs) and [`CollectionMechanicFacts.Project`](../../src/BazaarPlusPlus/Game/CollectionPanel/Data/CollectionMechanicFacts.cs). Filtering and refreshes consume the cached flag; architecture tests keep effect-graph types off those paths ([tests](../../tests/Architecture.Tests/CollectionMechanicArchitectureTests.cs)).
- Traverse active actions and triggers once per card, with base attributes in the tier loop.
- Keep `AbsorbDestroy` off `CollectionKeywordWhitelist.Ordered`; its hidden-tag mapping feeds only the Destroy mechanic.
- Reclassifying `TTriggerOnCardRepaired` or splitting the cluster requires a new product decision.

Executable semantics live in [`CollectionFilterEngine.Tests`](../../tests/CollectionFilterEngine.Tests/Program.cs).
