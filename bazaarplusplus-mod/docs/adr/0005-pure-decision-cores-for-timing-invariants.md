# ADR-0005: Timing invariants live in pure decision cores, not MonoBehaviour glue

Status: Accepted

## Context

Runtime orchestration used to hold race-safety and timing state inside `MonoBehaviour` callbacks. The behavior was difficult to exercise without Unity, while source-shape assertions could only prove that tokens appeared in an expected order.

## Decision

Put orchestration state algebra in pure decision cores. A core accepts observations and relative time, owns transitions, and returns decisions; the runtime translates those decisions into Unity, I/O, and publication effects.

A pure core is effect-free, not necessarily dependency-free. Use the smallest real domain dependency and keep Unity types, held delegates, clocks, files, codecs, and runtime objects outside. Do not invent mirror enums solely to make a source file compile without game assemblies.

## Load-bearing guardrails

- [`SavedReplayLifecycle`](../../src/BazaarPlusPlus/Game/CombatReplay/SavedReplayLifecycle.cs) commits replay start in three stages, decides exit in two phases around the ended publication, and owns one 15-second suppression latch shared by programmatic and native exits. Pending menu return and start progress are parallel states. `CombatReplayRuntime.TryContinueReplay` remains the only programmatic replay exit under ADR-0002.
- [`CollectionViewState`](../../src/BazaarPlusPlus/Game/CollectionPanel/CollectionViewState.cs) returns `null` for a same-value no-op. Catalog-driven hero normalization does not write the user's preference; grid unavailability does not trigger normalization write-back or cache invalidation; the run day used by open selection is captured when the panel opens.
- [`BackgroundUploadPump`](../../src/BazaarPlusPlus/Game/Upload/BackgroundUploadPump.cs) has a two-point shutdown: release arm subscriptions first, then dispose the feed session after the drain callback. This keeps in-flight attempts away from disposed resources.
- [`BundleSealConvergence`](../../src/BazaarPlusPlus/Game/BundlePipeline/BundleSealConvergence.cs) receives relative time and input facts and returns continue, wait, degradation, or terminal decisions. Storage parses persisted UTC time and the coordinator performs the one relative-time translation.
- [`EndOfRunCleanFramePreparationCore`](../../src/BazaarPlusPlus/Game/Screenshots/EndOfRunCleanFramePreparationCore.cs) owns the bounded post-tooltip-cleanup pose window and the wait/clean-capture/degraded-capture decision. The Unity attempt supplies native visibility audits, structural card snapshots, and relative time; clean-frame proof is best-effort and cannot by itself suppress the end-of-run artifact.

## Evidence

Behavior tests exercise the cores directly: [`SavedReplayLifecycleTests`](../../tests/CombatReplayPlaybackLogging.Tests/SavedReplayLifecycleTests.cs), [`CollectionViewState.Tests`](../../tests/CollectionViewState.Tests/), [`BundleSealConvergence.Tests`](../../tests/BundleSealConvergence.Tests/), and the upload lifecycle coverage in [`FeatureLogging.Tests`](../../tests/FeatureLogging.Tests/).
