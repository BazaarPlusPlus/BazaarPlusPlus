import json
from datetime import UTC, date, datetime
from pathlib import Path

import httpx

from bppanalyzer.bundle_source import BundleSource
from bppanalyzer.driver import PipelineDriver
from tests.pipeline_fixtures import (
    API_BASE_URL,
    SYNC_TOKEN,
    BundleServer,
    daily_bundles,
    fixed_clock,
)


def test_seven_day_build_records_bounded_rss_and_local_contract_state(tmp_path: Path) -> None:
    # Source Days 08-04..08-10 hold Bundles. At 00:30 on 08-11, heal_days=8 covers
    # 08-04..08-11; every hour stays inside the eight-day Bundle Source retention.
    now = datetime(2026, 8, 11, 0, 30, tzinfo=UTC)
    server = BundleServer(daily_bundles(date(2026, 8, 4), 7), now=now)
    with httpx.Client(transport=httpx.MockTransport(server)) as client:
        summary = PipelineDriver(
            tmp_path,
            source=BundleSource(
                api_base_url=API_BASE_URL,
                sync_token=SYNC_TOKEN,
                client=client,
                clock=fixed_clock(now),
            ),
            clock=fixed_clock(now),
            duckdb_memory_limit="1GB",
            duckdb_threads=4,
        ).run(heal_days=8, publish=False)

    assert summary.peak_rss_bytes < 8 * 1024**3
    status = json.loads((tmp_path / "status.json").read_bytes())
    assert status["peak_rss_bytes"] < 8 * 1024**3
    assert status["last_run"]["report"]["window"]["days"] == 7
    assert set(
        path.relative_to(tmp_path / "snapshots").as_posix()
        for path in (tmp_path / "snapshots").rglob("*.json")
    ) == {
        "heroes/latest.json",
        "builds/latest.json",
    }
    heroes = json.loads((tmp_path / "snapshots/heroes/latest.json").read_bytes())
    days = [item["day"] for item in heroes["days"]]
    assert days == sorted(days, reverse=True)
    assert len(set(days)) == len(days) == 7
