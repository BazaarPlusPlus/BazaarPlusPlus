import json
import multiprocessing
import uuid
from datetime import UTC, date, datetime, timedelta
from pathlib import Path

import pyarrow.parquet as pq

from bppanalyzer.bundle_source import (
    BundleRef,
    RawHourIndex,
    admit_bundle,
    raw_commit_sha256,
)
from bppanalyzer.driver import PipelineDriver
from bppanalyzer.fact_store import FactStore
from bppanalyzer.operational_evidence import peak_rss_bytes
from bppanalyzer.projection import project_hour
from tests.bundle_fixtures import Card, bundle_bytes, payload


class BusyHourSource:
    def __init__(self, *, bundle_count: int = 2_500, cards_per_set: int = 25) -> None:
        self.bundle_count = bundle_count
        self.run_payload = payload(cards_per_set=cards_per_set)

    def hour_index(self, source_hour: datetime) -> RawHourIndex:
        available_at_ms = int(source_hour.timestamp() * 1_000)
        refs = tuple(
            BundleRef(
                bundle_id=f"bundle-{index:05d}",
                available_at_ms=available_at_ms,
                download_url=f"https://download.invalid/bundle-{index:05d}",
                download_expires_at_ms=available_at_ms + 60_000,
            )
            for index in range(self.bundle_count)
        )
        return RawHourIndex(source_hour, refs, raw_commit_sha256(refs), 1)

    def stream(self, index: RawHourIndex):
        for ref in index.items:
            content = bundle_bytes(ref.bundle_id, run_payload=self.run_payload)
            yield admit_bundle(ref, content)


class DistinctCardHourSource(BusyHourSource):
    """A busy hour whose Bundles each carry their own card identities."""

    def stream(self, index: RawHourIndex):
        for ref in index.items:
            hand = [
                Card(str(uuid.uuid5(uuid.NAMESPACE_URL, f"{ref.bundle_id}/{slot}")), 1, slot)
                for slot in range(50)
            ]
            run = payload(run_id=ref.bundle_id, player_hand=hand)
            yield admit_bundle(
                ref, bundle_bytes(ref.bundle_id, run_payload=run, run_id=ref.bundle_id)
            )


def _measure_busy_hour(root: str, results) -> None:
    now = datetime(2026, 8, 7, 1, 1, tzinfo=UTC)
    source = BusyHourSource()
    baseline_rss = peak_rss_bytes()
    summary = PipelineDriver(
        Path(root),
        source=source,
        clock=lambda: now,
        duckdb_memory_limit="1GB",
        duckdb_threads=1,
    ).run(heal_days=1)
    status = json.loads((Path(root) / "status.json").read_bytes())
    cards_path = Path(root) / "facts/hourly/source_hour=2026-08-07T00/battle_cards.parquet"
    metadata = pq.ParquetFile(cards_path).metadata
    results.put(
        {
            "baseline_rss_bytes": baseline_rss,
            "peak_rss_bytes": summary.peak_rss_bytes,
            "status_peak_rss_bytes": status["peak_rss_bytes"],
            "card_rows": metadata.num_rows,
            "card_row_groups": metadata.num_row_groups,
        }
    )


def test_hour_ingest_records_and_stays_below_the_one_gib_peak_rss_limit(
    tmp_path: Path,
) -> None:
    context = multiprocessing.get_context("spawn")
    results = context.Queue()
    process = context.Process(target=_measure_busy_hour, args=(str(tmp_path), results))
    process.start()
    process.join(timeout=60)

    if process.is_alive():
        process.kill()
        process.join()
        raise AssertionError("Synthetic hour ingest exceeded 60 seconds")
    assert process.exitcode == 0
    measured = results.get(timeout=1)
    results.close()
    results.join_thread()
    assert measured["card_rows"] == 250_000
    assert measured["card_row_groups"] > 1
    assert measured["peak_rss_bytes"] < 1024**3
    assert measured["status_peak_rss_bytes"] == measured["peak_rss_bytes"]
    assert measured["peak_rss_bytes"] - measured["baseline_rss_bytes"] < 256 * 1024**2


def test_batched_hour_commit_is_byte_deterministic_across_batch_boundaries(
    tmp_path: Path,
) -> None:
    source_hour = datetime(2026, 8, 10, 12, tzinfo=UTC)
    committed: list[dict[str, bytes]] = []

    for root_name in ("first", "second"):
        root = tmp_path / root_name
        source = BusyHourSource(bundle_count=501, cards_per_set=25)
        index = source.hour_index(source_hour)
        FactStore(root).commit_hour(project_hour(index, source.stream(index)))
        hour_path = root / "facts/hourly/source_hour=2026-08-10T12"
        committed.append({path.name: path.read_bytes() for path in sorted(hour_path.iterdir())})

    assert (
        pq.ParquetFile(
            tmp_path / "first/facts/hourly/source_hour=2026-08-10T12/battle_cards.parquet"
        ).metadata.num_row_groups
        == 2
    )
    assert committed[0] == committed[1]


def test_one_source_day_persists_only_parquet_plus_at_most_one_percent_metadata(
    tmp_path: Path,
) -> None:
    store = FactStore(tmp_path)
    day = date(2026, 8, 10)
    # Real hours carry distinct cards; identical Bundles would compress far below
    # any realistic metadata ratio.
    busy = DistinctCardHourSource()
    for hour_number in range(24):
        hour = datetime.combine(day, datetime.min.time(), UTC) + timedelta(hours=hour_number)
        if hour_number == 0:
            index = busy.hour_index(hour)
            store.commit_hour(project_hour(index, busy.stream(index)))
        else:
            empty = RawHourIndex(hour, (), raw_commit_sha256(()), 1)
            store.commit_hour(project_hour(empty, ()))
    store.seal_day(day)

    files = [path for path in tmp_path.rglob("*") if path.is_file()]
    parquet_bytes = sum(path.stat().st_size for path in files if path.suffix == ".parquet")
    total_bytes = sum(path.stat().st_size for path in files)
    assert parquet_bytes > 0
    assert total_bytes <= parquet_bytes * 1.01
    assert not list(tmp_path.rglob("*.bundle"))
    assert not (tmp_path / "raw").exists()
