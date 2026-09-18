# ADR-0007: Remote data separates runtime catalogs, release manifests, and build seed fetch

Status: Accepted

## Context

Runtime catalogs, the release manifest, and production build seeds are all remote data, but they have different owners, freshness rules, validation, and failure recovery. Sharing an HTTP primitive does not make them one lifecycle.

## Decision

| Lifecycle | Owner and contract |
| --- | --- |
| Supporter runtime catalog | [`SupporterCatalogModule`](../../src/BazaarPlusPlus/Game/Supporters/SupporterCatalogModule.cs) owns an `IRemoteEmbeddedCatalog<T>` session. Fixed-list mode creates no catalog; normal mode publishes a session-stable snapshot, retries a missing snapshot after five minutes, and rejects late publications after disposal. The static facade contains projection and fixed-list policy only. |
| Release manifest | [`ReleaseManifestClient`](../../src/BazaarPlusPlus/Infrastructure/ReleaseManifest/ReleaseManifestClient.cs) maps the installer manifest request into typed outcomes. [`ReleaseManifestCheckLifecycle`](../../src/BazaarPlusPlus/Game/Lobby/ReleaseManifestCheckLifecycle.cs) owns request generation, cancellation, disposal, and the current-request publication gate. This request is separate from Mod API health and `ModApiSession`. |
| Build seed fetch | [`RemoteEmbeddedDataFetcher`](../../build/RemoteEmbeddedDataFetcher/RemoteEmbeddedDataFetch.cs) stages one streamed download, checks transfer integrity, and cleans temporary files. `run.sh fetch-data` stages the complete seed set, runs feature-owned semantic parsers, then promotes the set transactionally with rollback. |

## Why

One shared lifecycle would make a long-lived cached catalog, a short-lived UI request, and a pre-compilation transaction pretend to share cancellation and publication semantics. It would also create a second schema owner if the transport layer validated feature payloads.

## Guardrails

- Share HTTP primitives only where their contracts match; keep the three lifecycles independent.
- Gate every asynchronous publication by the current owner generation and dispose the owner exactly once.
- Keep feature parsers as the semantic source of truth for seed data.
- Promote a build seed set only after every member passes validation; a failure leaves the canonical set unchanged.
