# ADR-0017: Item boards in Core, with socket indexes and attribute member names

Status: Accepted

## Context

History and LiveBuild render the same item board: a row of up to ten sockets holding item cards, planned by `BppItemBoardSlotPlanner` and drawn by the desktop host's native board adapter. The board's card stored five game enums. ADR-0015 already gives three of them a facet: `ETier`, `ECardSize`, and `EEnchantmentType`. The other two have none. `EContainerSocketId` has ten members, `Socket_0` to `Socket_9`. `ECardAttributeType` has 91, and the game adds more. Core could not hold the board, so the LiveBuild panel state could not move into Core either (issue #366), and the Android host (#397) could not reuse it.

## Decision

1. The item board (`BppItemBoard`, `BppItemBoardCard`, and the slot planner) and the live card snapshot (`LiveCardSnapshot`, `LiveCardSnapshotSet`) live in `src/BazaarPlusPlus.Core/Cards/`. Their tier, size, enchantment, and hero are ADR-0015 facets. The LiveBuild panel state (`LiveBuildPanelState`), its recommendation repository, and its text move to Core on top of them.
2. A socket is an `int` index from 0 to 9. A socket is a position on the ten-socket board, not a property of a card, so it is not a facet. The integer is the position itself. It is not a mirror of the game's enum values, which ADR-0015's guardrail forbids. Each host converts the index at its boundary. A captured PvP card records where it sat as ADR-0015's `CardSocket` facet; History's board reader turns that into the index through `CardFacetOrder.SocketRank`.
3. Attributes are a bag keyed by the game's member name (`IReadOnlyDictionary<string, int>`). No Core code reads an attribute: the bag passes from a host's reader to that host's native mapper, which parses the names back and drops any name it does not know. A closed facet would turn every new game attribute into a pin-test failure for data Core never looks at. The stored `battle_snapshots` attributes and `PvpBattleCardDocument.Attributes` already use the same name-keyed form. ADR-0015 §3 rejected an open string type for hidden tags because Core consumes them through closed lists; attributes have no such consumer.
4. A board card whose tier or size has no facet still renders, as Bronze or as one Small socket. ADR-0015 leaves such a card out of the catalog, but a board shows recorded data: a captured PvP card the game gave no size (its unnamed value 0) has no `CardSize` (ADR-0015), and before this change those cards rendered with a span of 1. An enchantment without a facet reads as none, and a hero without a facet reads as no run.

## Consequences

- The desktop mappers (`BppItemBoardPreviewMapper` and `NativeMonsterBoardItemMapper`) and the readers (`HistoryBattlePreviewProjection` and `LiveCardSnapshotReader`) stay in the host. They convert through `DesktopCardFacets`.
- The LiveBuild and History preview tests reference Core instead of compiling host board sources.
- The Android host builds its own reader and native mapper over the same Core types.
- Amended 2026-10-25: each host's reader keeps only its game walk, a `LiveCardWalk` in facet terms. Core's `LiveCardSnapshotRead` places the cards (the slot planner's fit rule), applies item 4's fallbacks, numbers them, and reports the cards it leaves out on the host's own event, so the fit rule, the fallbacks, and the degraded report are written once.

## Relationship to ADR-0015

This decision narrows ADR-0015 for boards. It adds no facet. It makes positional sockets and opaque attribute names explicit exceptions to "Core speaks facets", and it replaces the leave-it-out rule with a render fallback for board cards.

## Evidence

- Core types: `src/BazaarPlusPlus.Core/Cards/ItemBoards/`, `src/BazaarPlusPlus.Core/Cards/LiveCards/`, `src/BazaarPlusPlus.Core/Game/LiveBuildPanel/`
- Live read outcome: `src/BazaarPlusPlus.Core/Cards/LiveCards/LiveCardSnapshotRead.cs`
- Desktop boundary: `src/BazaarPlusPlus/GameInterop/ItemBoardPreview/BppItemBoardPreviewMapper.cs`, `src/BazaarPlusPlus/GameInterop/LiveCards/LiveCardSnapshotReader.cs`, `src/BazaarPlusPlus/Game/HistoryPanel/Data/HistoryBattlePreviewProjection.cs`
- Tests: `tests/LiveBuildPanel.Tests/`, `tests/LiveBuildRecommendationCore.Tests/` (Core only), `tests/HistoryPanelPreview.Tests/` (planner plus the desktop mappers)
