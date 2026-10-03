"""Bundle Server → facts → seal → heroes/builds snapshots, compared byte for byte.

``just analyzer::golden`` rewrites the three goldens; ``just analyzer::test`` only
compares. The snapshot goldens are shared consumer fixtures under
``contracts/v5/fixtures/``; the run report is run evidence and stays in
``tests/fixtures/``.
"""

import json
from datetime import UTC, datetime
from pathlib import Path

import httpx

from bppanalyzer.bundle_source import BundleSource
from bppanalyzer.driver import PipelineDriver
from bppanalyzer.durable import canonical_json
from tests.pipeline_fixtures import (
    API_BASE_URL,
    CONTRACT_FIXTURES,
    SYNC_TOKEN,
    TEST_FIXTURES,
    BundleServer,
    assert_golden,
    fixed_clock,
    golden_day_bundles,
)

# Source Day 2026-08-10 is the only day with Bundles. At 00:30 on 08-11,
# healing_days(now, 2) is [08-10, 08-11]. is_hour_settled requires
# now >= hour + 1h + 60s, so all 24 Source Hours of 08-10 settle and the day is
# sealed, while 08-11 has no settled hour and stays incomplete. The Analysis
# Window is therefore exactly one day.
CLOCK = datetime(2026, 8, 11, 0, 30, tzinfo=UTC)
WALL_CLOCK_DOWNLOAD_FIELDS = ("download_latency_ms_p50", "download_latency_ms_p95")


def test_one_bundle_day_reproduces_the_committed_snapshots_and_run_report(
    tmp_path: Path,
) -> None:
    with httpx.Client(
        transport=httpx.MockTransport(BundleServer(golden_day_bundles(), now=CLOCK))
    ) as client:
        summary = PipelineDriver(
            tmp_path,
            source=BundleSource(
                api_base_url=API_BASE_URL,
                sync_token=SYNC_TOKEN,
                client=client,
                clock=fixed_clock(CLOCK),
            ),
            clock=fixed_clock(CLOCK),
            # Fixed DuckDB resources keep the snapshot bytes reproducible.
            duckdb_memory_limit="1GB",
            duckdb_threads=1,
        ).run(heal_days=2, publish=False)

    assert summary.failures == ()
    report = json.loads(json.dumps(summary.report))
    for field in WALL_CLOCK_DOWNLOAD_FIELDS:
        del report["downloads"][field]
    heroes = (tmp_path / "snapshots/heroes/latest.json").read_bytes()
    builds = (tmp_path / "snapshots/builds/latest.json").read_bytes()

    # A golden that proves nothing must never be committed.
    assert report["window"] == {"start": "2026-08-10", "end": "2026-08-10", "days": 1}
    assert report["builds"]["published_builds"] >= 1
    assert report["facts"]["discarded_unknown_hero"] > 0
    assert report["facts"]["discarded_unknown_final_rank"] > 0
    assert any(
        matchup["decided"] > 0
        for day in json.loads(heroes)["days"]
        for row in day["rows"]
        for matchup in row["matchups"]
    )

    assert_golden(CONTRACT_FIXTURES / "heroes.latest.json", heroes)
    assert_golden(CONTRACT_FIXTURES / "builds.latest.json", builds)
    assert_golden(TEST_FIXTURES / "pipeline-golden.run-report.json", canonical_json(report))
