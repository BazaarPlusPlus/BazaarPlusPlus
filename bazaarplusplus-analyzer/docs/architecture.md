# Pipeline architecture

This document owns invariants that span pipeline modules. Consumer payload
semantics belong to `specs/consumer-data-contract.md`.

## Lifecycle

One directory-locked invocation:

1. heals settled Source Hours through one intake transaction: enumerate the
   complete index, admit each Bundle once, project bounded Arrow batches, and
   atomically commit the complete hour;
2. seals a Complete Source Day only after all 24 hourly commits verify;
3. selects the latest consecutive one-to-seven-day Analysis Window;
4. builds, validates, saves, and optionally publishes each consumer snapshot
   independently;
5. after both consumer snapshots publish successfully, removes local facts older
   than the configured number of latest Complete Source Days.

Raw Bundle envelopes are validated once at admission; only the manifest and Run
segment cross into projection. Raw bytes are then discarded. Durable local
state consists of hourly facts and commits, day seals or abandonment records,
current snapshots, status, run history, and logs.

## Recovery invariants

- A download or Bundle validation failure leaves its Source Hour uncommitted.
- An hourly commit is immutable. Reprocessing identical content reuses it;
  different content at the same Source Hour is a conflict.
- A day seal names exactly 24 verified hourly commit identities. A retention-
  expired day that cannot be completed is recorded as abandoned.
- Analysis reads explicit Parquet paths recovered from verified day seals.
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

The consumer snapshots publish to the R2 bucket `BPP_METRICS_R2_BUCKET` names:
`bazaarplusplus-metrics-v6`, served read-only at the custom domain
`bpp-metrics-v6.bazaarplusplus.com`. Keys carry no version prefix; the bucket
and domain name the version.
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

Production runs are `.github/workflows/analyzer.yml`: hourly on a GitHub-hosted
runner, plus manual dispatch with an optional `--no-publish`. A runner starts
empty, so the Data Root lives in the private R2 bucket
`bazaarplusplus-analyzer-state-v6` and `.github/scripts/analyzer-state.sh`
mirrors it around each run:

- The run starts only after a complete pull into an empty Data Root and a
  `bpp verify --deep` of what it restored; either failing stops the job before
  any write.
- After a successful run the mirror deletes what fact retention pruned. After a
  failed run it only uploads new and changed files, so the hours that run
  committed survive and nothing is deleted.
- Lock and staging entries are never mirrored. The workflow's concurrency
  group, not `DirectoryLock`, keeps the state bucket to one writer, so no other
  machine runs a publishing analyzer against that bucket.

Snapshots still advance once per Complete Source Day; the hourly schedule only
keeps each run's intake small and leaves room for skipped schedules within the
eight-day Bundle retention. `BPP_MAX_RUN_SECONDS` is set below the six-hour job
limit so a long heal stops itself and its committed hours are mirrored before
the runner is reclaimed. The workflow's secrets live in the `analyzer` GitHub
Environment, listed in `release/github-secrets.json`; its R2 token reaches only
the V6 metrics and state buckets.

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

Run evidence aggregates collection and processing performance at Run scope:
listing pages and retries, download attempts, retries, bytes and latency
percentiles, plus bounded stage timings for projection, Parquet persistence,
snapshot construction, and publication. It never stores per-Bundle metrics.
