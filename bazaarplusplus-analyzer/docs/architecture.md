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

## Modes and evidence

`run --dry-run` reports its plan without reading or writing pipeline or
external state. `run --no-publish` performs collection and local snapshot
replacement without constructing an R2 adapter.

The Operational Evidence module owns the shape and persistence of the Run
Report, live status, run history, and logs. Each normal invocation atomically
refreshes `status.json`, appends to `runs.jsonl`, and records progress in its run
log. These artifacts are local operational evidence, not consumer objects.

Run evidence aggregates collection and processing performance at Run scope:
listing pages and retries, download attempts, retries, bytes and latency
percentiles, plus bounded stage timings for projection, Parquet persistence,
snapshot construction, and publication. It never stores per-Bundle metrics.
