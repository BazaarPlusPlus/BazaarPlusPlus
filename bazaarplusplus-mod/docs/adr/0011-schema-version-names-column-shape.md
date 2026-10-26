# ADR-0011: The local schema version names column shape

Status: Accepted

## Context

The installer's History reads the mod's SQLite database and opens only the `user_version` values listed in `../bazaarplusplus-installer/src-tauri/history-database-compatibility.json`. The mod refuses a database newer than `RunLogSchema.LocalDatabaseSchemaVersion`. 5.6.0 bumped the version from 2 to 3 only to run a one-time queue repair of legacy JSON seal failures in `BundleQueueStore` (removed in V6), with no column change. As a result, a mod downgraded below 5.6.0 refuses the database and loses all storage, older installers report History unsupported, and the installer's compatibility list and tests changed with nothing new to read. The number was restated across `RunLogSchema`, tests, and both JSON files. The two files were compared only when a Payload was prepared.

V6 moves the data root to `BazaarPlusPlusV6/` and starts a new database there instead of migrating the V5 one, so its numbering restarts.

## Decision

1. `user_version` names the column shape the installer reads. Bump it only for a column change. The bump lands in one pull request with its migration, `BazaarPlusPlus.history-database.json`, and the installer's compatibility list. Index and trigger DDL in `BootstrapSql` needs no bump.
2. A one-time data repair never bumps it. The first repair that must run once adds a mod-private ledger table through `BootstrapSql`, with one row per repair name written in the repair's own transaction. The repair checks that ledger, not the version. No ledger exists yet because no repair needs one. V5's version 3 repair was keyed on leaving version 2; V6 has no repair and no migration.
3. The mod's version is the newest one the installer supports. `assertHistoryDatabaseCompatibility` in root `release/history-database.mjs` checks the source files in `release::check` and the staged Payload in `release::prepare`.

A mod-private table is invisible to the installer. History, cleanup, and the overlay query named tables only, and Reset deletes the whole data root (installer ADR-0008).

## Guardrails

- V5 keeps version 3: it shipped in 5.6.0, and reverting it would strand databases already at 3. V6 starts at 1, and `EnsureInitialized` accepts only 0 (empty) and 1, so a V5 database at 3 is refused, not reinterpreted. V6 stores the seal input deadline as an integer column (`bundle_seal_jobs.input_deadline_at_ms`), so its format needs no repair.
- `RunLogSchemaColumnShapeTests` fails when columns change without a bump and when a bump records a shape an earlier version already had.
- `CREATE INDEX IF NOT EXISTS` never redefines an index, and History pages name theirs in `INDEXED BY`, which errors when the stored definition cannot serve the query. `EnsureInitialized` drops each index whose stored SQL differs from what `BootstrapSql` produces, in the init transaction, so `BootstrapSql` recreates it; `RunLogSchemaIndexTests` and `HistoryPaginationTests` pin this.
- Not covered: CHECK constraints and triggers are outside the fingerprint, a changed trigger or retired index name needs an explicit `DROP`, and a mod older than the database still refuses it.

## Evidence

- `LocalDatabaseSchemaVersion` and `EnsureInitialized` in `src/BazaarPlusPlus.Storage/RunLog/RunLogSchema.cs`
- `tests/Storage.Tests/RunLogSchemaColumnShapeTests.cs`, `tests/Storage.Tests/RunLogSchemaReleaseContractTests.cs`, `tests/Storage.Tests/RunLogSchemaIndexTests.cs`
- Root `release/history-database.test.mjs`
