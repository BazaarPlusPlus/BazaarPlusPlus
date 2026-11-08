# Pipeline architecture

This document owns invariants that span pipeline modules. Consumer payload
semantics belong to `specs/consumer-data-contract.md`.

## Code boundaries

The Python package and operator entry point are both `bazaarlens`.

| Module                                                              | Responsibility                                                                         |
| ------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| `facts/schema.py`                                                   | Arrow table schemas shared by projection and persistence                               |
| `facts/admission.py`, `facts/run_payload.py`, `facts/projection.py` | Accepted Run normalization, binary Run decoding, and bounded fact batches              |
| `facts/store.py`                                                    | Hour commits, day seals, verification, and retention                                   |
| `analysis.py`                                                       | Analysis Window selection, isolated DuckDB connections, and fact counts                |
| `products/heroes.py`, `products/builds.py`                          | Product SQL, payload construction, and semantic validation                             |
| `snapshots.py`                                                      | Schema validation, canonical serialization, local replacement, and remote confirmation |
| `driver.py`                                                         | Run ordering, Source Day scheduling, lock ownership, and retention decisions           |
| `operational_evidence.py`                                           | Run reports, live status, history, and logs                                            |

Paths in the table are relative to `src/bazaarlens/`. `SourceHourIntake` in
`hour_intake.py` coordinates collection through `bundle_source.py` and the
facts modules. Facts do not depend on consumer products or publication.

`ConsumerProduct` in `products/base.py` declares each product's fact tables,
builder, calculation version, semantic validator, contract, destinations, and report counters.
`PRODUCTS` in `products/__init__.py` is the explicit registry. A new product
supplies that definition and its owned contract; the driver and snapshot
execution sequence do not contain product-specific branches.

`open_analysis` in `analysis.py` opens only the requested tables and closes
the connection on exit. Products use DuckDB directly and return `ProductData`;
they do not write snapshots or publish objects. `SnapshotRunner` in
`snapshots.py` owns those effects and returns a `SnapshotResult` that separates
local persistence, remote confirmation, and whether an upload was necessary.
An ordinary product failure returns its completed stages; ownership loss and
observer failures propagate to stop the Run.

`contracts/v6/` remains the single source for JSON schemas and hero aliases.
The wheel includes these resources through the build configuration;
`contract_bytes` in `src/bazaarlens/resources.py` reads the bundled resources
when installed and the owned source files in a checkout.

## Lifecycle

One directory-locked invocation:

1. heals settled Source Hours through one intake transaction: enumerate the
   complete index, admit each Bundle once, project bounded Arrow batches, and
   atomically commit the complete hour;
2. seals a Complete Source Day only after all 24 hourly commits verify;
3. selects the latest consecutive one-to-seven-day Analysis Window;
4. reuses each validated snapshot when its inputs match, otherwise builds and
   saves it, then optionally confirms or publishes it independently;
5. after both consumer snapshots publish successfully, removes local facts older
   than the configured number of latest Complete Source Days.

Raw Bundle envelopes are validated once at admission; only the manifest and Run
segment cross into projection. Raw bytes are then discarded. Durable local
state consists of hourly facts and commits, day seals or abandonment records,
current snapshots with their input evidence, status, run history, and logs.

## Recovery invariants

- A download or Bundle validation failure leaves its Source Hour uncommitted.
- An hourly commit is immutable. Reprocessing identical content reuses it;
  different content at the same Source Hour is a conflict.
- A day seal names exactly 24 verified hourly commit identities. A retention-
  expired day that cannot be completed is recorded as abandoned.
- Analysis reads explicit Parquet paths from checked day seals and verifies
  referenced file sizes. Deep verification re-hashes the files separately.
- Projection admits Runs and normalizes hero, final rank, and Battle winner
  side once. Analysis reads those stored columns as-is and reads every hour
  with the one owned table schema, so a change to admission or to a fact
  schema must replace every committed hour in the Analysis Window, not only
  new ones.
