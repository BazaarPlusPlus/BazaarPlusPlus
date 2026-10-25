# ADR-0003: Preserve behavior-specific boundaries; reject cosmetic unification

Status: Accepted

## Decision

Share a seam only when its consumers share behavior, ownership, and lifecycle. Preserve these deliberate boundaries:

- `IGameStateProbe` and `IRunContext` remain separate active seams; configuration is the concrete `BppSettings`.
- HistoryPanel replay, health, account-link, and ghost-sync operations retain separate handlers because their cancellation, rollback, and status semantics differ. `HistoryPanelCoordinator` remains their single mutable-state owner; showing the panel resets replay/account-link state while preserving in-flight ghost sync and health probes ([coordinator](../../src/BazaarPlusPlus.Core/Game/HistoryPanel/HistoryPanelCoordinator.cs)). Amended 2026-10-25: the coordinator also owns the return from a replay the panel started, behind `HistoryPanelReplayReturnPort`. It arms the return before it hides the panel, brings the panel back once no saved replay session remains and the host's lobby is active (a null runtime keeps it waiting), and resumes the selection on that show. It also dispatches the delete and favorite buttons by section. The cancel triggers stay per host on purpose, because they follow what each host can do: the desktop cancels only when the panel's object disables, Android on switch-off and on every close but the replay's own.
- Enchant-section normalization preserves multiline content; quest-reward normalization trims and joins lines. Each keeps its own formatter ([enchant](../../src/BazaarPlusPlus/Game/ItemEnchantPreview/ItemEnchantPreviewFormatting.cs), [quest](../../src/BazaarPlusPlus/Patches/Tooltips/QuestRewardPreviewTooltipPatch.cs)).
- Collection facet availability is recomputed beside catalog publication in `CollectionViewState`, not attached to catalog results or repeated on refresh ([state](../../src/BazaarPlusPlus.Core/Game/CollectionPanel/CollectionViewState.cs)).
- Feature and mountable order follows explicit dependency evidence. Registration order and reverse teardown are already pinned by executable tests ([feature tests](../../tests/CompositionRuntime.Tests/BppFeatureRegistryTests.cs), [mountable tests](../../tests/CompositionRuntime.Tests/BppMountableRegistryTests.cs)).
- Mod API endpoints share bounded response parsing, request correlation, and retry extraction, while Bundle, Ghost, BazaarDB, and health retain their own outcome types and product mappings.

## Why

These surfaces look similar but fail, recover, and publish state differently. A generic wrapper would move those differences into configuration or introduce an abstraction with no second behavioral consumer.

## Guardrail

Reopen one boundary at a time, with code evidence that its behavior and ownership now match the proposed peer. Visual repetition alone is not evidence.
