# V5 import fixtures

Frozen inputs for the one-time V5 import (installer ADR-0008): the V5 local history database in each shape real users have, seed rows, and a minimal `BazaarPlusPlusV5/` Data Root. They are frozen because the mod's V5 schema and migration code is being deleted; after that nothing in the repository can produce these shapes again. Do not regenerate them from a later mod.

Everything is text. No `.db` file is committed: a test builds each database from its `*.schema.sql` (rusqlite `execute_batch`), then loads the rows. `tests/v5_import_fixtures.rs` asserts each fixture's `user_version` and `PRAGMA table_info` columns and that the seed rows match the Data Root sample.

## Schemas

Each `*.schema.sql` uses the mod golden's dump format: `PRAGMA user_version=N;`, then every `sqlite_master` statement ordered by type (table, index, trigger) and name, each ending in `;` and LF. The index expressions contain the literal control and Unicode whitespace characters of `RunLogSchema.HistoryWhitespace`, a lone CR included; edit these files only through the generator.

| File | `user_version` | Source |
|---|---|---|
| `v1.schema.sql` | 1 | `RunLogSchema.BootstrapSql` at `d16fee6192f4539f7c853cdd10a653fb2d76df8d` (`LocalDatabaseSchemaVersion = 1`), run on an empty database. |
| `v2.schema.sql` | 2 | `RunLogSchema.BootstrapSql` at tag `5.5.0` (`c5c9c919e08b79ce0e9621f10681e679d19e97c4`, `LocalDatabaseSchemaVersion = 2`), run on an empty database. |
| `v3.schema.sql` | 3 | A database created fresh by 5.6.0 and 5.7.0: byte copy of `history-database-v3.schema.sql`, the mod's `RunLoggingPipeline.Tests` golden, at `3c8e50556b97e059ef8f1eb56c552e3b9437603e`. The generator also renders that commit's `BootstrapSql` and checks it reproduces the golden. |
| `v3-upgraded-from-v1.schema.sql` | 3 | `v1.schema.sql`'s database opened by `RunLogSchema.EnsureInitialized` at `3c8e50556b97e059ef8f1eb56c552e3b9437603e`: the shape of every user who started before 5.5.0. |

`generate-schemas.py` (Python 3 standard library only) regenerates all four from those commits through `git show`, so it does not depend on the working tree; `python3 generate-schemas.py --check` verifies the committed files instead. It renders the C# raw interpolated strings, substituting `RunLogSchema`'s string constants.

The upgraded fixture was produced by replaying `EnsureInitialized`'s SQL with Python's `sqlite3`, not by running the C#, because no .NET SDK was available when the fixtures were frozen. The replay runs, in order: the `UpgradeToLifecycleColumns` script (`ALTER TABLE ... ADD COLUMN`, its `UPDATE`s, index, triggers, `user_version = 2`), `DropDriftedIndexes` (no index drifted), then `BootstrapSql` (`CREATE ... IF NOT EXISTS`, `DROP INDEX IF EXISTS idx_runs_status_last_seen`, the new indexes, `user_version = 3`). The remaining steps cannot change the shape: `ValidateLifecycleColumns` only reads, and `BundleQueueStore.RecoverLegacyJsonFailures` and `BundleQueueStore.NormalizeSealJobDeadlines` only update rows.

Both v3 fixtures, and `v2.schema.sql`, have the version 3 column shape `b3458fe8b3a4f5c5` (`ShippedColumnShapes` in the mod's `RunLogSchemaColumnShapeTests`): `ALTER TABLE ADD COLUMN` appends each lifecycle column right after the last v1 column, where a fresh v3 also declares it, so `PRAGMA table_info` lists the same columns in the same order. What differs is the stored table definition. The upgraded `battles` lacks the table-level `local_payload_state` CHECK (its lifecycle triggers still guard inserts and updates), and the upgraded `combat_replay_videos` lacks the `attachment_state` and `file_state` CHECKs, so it accepts values outside those lists. Indexes and triggers are identical.

## Rows

`rows-common.sql` holds the tables whose columns are the same in every shape; load it first. Then load `battles-v1.sql` for `v1.schema.sql`, or `battles-v2-v3.sql` for the other three; the two hold the same battles and videos, with and without the lifecycle columns. Before loading, replace `{{V5_ROOT}}` with the absolute path of the sample's `game/BazaarPlusPlusV5` (or a copy of it) and `{{OUTSIDE_ROOT}}` with that of `outside/`, doubling any `'`. The row values are shared across shapes and are not tied to the mod version that would have written each shape.

The rows cover:

- `run-alpha` and `run-bravo` share one screenshot file and one replay video file.
- Absolute references: `run-charlie`'s screenshot and video point inside the V5 Data Root, `run-delta`'s outside it (into `outside/`).
- `run-echo` and its collision id `run-echo:bpp:<32 hex>`; `run-echo`'s screenshot path uses Windows separators.
- `run-foxtrot` is unfinished (`completed = 0`, `status = 'active'`).
- `run-golf` has the internal hero id `hero8`.
- Ghost battles that must not be imported: `ghost-local-ready` (payload in `GhostBattlePayloads/`) and `ghost-remote-only` (never downloaded).
- `bundle_outbox`: `bundle-alpha` uploaded, `bundle-bravo` pending with its file in `BundleOutbox/`. Seal jobs: `run-echo` waiting with an ISO `"o"` `input_deadline_at_utc`, the collision run in `terminal_failure` with SQLite `datetime()` text.
- Replay retention, at the reference clock 2026-10-10T00:00:00Z and the 30-day window of `ReplayPayloadRetentionPolicy.RetainedAge`: `battle-golf-1` (2026-08-01) has a payload outside the window; the alpha, bravo, echo, and foxtrot battles have payloads inside it. `battle-charlie-1` is `evicted` and `battle-delta-1` `missing` in the v2/v3 rows.

## Data Root sample

`game/BazaarPlusPlusV5/` is a minimal V5 Data Root holding every file the rows name: `Screenshots/`, `CombatReplays/`, `CombatReplayVideos/`, `GhostBattlePayloads/`, `BundleOutbox/`, and the mod's remote caches `builds.json`, `supporter-list.json`, `voice-lines.json`, and `EncounterPreview/preview-plans.json`. `outside/` stands for a location outside the Data Root. File contents are one-line text placeholders, not decodable images, videos, payloads, or Bundles; only the names and presence matter.