- Fact retention defaults to the latest eight Complete Source Days: the
  seven-day Analysis Window plus one rollback day. All newer incomplete Source
  Hours remain available for sealing. Retention removes an expired day seal
  before its hourly facts, so an interrupted cleanup cannot leave a seal that
  references missing files.
- Each product uses its own analysis connection and failure boundary. A valid
  product can advance while the other product remains unchanged.
- Snapshot bytes are canonical JSON and pass schema plus semantic validation
  before local replacement or object-store publication.
- The heartbeat lock fences stale owners before durable writes. A replacement
  owner may recover from committed state without trusting partial staging data.

## Publication target

The consumer snapshots publish to the R2 bucket `BPP_METRICS_R2_BUCKET` names,
under the key prefix `BPP_METRICS_R2_PREFIX`: `metrics/v6/` in the public
bucket `bazaarplusplus-public`, served read-only at
`https://cdn.bazaarplusplus.com/metrics/v6`. The prefix names the version; a
later contract publishes beside it, in the same bucket and domain.
A production mod Payload embeds `builds/latest.json` fetched from this origin at
build time (`TenWinBuildsRemoteUrl` in the mod's `RemoteEmbeddedData.targets`,
which also sets its minimum size), so `release::prepare` fails until a first
publication lands here.

The bucket's CORS rule, set on the bucket rather than kept in this repository,
lets the site read Hero metrics from the browser: allowed method `GET` only, no headers, no credentials; allowed origins
`https://bazaarplusplus.com`, the preview site
`https://bazaarplusplus-site-preview.cauyxy.workers.dev`, and the local dev
servers `http://localhost:3000`, `:4173`, `:5173`, `http://127.0.0.1:3000` and
`http://0.0.0.0:3000`. The site ADR
`bazaarplusplus-site/docs/adr/0001-direct-metrics-origin.md` depends on it.

## Production runs

Production runs are `.github/workflows/analyzer.yml` on a GitHub-hosted runner,
plus manual dispatch with an optional `--no-publish`. Its hourly `schedule`
is best-effort: GitHub drops many of those events, and the next run that fires
ingests every settled hour since the last one. A runner starts
empty, so the Data Root lives under `analyzer-state/` in the private Bundle
bucket `bazaarplusplus-bundle-v6` (whose 8-day lifecycle rule covers only
`bundles/`) and `.github/scripts/analyzer-state.sh`
mirrors it around each run:

- The run starts only after a complete pull into an empty Data Root and a
  `bazaarlens verify --deep` of what it restored; either failing stops the job before
  any write.
- After a successful run the mirror deletes what fact retention pruned. After a
  failed run it only uploads new and changed files, so the hours that run
  committed survive and nothing is deleted.
- Lock and staging entries are never mirrored. The workflow's concurrency
  group, not `DirectoryLock`, keeps the state bucket to one writer, so no other
  machine runs a publishing analyzer against that bucket.

The Analysis Window advances when another Complete Source Day becomes
available. `SnapshotRunner` reuses a local snapshot only when its input
fingerprint and content digest match. The fingerprint covers the selected day-seal identities,
window, product calculation version, schema and hero-alias bytes, fact tables,
report counters, and public destination. Day seals identify their hourly fact
commits. A cache hit still validates the snapshot and verifies its referenced
hours; it skips product queries and preserves the original generation time.
The Run Report still queries the selected facts for its population counts.

The adjacent `latest.inputs.json` stores the fingerprint, content digest, and
product counters. The runner atomically saves the snapshot before this evidence;
an absent, invalid, or mismatched pair triggers a rebuild. Neither file changes
the consumer contract. A local cache hit still checks the remote object when
publication is enabled, so a failed or skipped upload can recover on the next
run. Bump the product's `calculation_version` whenever its builder or calculation
semantics change, including changes in shared calculation helpers.

