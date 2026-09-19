# ADR-0006: Outbound Mod API rules have protocol and persistence owners

Status: Accepted

## Context

Bundle composition and Ghost import once carried different replayability rules, endpoint clients repeated response parsing, transport lifetime leaked through a client bag, and Bundle queue SQL was split across Game workflows. The shared protocol rules needed one owner without collapsing distinct endpoint behavior.

## Decision

### Wire contract

[`RunBundleV5Contract`](../../src/BazaarPlusPlus.ModApi/Bundle/RunBundleV5Contract.cs) is the shared V5 Run Bundle reader/writer. It verifies the container, payload, Run and player identities, and the exact replayability predicate. `BundleV5Codec` remains an opaque container codec.

[`ModApiResponse`](../../src/BazaarPlusPlus.ModApi/Http/ModApiResponse.cs) is the single bounded JSON-response reader. It owns V5 and legacy envelope recognition, request-ID precedence, `Retry-After`, and the closed user-facing code. Endpoint clients explicitly allow-list remote codes that change product behavior and keep their own disposition, cooldown, expiry, and presentation mappings. Raw bodies and exceptions remain diagnostics rather than user text.

`MessagePackGzipFraming` owns bounded gzip framing and failure classification. `RunPayloadV5Codec` owns payload limits, untrusted-data options, version checks, and V5 error codes. The local replay codec retains its compatibility and user-text behavior.

### Transport lifetime

[`ModApiSession`](../../src/BazaarPlusPlus.ModApi/Clients/ModApiSession.cs) owns one normalized route set, one configured `HttpClient`, typed Bundle/Ghost/health operations, and one disposal boundary. Plugin History, each upload-feed activation, and tools own independent sessions. BazaarDB remains separate because it has a different host and workflow.

A missing Mod API session removes online History capabilities only; local History still mounts.

### Durable queue

[`BundleQueueStore`](../../src/BazaarPlusPlus.Storage/BundleQueue/BundleQueueStore.cs) alone owns `bundle_seal_jobs` and `bundle_outbox` SQL, row materialization, and their transactions. Allocation is idempotent; publication verifies allocation and atomically creates the outbox row before deleting the seal job; invalid artifacts fail and schedule reseal in one transaction.

Storage owns neither Bundle files nor codecs, HTTP, upload, or retention policy. Game reaches files through [`IBundleOutboxFiles`](../../src/BazaarPlusPlus/Game/BundlePipeline/BundleOutboxFiles.cs), drives seal timing through [`BundleSealConvergence`](../../src/BazaarPlusPlus/Game/BundlePipeline/BundleSealConvergence.cs), commits an accepted upload before deletion, and retains artifacts for transient outcomes. Tests use the concrete Storage module against temporary SQLite databases; an interface is reserved for substitutable effects, not mirrored shapes.

## Guardrails

- Keep protocol parsing in ModApi, durable queue state in Storage, and orchestration/product mappings in Game.
- Keep endpoint outcomes typed by behavior; a generic `Result<T>` must not encode their differences as configuration.
- Keep one transport owner per consumer session; no process-global transport or public endpoint-client bag.
- Keep Bundle file policy out of Storage and keep one active queue path.
