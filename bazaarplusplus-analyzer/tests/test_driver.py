import hashlib
import json
from datetime import UTC, date, datetime
from pathlib import Path

import httpx
import pytest

from bppanalyzer.bundle_source import (
    BundleRef,
    BundleSource,
    HourExpired,
    RawHourIndex,
    RetryableSourceError,
    raw_commit_sha256,
)
from bppanalyzer.driver import PipelineDriver
from bppanalyzer.fact_store import FactStore
from bppanalyzer.object_store import LocalObjectStore
from bppanalyzer.operational_evidence import read_status
from bppanalyzer.publication import BUILDS_KEY, HEROES_KEY
from tests.bundle_fixtures import bundle_bytes


class NeverSource:
    def hour_index(self, _source_hour):
        raise AssertionError("Sealed days must not reach the Bundle Server")

    def stream(self, _index):
        raise AssertionError("Sealed days must not reach the Bundle Server")


class InvalidBundleSource:
    def hour_index(self, source_hour: datetime) -> RawHourIndex:
        milliseconds = int(source_hour.timestamp() * 1000)
        item = BundleRef(
            bundle_id="bad-bundle",
            available_at_ms=milliseconds,
            download_url="https://download.invalid/bad",
            download_expires_at_ms=milliseconds + 3_600_000,
            sha256=None,
            bytes=None,
        )
        return RawHourIndex(source_hour, (item,), raw_commit_sha256((item,)), 1)

    def stream(self, _index: RawHourIndex):
        raise RetryableSourceError("bundle_validation_failed", "fixture Bundle validation failed")


class ExpiredSource:
    def hour_index(self, _source_hour):
        raise HourExpired("source_hour_expired", "fixture expired")

    def stream(self, _index):
        raise AssertionError("An expired index must never be streamed")


class UnexpectedSource:
    def hour_index(self, _source_hour):
        raise RuntimeError("fixture unexpected failure")

    def stream(self, _index):
        raise AssertionError("A failed index must never be streamed")


class RecordingExpiredSource:
    def __init__(self) -> None:
        self.requested_hours: list[datetime] = []

    def hour_index(self, source_hour: datetime):
        self.requested_hours.append(source_hour)
        raise HourExpired("source_hour_expired", "fixture expired")

    def stream(self, _index):
        raise AssertionError("An expired index must never be streamed")


def test_one_complete_day_publishes_a_one_day_window(tmp_path: Path) -> None:
    from tests.release_fixtures import sealed_store

    root = tmp_path / "facts"
    sealed_store(root, 1)
    objects = LocalObjectStore(tmp_path / "objects")

    summary = PipelineDriver(
        root,
        source=NeverSource(),
        clock=lambda: datetime(2026, 8, 7, 23, 59, tzinfo=UTC),
        object_store=objects,
    ).run(heal_days=1, anchor_day=date(2026, 8, 7))

    assert summary.exit_code == 0
    assert summary.report["window"] == {
        "start": "2026-08-07",
        "end": "2026-08-07",
        "days": 1,
    }
    assert [request.key for request in objects.requests if request.operation == "put"] == [
        HEROES_KEY,
        BUILDS_KEY,
    ]


def test_successful_publication_prunes_facts_to_eight_latest_sealed_days(
    tmp_path: Path,
) -> None:
    from tests.release_fixtures import sealed_store

    root = tmp_path / "facts"
    sealed_store(root, 9)
    objects = LocalObjectStore(tmp_path / "objects")

    summary = PipelineDriver(
        root,
        source=NeverSource(),
        clock=lambda: datetime(2026, 8, 15, 23, 59, tzinfo=UTC),
        object_store=objects,
    ).run(heal_days=9)

    assert summary.exit_code == 0
    assert summary.report["window"] == {
        "start": "2026-08-09",
        "end": "2026-08-15",
        "days": 7,
    }
    assert summary.report["retention"]["source_days_pruned"] == 1
    assert summary.report["retention"]["hours_pruned"] == 24
    assert summary.report["retention"]["files_pruned"] == 145
    assert summary.report["retention"]["bytes_pruned"] > 0
    assert [seal.source_day for seal in FactStore(root).seals()] == [
        "2026-08-08",
        "2026-08-09",
        "2026-08-10",
        "2026-08-11",
        "2026-08-12",
        "2026-08-13",
        "2026-08-14",
        "2026-08-15",
    ]
    assert FactStore(root).verify(deep=True).hours_verified == 8 * 24