The hourly schedule keeps each run's intake small and leaves room for skipped
schedules within the eight-day Bundle retention. `BPP_MAX_RUN_SECONDS` is set below the six-hour job
limit so a long heal stops itself and its committed hours are mirrored before
the runner is reclaimed. The workflow's secrets live in the `analyzer` GitHub
Environment, listed in `release/github-secrets.json`; its R2 token reaches only
the V6 metrics and state buckets.

`.github/workflows/daily-report.yml` calls `analyzer.yml` once a day at
about 10:00 Beijing time (02:00 UTC), so the previous UTC day is sealed and
published even when hourly runs were dropped. That run also returns the
`window.end` it analyzed and the Daily Highlights `bazaarlens highlights`
prints for it from the restored facts; the report job renders three images
from the public `heroes/latest.json` and the Daily Highlights and sends them to
the QQ groups. Daily Highlights name players, so they travel only as a job
output, never to a bucket. The report runs in its own job, so a QQ failure never
fails the analyzer, and the analyzer has no QQ dependency; a failed Daily
Highlights step only drops their parts of the report. The `analyzer-v6`
concurrency group is on the job rather than the workflow, so a called run still
waits for an hourly one.

## Modes and evidence

`run --no-publish` performs collection and local snapshot
replacement without constructing an R2 adapter.

The Operational Evidence module owns the shape and persistence of the Run
Report, live status, run history, and logs. Each normal invocation atomically
refreshes `status.json`, appends to `runs.jsonl`, and records progress in its run
log. These artifacts are local operational evidence, not consumer objects.

`RunEvidence` accumulates one Run's facts and materializes its final summary
once. Collection and publication update the same report; a later failure,
including retention failure, preserves completed publication and analysis
facts. The driver retains pipeline ordering and lock-ownership decisions.

`SourceHourIntake` in `src/bazaarlens/hour_intake.py` owns one Source Hour's
index and ingest stages, cumulative admission counts, timings, and terminal
result. Its synchronous progress observations reach Operational Evidence during streaming, before the next
Bundle is projected; the driver does not reconstruct these facts from callbacks
or Run-level counter differences. `admitted_bundles` counts the ordered Bundles
delivered by the source;
`unprocessed_bundles` counts indexed Bundles not yet delivered, including a
failing Bundle and any cancelled or untouched remainder. Their sum equals the
known index population. `committed_bundles` counts only complete committed
hours. An admitted Bundle can therefore belong to an uncommitted hour. A failure
observation only updates in-memory evidence, so a
lost lock cannot trigger another status or log write. `FactStore` in
`src/bazaarlens/facts/store.py` retains atomic commit and write-time ownership
checks; the driver retains Source Day scheduling, abandonment, and sealing. Recoverable source failures return a terminal result
for the driver to schedule; observer failures propagate as Run failures, even
when their exception type happens to match a recoverable source error.

Run evidence aggregates collection and processing performance at Run scope:
listing pages and retries, download attempts, retries, bytes and latency
percentiles, plus bounded stage timings for projection, Parquet persistence,
snapshot construction, and publication. `request_failures` groups failed HTTP
responses by fixed endpoint name and numeric status, with a separate transport
category. It counts attempts even when a later retry succeeds; `retry_exhaustions`
counts operations that use their full retry allowance. Source-hour failures also
retain the terminal HTTP status and attempt count when available. These
measurements contain no download URLs or response bodies.

`FactWriteTimings` in `facts/store.py` measures every commit attempt, including
failed generation and writes. Intake subtracts the time spent advancing its
source iterator (including admission, progress reporting, exhaustion, and
failure cleanup) from batch generation to measure projection. Both terms cover
the same successful and failed attempts. `download_wait_seconds` separately
measures blocking on download futures; `retry_sleep_seconds` sums requested
retry delays across concurrent workers, so neither is added to projection to
reconstruct elapsed Run time.

Product evidence distinguishes a durable local snapshot (`local_saved`), input
reuse (`reused`), remote confirmation (`published`), and an actual object upload
(`uploaded`). A later save, publication, or retention failure preserves earlier
completed stages. The report never stores per-Bundle metrics.
