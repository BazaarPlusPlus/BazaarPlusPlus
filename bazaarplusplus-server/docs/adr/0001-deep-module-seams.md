# ADR 0001: Deep module seams

Status: Accepted
Date: 2026-08-02

## Context

The Worker must preserve a fixed public wire contract while keeping protocol handling, Bundle validation, persistence decisions, and delivery state transitions independently testable. It is a standalone backend: earlier Workers' handlers, storage layouts, bindings, and wire behavior are not architectural inputs.

The architecture therefore uses a small number of explicit seams rather than controller, service, and repository forwarding layers.

## Decision

### Route table and HTTP shell

`src/http/routes.ts` is the complete public route table. `src/http/route-shell.ts` binds that table into the Worker `fetch`; it is the sole HTTP exit and owns path and method resolution, `OPTIONS`, request IDs, authentication ordering, route CORS, JSON envelopes, route-specific error headers, and unclassified-error logging. Route handlers return status and body data; they do not construct the final HTTP response.

### Handler dependencies

There is no handler dependency object. Domain handlers take `(request, env, requestId)`, read the clock with `Date.now()`, and construct the download signer with `createBundleDownloadSigner(env)`. Tests drive time and signing through the deployed `worker.fetch`: `vi.spyOn(Date, "now")` controls time, and `vi.spyOn(crypto.subtle, "sign")` injects signing failures.

### Bundle opening

`src/bundle/open.ts` owns bounded prefix and manifest reads, manifest validation, payload layout validation, complete-object digest validation, and Run and Screenshot segment digest validation. `openBundle` returns the validated descriptor, a streaming body for the conditional R2 PUT, and the eventual complete digest. It never decompresses the Run payload and does not own R2 or D1 policy.

### Bundle persistence

`src/modules/bundle-commit.ts` owns immutable-identity decisions and the atomic D1 Bundle commit. It keeps the Ghost eligibility predicate in one definition, associates D1 results with named statements, preserves the first cross-Bundle projection on a duplicate battle identity, and makes the uploader insert the final batch statement. The ingest module owns R2 orchestration and orphan recovery, not persistence branching.

### Delivery convergence

Superseded for V6 by [ADR 0004](0004-bazaardb-cursor-feed.md): V6 keeps no delivery state, so there is nothing to converge.

### D1 performance verification

Bounded-read tests drive the deployed `worker.fetch` against local D1. A test recorder observes the actual SQL, bindings, and results at the existing D1 seam without replacing native statements or splitting batches. Read-cost assertions put an upper bound on each recorded statement's `meta.rows_read`; they do not assert query-plan text. Index use needs no plan assertion: a statement that names its index with `INDEXED BY` fails with "no query solution" when that index cannot serve it, so the behavior test that runs the statement fails. Production modules own the SQL, while tests own independent cost expectations. Plan assertions remain only for migration and schema contracts that have no runtime caller.

## Consequences

- Public route additions must update the route table, contract tests, and API reference together.
- Tests control time and signing by spying on the runtime globals the Worker reads, so production code carries no test-only parameters.
- Bundle validation and D1 commit decisions can be tested through public module interfaces.
- The scheduled handler owns a separate 15-day D1 metadata retention policy, detailed in [ADR 0002](0002-d1-retention.md). It deletes parent Bundles in bounded batches and relies on foreign-key cascades, without accessing R2 or deleting uploader assertions.

## Source layout

Cross-cutting single-file directories were flattened to `src/limits.ts`, `src/presigner.ts`, and `src/errors.ts`; moving `HttpError` out of `src/http/` made the directory dependency graph one-directional (`http` → `modules` → `bundle`/`presigner` → `errors`/`limits`). `test/` mirrors the `src/` layout. Module boundaries and interfaces are unchanged by layout.

## Historical plans

The pre-implementation planning documents that lived under `docs/plans/` were process artifacts; their durable decisions are captured by this ADR, current source, `docs/api-reference.md`, and `contracts/v6/`. They were removed from the tree and remain available in git history.