def test_driver_honors_a_longer_fact_retention_window(tmp_path: Path) -> None:
    from tests.release_fixtures import sealed_store

    root = tmp_path / "facts"
    sealed_store(root, 9)

    summary = PipelineDriver(
        root,
        source=NeverSource(),
        clock=lambda: datetime(2026, 8, 15, 23, 59, tzinfo=UTC),
        object_store=LocalObjectStore(tmp_path / "objects"),
        fact_retention_days=9,
    ).run(heal_days=9)

    assert summary.exit_code == 0
    assert len(FactStore(root).seals()) == 9


def test_source_epoch_prevents_pre_epoch_days_from_being_healed_or_considered(
    tmp_path: Path,
) -> None:
    source = RecordingExpiredSource()

    summary = PipelineDriver(
        tmp_path,
        source=source,
        source_epoch=date(2026, 8, 7),
        clock=lambda: datetime(2026, 8, 9, 1, 1, tzinfo=UTC),
    ).run(heal_days=5)

    assert [hour.date() for hour in source.requested_hours] == [
        date(2026, 8, 7),
        date(2026, 8, 8),
        date(2026, 8, 9),
    ]
    assert summary.report["window"] is None
    status = read_status(tmp_path)
    assert all(day >= "2026-08-07" for day in status["facts"]["sealed_days"])
    assert all(item["source_day"] >= "2026-08-07" for item in status["facts"]["abandoned_days"])


def test_driver_publishes_exactly_two_objects_and_records_the_structured_run_report(
    tmp_path: Path, canonical_fact_store
) -> None:
    root, _store = canonical_fact_store
    objects = LocalObjectStore(tmp_path / "objects")

    summary = PipelineDriver(
        root,
        source=NeverSource(),
        clock=lambda: datetime(2026, 8, 13, 23, 59, tzinfo=UTC),
        object_store=objects,
    ).run(heal_days=7)

    assert summary.exit_code == 0
    assert summary.report == {
        "window": {"start": "2026-08-07", "end": "2026-08-13", "days": 7},
        "downloads": {
            "expected_bundles": 0,
            "succeeded_bundles": 0,
            "failed_bundles": 0,
            "listing_pages": 0,
            "listing_requests": 0,
            "listing_retries": 0,
            "download_attempts": 0,
            "download_retries": 0,
            "downloaded_bytes": 0,
            "download_latency_ms_p50": None,
            "download_latency_ms_p95": None,
        },
        "facts": {
            "raw_runs": 7,
            "discarded_unknown_hero": 0,
            "discarded_unknown_final_rank": 0,
            "included_runs": 7,
            "included_battles": 56,
        },
        "heroes": {
            "participating_runs": 7,
            "participating_matchup_battles": 56,
            "published": True,
        },
        "builds": {
            "eligible_layout_runs": 7,
            "candidate_builds": 1,
            "published_builds": 1,
            "published": True,
        },
        "retention": {
            "source_days_pruned": 0,
            "hours_pruned": 0,
            "files_pruned": 0,
            "bytes_pruned": 0,
        },
    }
    assert [request.key for request in objects.requests if request.operation == "put"] == [
        HEROES_KEY,
        BUILDS_KEY,
    ]
    status = json.loads((root / "status.json").read_bytes())
    assert status["last_run"]["report"] == summary.report
    assert status["publication"] == {
        "heroes": {"present": True, "window_end": "2026-08-13"},
        "builds": {"present": True, "window_end": "2026-08-13"},
    }
    assert json.loads((root / "runs.jsonl").read_text().splitlines()[-1]) == status["last_run"]
    log = "".join(path.read_text() for path in (root / "logs").glob("*.log"))
    assert 'run report: {"builds":' in log


