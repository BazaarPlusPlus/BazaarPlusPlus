import json
from datetime import UTC, datetime

from bppanalyzer.driver import PipelineDriver
from bppanalyzer.publication import SnapshotBuilder, select_analysis_window


class NeverSource:
    def hour_index(self, _source_hour):
        raise AssertionError("Sealed fixture days must not reach the Bundle Server")

    def stream(self, _index):
        raise AssertionError("Sealed fixture days must not reach the Bundle Server")


def test_same_input_produces_stable_ordering_and_business_content(
    canonical_fact_store,
) -> None:
    root, store = canonical_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None

    def clock() -> datetime:
        return datetime(2026, 8, 14, 2, tzinfo=UTC)

    first = SnapshotBuilder(root, store=store, clock=clock, threads=4)
    second = SnapshotBuilder(root, store=store, clock=clock, threads=4)

    assert first.build_heroes(window).content == second.build_heroes(window).content
    assert first.build_builds(window).content == second.build_builds(window).content


def test_seven_day_build_records_bounded_rss_and_local_contract_state(
    canonical_fact_store,
) -> None:
    root, _store = canonical_fact_store
    driver = PipelineDriver(
        root,
        source=NeverSource(),
        clock=lambda: datetime(2026, 8, 13, 23, 59, tzinfo=UTC),
        duckdb_threads=4,
    )

    summary = driver.run(heal_days=7, publish=False)

    assert summary.peak_rss_bytes < 8 * 1024**3
    status = json.loads((root / "status.json").read_bytes())
    assert status["peak_rss_bytes"] < 8 * 1024**3
    assert status["last_run"]["report"]["window"]["days"] == 7
    assert set(
        path.relative_to(root / "snapshots").as_posix()
        for path in (root / "snapshots").rglob("*.json")
    ) == {
        "heroes/latest.json",
        "builds/latest.json",
    }
