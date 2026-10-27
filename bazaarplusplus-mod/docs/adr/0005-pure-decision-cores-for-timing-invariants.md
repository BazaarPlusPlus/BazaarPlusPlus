# ADR-0005: Timing invariants live in pure decision cores, not MonoBehaviour glue

Status: Accepted

## Context

Runtime orchestration used to hold race-safety and timing state inside `MonoBehaviour` callbacks. The behavior was difficult to exercise without Unity, while source-shape assertions could only prove that tokens appeared in an expected order.

## Decision

Put orchestration state algebra in pure decision cores. A core accepts observations and relative time, owns transitions, and returns decisions; the runtime translates those decisions into Unity, I/O, and publication effects.

A pure core is effect-free, not necessarily dependency-free. Use the smallest real domain dependency and keep Unity types, held delegates, clocks, files, codecs, and runtime objects outside. Do not invent mirror enums solely to make a source file compile without game assemblies.

## Load-bearing guardrails

- [`SavedReplayLifecycle`](../../src/BazaarPlusPlus.Core/Game/CombatReplay/SavedReplayLifecycle.cs) commits replay start in three stages, decides exit in two phases around the ended publication, and owns one 15-second suppression latch shared by the bootstrapped and native exits. Pending menu return and start progress are parallel states. Core's `SavedReplayPlayback` feeds it and translates its decisions for both hosts, through each host's port (ADR-0020).
- [`CollectionViewState`](../../src/BazaarPlusPlus.Core/Game/CollectionPanel/CollectionViewState.cs) returns `null` for a same-value no-op. Catalog-driven hero normalization does not write the user's preference; grid unavailability does not trigger normalization write-back or cache invalidation; the run day open selection uses is the one the panel stamps when it opens, and it holds until the first catalog query re-reads it from the day-tier port. Amended 2026-10-25: opening is one transition, `Open(CollectionOpenFacts)`: catalog readiness from the host's cached catalog, then the remembered-hero load (out of a run only; a failed load counts as absent), then the open selection and its apply. Open never accepts the catalog, so the host delivers it afterwards with `AcceptCatalog`, `CatalogUnavailable` or `BeginCatalogLoad`. `Close()` builds its render before it clears the loading flag.
- [`UploadPump`](../../src/BazaarPlusPlus.Core/Game/Upload/UploadPump.cs) has a two-point shutdown: release arm subscriptions first, then dispose the feed session after the drain callback. This keeps in-flight attempts away from disposed resources.
- [`BundleSealConvergence`](../../src/BazaarPlusPlus.Core/Game/BundlePipeline/BundleSealConvergence.cs) receives relative time and input facts and returns continue, wait, degradation, or terminal decisions. Its failure half, `BundleSealFailurePolicy`, classifies a failure by walking the whole exception graph (an environment link anywhere wins) and decides retry backoff, once-per-launch parking, reseal, degradation, or termination; every exception the seal path catches is logged through its decision, while convergence terminals and failures to write the queue row log directly. Its wake half, `BundleSealWakeSchedule`, turns the same backoff and the input deadlines into the worker's next wake and its idle cap, so the worker scans only when a job can act. Storage parses persisted UTC time and the coordinator performs the one relative-time translation.
- [`EndOfRunCleanFramePreparationCore`](../../src/BazaarPlusPlus/Game/Screenshots/EndOfRunCleanFramePreparationCore.cs) owns the bounded post-tooltip-cleanup pose window and the wait/clean-capture/degraded-capture decision. The Unity attempt supplies native visibility audits, structural card snapshots, and relative time; clean-frame proof is best-effort and cannot by itself suppress the end-of-run artifact.

## Evidence

Behavior tests exercise the cores directly: [`SavedReplayPlaybackTests`](../../tests/SavedReplayPlayback.Tests/SavedReplayPlaybackTests.cs) with [`SavedReplayLifecycleTests`](../../tests/SavedReplayPlayback.Tests/SavedReplayLifecycleTests.cs), [`CollectionViewState.Tests`](../../tests/CollectionViewState.Tests/), and [`BundleSealConvergence.Tests`](../../tests/BundleSealConvergence.Tests/).