def test_one_product_failure_preserves_it_but_the_other_product_still_updates(
    tmp_path: Path, canonical_fact_store
) -> None:
    root, _store = canonical_fact_store
    objects = LocalObjectStore(tmp_path / "objects")
    old_heroes = b'{"old":"heroes"}\n'
    objects.put(
        HEROES_KEY,
        old_heroes,
        cache_control="public,max-age=60,must-revalidate",
        content_type="application/json",
    )

    def fail_heroes(product: str, stage: str) -> None:
        if product == "heroes" and stage == "after_build":
            raise RuntimeError("fixture heroes failure")

    summary = PipelineDriver(
        root,
        source=NeverSource(),
        clock=lambda: datetime(2026, 8, 13, 23, 59, tzinfo=UTC),
        object_store=objects,
        publication_fault_injector=fail_heroes,
    ).run(heal_days=7)

    assert summary.outcome == "partial"
    assert summary.exit_code == 4
    assert summary.report["heroes"]["published"] is False
    assert summary.report["builds"]["published"] is True
    assert objects.get(HEROES_KEY).body == old_heroes
    assert objects.get(BUILDS_KEY) is not None


def test_one_product_failure_does_not_prune_old_facts(tmp_path: Path) -> None:
    from tests.release_fixtures import sealed_store

    root = tmp_path / "facts"
    sealed_store(root, 9)

    def fail_heroes(product: str, stage: str) -> None:
        if product == "heroes" and stage == "after_build":
            raise RuntimeError("fixture heroes failure")

    summary = PipelineDriver(
        root,
        source=NeverSource(),
        clock=lambda: datetime(2026, 8, 15, 23, 59, tzinfo=UTC),
        object_store=LocalObjectStore(tmp_path / "objects"),
        publication_fault_injector=fail_heroes,
    ).run(heal_days=9)

    assert summary.exit_code == 4
    assert len(FactStore(root).seals()) == 9
    assert FactStore(root).committed_hours()[0] == "2026-08-07T00"


def test_failed_bundle_is_reported_and_its_source_hour_remains_incomplete(
    tmp_path: Path,
) -> None:
    now = datetime(2026, 8, 7, 1, 1, tzinfo=UTC)

    summary = PipelineDriver(
        tmp_path,
        source=InvalidBundleSource(),
        clock=lambda: now,
    ).run(heal_days=1)

    assert summary.outcome == "partial"
    assert summary.exit_code == 4
    assert summary.report["downloads"] == {
        "expected_bundles": 1,
        "succeeded_bundles": 0,
        "failed_bundles": 1,
        "listing_pages": 1,
        "listing_requests": 0,
        "listing_retries": 0,
        "download_attempts": 0,
        "download_retries": 0,
        "downloaded_bytes": 0,
        "download_latency_ms_p50": None,
        "download_latency_ms_p95": None,
    }
    assert summary.report["window"] is None
    assert not (tmp_path / "facts/hourly/source_hour=2026-08-07T00").exists()
    status = json.loads((tmp_path / "status.json").read_bytes())
    assert "2026-08-07T00" in status["facts"]["incomplete_days"][0]["missing_hours"]


