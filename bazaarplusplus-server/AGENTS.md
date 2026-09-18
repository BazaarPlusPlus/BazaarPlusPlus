# Project Rules

## Scope

This is the standalone V5 mod-facing backend. Never copy V4 handlers, migrations, bindings, object layouts, or wire behavior unless a V5 design document explicitly requires it. `src/env.ts` is the only place to declare Worker bindings, vars, and secrets.

## Domain invariants

A Bundle is the smallest upload, R2 storage, and delivery unit: exactly one Run and zero or one Screenshot. The Bundle prefix is 16 bytes — ASCII `BPPBNDL5`, a u32 big-endian version, and a u32 big-endian manifest length. A Bundle carries at most 30 Battle projections (`MAX_BATTLES_PER_BUNDLE`), and duplicate `battle_id` values inside one Bundle are invalid.

## Trust and projection

Bundle upload is intentionally unauthenticated: `uploader_account_id` and `bundle_uploaders` are caller assertions, never authentication or authorization facts. `bundle_uploaders` starts empty, records only successful V5 Bundle uploaders, and is updated as the final Bundle commit statement with `ON CONFLICT DO NOTHING`. Ghost projection accepts only a self opponent or an opponent already present in `bundle_uploaders`; filtered history is never backfilled. A repeated `(uploader_account_id, battle_id)` across Bundles is anomalous: preserve the first projection and record a metric, and never merge fields across Bundles.

## Runtime boundaries

The Worker never exposes a Bundle download proxy and never decompresses the Run payload during ingest. Protected routes authenticate before parsing or querying, and `GET /ghost-battles` calls its rate-limit binding before business query parsing or D1 access. R2 object retention is an externally provisioned 8-day lifecycle rule, and every issued presigned URL is a seven-day S3 `GetObject` capability.

## Change discipline

All public HTTP paths are listed in `docs/api-reference.md`; adding a route requires updating the route table, contract tests, and the API reference together. Tests exercise public module interfaces — storage adapter tests may inspect schema and query plans only when the migration or SQL statement is the interface under test.

## Pull request hygiene

Use a clear, correctly capitalized, imperative PR title with no conventional-commit prefix and no trailing punctuation. End the body with `Release Notes:` and one `Added`/`Fixed`/`Improved` bullet, or `N/A` for backend-internal work.
