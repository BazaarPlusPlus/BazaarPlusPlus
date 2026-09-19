# ADR-0003: Preserve behavior-specific boundaries; reject cosmetic unification

Status: Accepted

## Decision

Share a seam only when its consumers share behavior, ownership, and lifecycle. Preserve these deliberate boundaries:

- `IGameStateProbe`, `IRunContext`, and `IBppConfig` remain separate active seams; `IGameStateProbe` also has a live CombatStatusBar test adapter ([test seam](../../tests/CombatStatusBarState.Tests/TestCombatStatusBarShims.cs), [tests](../../tests/CombatStatusBarState.Tests/CombatStatusBarStateTests.cs)).
- HistoryPanel replay, health, account-link, and ghost-sync operations retain separate handlers because their cancellation, rollback, and status semantics differ. `HistoryPanelCoordinator` remains their single mutable-state owner; showing the panel resets replay/account-link state while preserving in-flight ghost sync and health probes ([coordinator](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelCoordinator.cs)).
- `HistoryPanelDependencies` keeps one guard-free constructor because process-isolated scenario capsules use positional nulls as behavior anchors ([dependencies](../../src/BazaarPlusPlus/Game/HistoryPanel/HistoryPanelDependencies.cs)).
- Enchant-section normalization preserves multiline content; quest-reward normalization trims and joins lines. Each keeps its own formatter ([enchant](../../src/BazaarPlusPlus/Game/ItemEnchantPreview/ItemEnchantPreviewFormatting.cs), [quest](../../src/BazaarPlusPlus/Patches/Tooltips/QuestRewardPreviewTooltipPatch.cs)).
- Collection facet availability is recomputed beside catalog publication in `CollectionViewState`, not attached to catalog results or repeated on refresh ([state](../../src/BazaarPlusPlus/Game/CollectionPanel/CollectionViewState.cs)).
- Feature and mountable order follows explicit dependency evidence. Registration order and reverse teardown are already pinned by executable tests ([feature tests](../../tests/CompositionRuntime.Tests/BppFeatureRegistryTests.cs), [mountable tests](../../tests/CompositionRuntime.Tests/BppMountableRegistryTests.cs)).
- Mod API endpoints share bounded response parsing, request correlation, and retry extraction, while Bundle, Ghost, BazaarDB, and health retain their own outcome types and product mappings.

## Why

These surfaces look similar but fail, recover, and publish state differently. A generic wrapper would move those differences into configuration or introduce an abstraction with no second behavioral consumer.

## Guardrail

Reopen one boundary at a time, with code evidence that its behavior and ownership now match the proposed peer. Visual repetition alone is not evidence.
