# ADR 0001: V5 deep module seams

Status: Accepted
Date: 2026-08-02

## Context

The V5 Worker must preserve a fixed public wire contract while keeping protocol handling, Bundle validation, persistence decisions, and delivery state transitions independently testable. It is a standalone backend: V4 handlers, storage layouts, bindings, and wire behavior are not architectural inputs unless the V5 contract explicitly requires them.

The architecture therefore uses a small number of explicit seams rather than controller, service, and repository forwarding layers.

## Decision

### Route table and HTTP shell

`src/http/routes.ts` is the complete public route table. `src/http/route-shell.ts` is the sole HTTP exit and owns path and method resolution, `OPTIONS`, request IDs, authentication ordering, route CORS, JSON envelopes, route-specific error headers, and unclassified-error logging. Route handlers return status and body data; they do not construct the final HTTP response.

### Handler dependencies

`src/http/deps.ts` is the only `HandlerDeps` channel. Every domain handler has the required four-argument form `(request, env, requestId, deps)`; `deps` has no default. The production dependency object supplies the clock and lazily constructed download signer. The claim handler reads `deps.signer` before its D1 claim batch so signer-construction failures retain their established behavior.

### Bundle opening

`src/bundle/open.ts` owns bounded prefix and manifest reads, manifest validation, payload layout validation, complete-object digest validation, and Run and Screenshot segment digest validation. `openBundle` returns the validated descriptor, a streaming body for the conditional R2 PUT, and the eventual complete digest. It never decompresses the Run payload and does not own R2 or D1 policy.

### Bundle persistence

`src/modules/bundle-commit.ts` owns immutable-identity decisions and the atomic D1 Bundle commit. It keeps the Ghost eligibility predicate in one definition, associates D1 results with named statements, preserves the first cross-Bundle projection on a duplicate battle identity, and makes the uploader insert the final batch statement. The ingest module owns R2 orchestration and orphan recovery, not persistence branching.

### Delivery convergence

Lazy delivery convergence is claim-only. `POST /bazaardb/deliveries/claim` marks Bundles beyond R2 retention and expired final attempts as failed before claiming a page. Settle does not perform convergence; it validates active lease ownership and applies idempotent per-attempt outcomes.

Each convergence statement updates at most 1,000 rows. If expired pending Bundles remain, a scalar index probe sets the claim candidate limit to zero, and the handler returns a retryable `503` after committing maintenance progress. This keeps both scans and writes bounded without issuing expired objects or reporting an empty successful page while live work is blocked behind cleanup. Candidate orders are applied through an `UPDATE ... FROM` join; settle reads all requested receipts in one query.

Pending delivery retention uses the indexed `bundle_stored_at_ms` projection. Database triggers derive it from the authoritative `bundles.stored_at_ms` on delivery insertion, Bundle storage-time changes, and terminal-to-pending requeue. Historical terminal rows need no backfill because retention never queries them. This adds one indexed projection and an insert-time update to avoid scanning the live pending backlog on every claim. The migration is compatible with older Workers that omit the new column; deploy it before the Worker.

### D1 performance verification

Query-plan and bounded-read tests call the public handler interfaces against local D1. A test recorder observes the actual SQL, bindings, and results at the existing D1 seam without replacing native statements or splitting batches. Plan assertions explain those recorded statements; read-cost assertions use their execution metadata. Production modules own the SQL, while tests own independent plan and cost expectations. Handwritten queries remain appropriate for migration and schema contracts that have no runtime caller.

## Consequences

- Public route additions must update the route table, contract tests, and API reference together.
- Tests can replace time and signing through one dependency object without changing Worker bindings.
- Bundle validation and D1 commit decisions can be tested through public module interfaces.
- Delivery maintenance occurs on claim traffic or through explicit operator action, never through settle or a scheduled Worker handler.
- The scheduled handler owns a separate 15-day D1 metadata retention policy, detailed in [ADR 0002](0002-d1-retention.md). It deletes parent Bundles in bounded batches and relies on foreign-key cascades, without accessing R2 or deleting uploader assertions.

## Source layout

Cross-cutting single-file directories were flattened to `src/limits.ts`, `src/presigner.ts`, and `src/errors.ts`; moving `HttpError` out of `src/http/` made the directory dependency graph one-directional (`http` → `modules` → `bundle`/`presigner` → `errors`/`limits`). `test/` mirrors the `src/` layout. Module boundaries and interfaces are unchanged by layout.

## Historical plans

The pre-implementation planning documents that lived under `docs/plans/` were process artifacts; their durable decisions are captured by this ADR, current source, `docs/api-reference.md`, and `contracts/v5/`. They were removed from the tree and remain available in git history.
