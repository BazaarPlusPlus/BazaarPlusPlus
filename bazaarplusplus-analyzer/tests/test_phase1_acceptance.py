import hashlib
import json
import multiprocessing
import os
from datetime import UTC, date, datetime, timedelta
from pathlib import Path

import pyarrow as pa
import pyarrow.parquet as pq

from bppanalyzer.bundle_source import (
    BundleRef,
    RawHourIndex,
    admit_bundle,
    raw_commit_sha256,
)
from bppanalyzer.driver import PipelineDriver
from bppanalyzer.fact_store import FactStore
from bppanalyzer.hour_intake import healing_days, is_hour_settled
from bppanalyzer.operational_evidence import peak_rss_bytes
from bppanalyzer.projection import HourProjection, project_hour, table_schemas
from tests.bundle_fixtures import bundle_bytes, payload


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
                sha256=None,
                bytes=None,
            )
            for index in range(self.bundle_count)
        )
        return RawHourIndex(source_hour, refs, raw_commit_sha256(refs), 1)

    def stream(self, index: RawHourIndex):
        for ref in index.items:
            content = bundle_bytes(ref.bundle_id, run_payload=self.run_payload)
            yield admit_bundle(ref, content)


def _measure_busy_hour(root: str, results) -> None:
    now = datetime(2026, 8, 7, 1, 1, tzinfo=UTC)
    source = BusyHourSource()
    baseline_rss = peak_rss_bytes()
    summary = PipelineDriver(Path(root), source=source, clock=lambda: now).run(heal_days=1)
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


def _empty_projection(hour: datetime) -> HourProjection:
    return HourProjection(
        hour,
        hashlib.sha256(hour.isoformat().encode()).hexdigest(),
        {name: pa.Table.from_pylist([], schema=schema) for name, schema in table_schemas().items()},
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
    schema = table_schemas()
    day = date(2026, 8, 10)
    for hour_number in range(24):
        hour = datetime.combine(day, datetime.min.time(), UTC) + timedelta(hours=hour_number)
        projection = _empty_projection(hour)
        if hour_number == 0:
            quarantine = pa.Table.from_pylist(
                [
                    {
                        "source_hour": hour.strftime("%Y-%m-%dT%H"),
                        "source_day": day.isoformat(),
                        "bundle_id": "fixture-bundle",
                        "run_id": None,
                        "stage": "bundle_validation",
                        "reason_code": "fixture",
                        "raw_run": False,
                        "discarded_unknown_hero": False,
                        "discarded_unknown_final_rank": False,
                        "first_seen_at": "2026-08-10T01:00:00Z",
                        "decoder_code_version": "fixture",
                        "diagnostic_json": os.urandom(6 * 1024 * 1024).hex(),
                    }
                ],
                schema=schema["quarantine"],
            )
            projection = HourProjection(
                hour,
                projection.raw_commit_sha256,
                {**projection.tables, "quarantine": quarantine},
            )
        store.commit_hour(projection)
    store.seal_day(day)

    files = [path for path in tmp_path.rglob("*") if path.is_file()]
    parquet_bytes = sum(path.stat().st_size for path in files if path.suffix == ".parquet")
    total_bytes = sum(path.stat().st_size for path in files)
    assert parquet_bytes > 0
    assert total_bytes <= parquet_bytes * 1.01
    assert not list(tmp_path.rglob("*.bundle"))
    assert not (tmp_path / "raw").exists()


def test_healing_is_oldest_first_and_settlement_is_a_pure_boundary() -> None:
    now = datetime(2026, 8, 9, 1, 1, tzinfo=UTC)

    days = healing_days(now, 30)
    assert days[0] == date(2026, 7, 11)
    assert days[-1] == date(2026, 8, 9)
    assert len(days) == 30
    assert healing_days(now, 30, source_epoch=date(2026, 8, 7)) == (
        date(2026, 8, 7),
        date(2026, 8, 8),
        date(2026, 8, 9),
    )
    hour = datetime(2026, 8, 9, tzinfo=UTC)
    assert not is_hour_settled(hour, datetime(2026, 8, 9, 1, 0, 59, tzinfo=UTC))
    assert is_hour_settled(hour, datetime(2026, 8, 9, 1, 1, tzinfo=UTC))