def test_run_summary_records_low_cardinality_source_performance(tmp_path: Path) -> None:
    now = datetime(2026, 8, 7, 1, 1, tzinfo=UTC)
    content = bundle_bytes("bundle-a")
    download_attempts = 0
    sleeps: list[float] = []

    def server(request: httpx.Request) -> httpx.Response:
        nonlocal download_attempts
        if request.url.host == "api.invalid":
            timestamp = int(datetime(2026, 8, 7, tzinfo=UTC).timestamp() * 1_000)
            return httpx.Response(
                200,
                json={
                    "window": {
                        "available_from_ms": timestamp,
                        "available_before_ms": timestamp + 3_600_000,
                    },
                    "items": [
                        {
                            "bundle_id": "bundle-a",
                            "available_at_ms": timestamp,
                            "download_url": "https://download.invalid/bundle-a",
                            "download_expires_at_ms": timestamp + 60_000,
                            "sha256": hashlib.sha256(content).hexdigest(),
                            "bytes": len(content),
                        }
                    ],
                    "next_after": None,
                },
            )
        download_attempts += 1
        if download_attempts == 1:
            return httpx.Response(503)
        return httpx.Response(200, content=content)

    source = BundleSource(
        api_base_url="https://api.invalid",
        sync_token="test-token",
        client=httpx.Client(transport=httpx.MockTransport(server)),
        clock=lambda: now,
        sleep=sleeps.append,
        jitter=lambda delay: delay,
    )

    summary = PipelineDriver(tmp_path, source=source, clock=lambda: now).run(heal_days=1)

    downloads = summary.report["downloads"]
    assert {
        key: downloads[key]
        for key in (
            "expected_bundles",
            "succeeded_bundles",
            "failed_bundles",
            "listing_pages",
            "listing_requests",
            "listing_retries",
            "download_attempts",
            "download_retries",
            "downloaded_bytes",
        )
    } == {
        "expected_bundles": 1,
        "succeeded_bundles": 1,
        "failed_bundles": 0,
        "listing_pages": 1,
        "listing_requests": 1,
        "listing_retries": 0,
        "download_attempts": 2,
        "download_retries": 1,
        "downloaded_bytes": len(content),
    }
    assert isinstance(downloads["download_latency_ms_p50"], float)
    assert isinstance(downloads["download_latency_ms_p95"], float)
    assert 0 <= downloads["download_latency_ms_p50"] <= downloads["download_latency_ms_p95"]
    assert sleeps == [1.0]
    assert summary.timings["retry_sleep_seconds"] == 1.0
    for name in (
        "source_index_seconds",
        "source_ingest_seconds",
        "download_wait_seconds",
        "batch_generation_seconds",
        "projection_seconds",
        "parquet_write_seconds",
        "fact_finalize_seconds",
    ):
        assert summary.timings[name] >= 0
    status = json.loads((tmp_path / "status.json").read_bytes())
    assert status["last_run"]["timings"] == summary.timings
    log = "".join(path.read_text() for path in (tmp_path / "logs").glob("*.log"))
    assert 'performance report: {"downloads":' in log


def test_no_publish_writes_valid_local_snapshots_without_object_store_calls(
    tmp_path: Path,
) -> None:
    from tests.release_fixtures import sealed_store

    root = tmp_path / "facts"
    sealed_store(root, 9)
    objects = LocalObjectStore(tmp_path / "objects")

    summary = PipelineDriver(
        root,
        source=NeverSource(),
        clock=lambda: datetime(2026, 8, 15, 23, 59, tzinfo=UTC),
        object_store=objects,
    ).run(heal_days=9, publish=False)

    assert summary.report["heroes"]["published"] is False
    assert summary.report["builds"]["published"] is False
    assert objects.requests == []
    assert (root / "snapshots/heroes/latest.json").is_file()
    assert (root / "snapshots/builds/latest.json").is_file()
    assert len(FactStore(root).seals()) == 9
    assert FactStore(root).committed_hours()[0] == "2026-08-07T00"


def test_expired_hour_abandons_the_day_and_is_visible_in_status(tmp_path: Path) -> None:
    now = datetime(2026, 8, 7, 1, 1, tzinfo=UTC)

    summary = PipelineDriver(tmp_path, source=ExpiredSource(), clock=lambda: now).run(heal_days=1)

    assert summary.exit_code == 0
    status = read_status(tmp_path)
    assert status["facts"]["abandoned_days"][0]["source_day"] == "2026-08-07"
    assert status["facts"]["abandoned_days"][0]["reason"] == "source_hour_expired"


def test_unexpected_failure_is_recorded_before_it_is_reraised(tmp_path: Path) -> None:
    now = datetime(2026, 8, 7, 1, 1, tzinfo=UTC)

    with pytest.raises(RuntimeError, match="fixture unexpected failure"):
        PipelineDriver(tmp_path, source=UnexpectedSource(), clock=lambda: now).run(heal_days=1)

    status = read_status(tmp_path)
    assert status["last_run"]["outcome"] == "error"
    assert status["last_run"]["failures"][-1] == {
        "scope": "run",
        "reason": "fixture unexpected failure",
    }
