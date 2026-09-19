# Analyzer operational measurements

These measurements were taken on Apple Silicon on 2026-08-11 and 2026-08-12
against the production Bundle Server, using read-only collection and local fact
writes. They are workload observations, not portable guarantees.

## Bundle retention behavior

The configured retention contract is eight days. Deterministic boundary checks
cover the Worker cutoff (`8 × 86,400,000 ms`) and the Analyzer default (eight
UTC dates).

The server returns HTTP 410 with `window_expired` for hours outside retention
and HTTP 200 with an empty `items` array for a retained hour with no Bundles.
The source adapter preserves this distinction; an expired day is abandoned,
while a retained zero-Bundle hour can be committed.

## Collection throughput

A busy Source Hour contains roughly 2,400 Bundles. Initial conservative
collection took five to six minutes per busy hour. Because the workload is
latency-bound, increasing download concurrency from 4 to 8 approximately
halved collection time. Concurrency 64 produced no observed server-side
pushback when the HTTP keepalive pool was sized to match. The shipped
defaults are therefore 64 download workers, 128-bundle lookahead, and a
21,600-second run budget.

Transient collection failures use bounded jittered backoff. A valid
`Retry-After` response can extend the next delay up to 60 seconds. Once a fatal
Bundle failure is observed, queued downloads that have not started are
cancelled; already-running requests are allowed to finish before the Source
Hour fails without a commit.

## Local fact storage

Busy hours produce roughly 17–22 MiB of Parquet, dominated by Battle Cards.
Five sealed Source Days occupied about 1.9 GiB.

The default local fact retention is the latest eight sealed Complete Source
Days. It is intentionally separate from the Bundle Server's eight-day source
availability contract. Retention runs only after both consumer snapshots have
published successfully and preserves every newer incomplete Source Hour.

On 2026-08-19, 272 hourly partitions spanning 11 sealed days plus eight hours
of the next Source Day occupied 4,731,857,648 bytes (4.41 GiB). Applying the
eight-sealed-day boundary to that population would remove 72 old hourly
partitions totaling about 1.19 GiB, leaving an estimated 3.21 GiB. Daily size
varies with Bundle volume.

## Analysis resources

Analysis of five Source Days containing 2.79 million Battles took about four
seconds and stayed below 1 GiB RSS. The seven-day offline fixture remains below
the 8 GiB build limit.

Projection flushes fixed 50,000-row Arrow batches. A 2,500-Bundle synthetic
hour with 1.13 million Battle Card rows peaked 159 MiB above its fresh-process
baseline and produced 23 Parquet row groups. A production busy-hour heal with
734,544 rows peaked at 0.65 GiB for the whole process, below the 1 GiB hourly
ingest limit.

After Run performance instrumentation was added, the current 2,500-Bundle,
250,000-card acceptance fixture completed in 3.10 seconds, peaked about 148 MiB
above its fresh-process baseline, and kept whole-process RSS near 241 MiB.

## Consumer snapshot size

The two current snapshot sizes have not yet been measured in production.

## Performance evidence

Each Run Report records low-cardinality collection totals: listing pages and
requests, listing/download retries, download attempts and bytes, and p50/p95
attempt latency. Run timings separate source indexing, download waiting,
projection, Parquet writing, fact finalization, and each product's build, local
save, and publication stages. This is diagnostic evidence rather than a
portable service-level guarantee.
