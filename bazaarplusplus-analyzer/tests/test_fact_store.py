from datetime import UTC, datetime
from pathlib import Path

import pyarrow as pa
import pytest

from bppanalyzer.fact_store import FactConflict, FactCorrupt, FactMissing, FactStore
from bppanalyzer.projection import HourProjection, table_schemas

HOUR = datetime(2026, 8, 10, 12, tzinfo=UTC)


def _empty_hour(hour: datetime = HOUR) -> HourProjection:
    return HourProjection(
        source_hour=hour,
        raw_commit_sha256="a" * 64,
        tables={
            name: pa.Table.from_pylist([], schema=schema)
            for name, schema in table_schemas().items()
        },
    )


def test_commit_refuses_promotion_when_a_written_parquet_differs_from_its_recorded_hash(
    tmp_path: Path,
) -> None:
    def corrupt(seam: str, stage: Path) -> None:
        if seam == "before_precommit_verify":
            with (stage / "runs.parquet").open("ab") as stream:
                stream.write(b"corruption")

    store = FactStore(tmp_path, fault_injector=corrupt)

    with pytest.raises(FactCorrupt, match="checksum"):
        store.commit_hour(_empty_hour())

    assert not (tmp_path / "facts/hourly/source_hour=2026-08-10T12").exists()


def test_identical_recommit_is_reused_but_different_canonical_commit_is_a_hard_conflict(
    tmp_path: Path,
) -> None:
    store = FactStore(tmp_path)
    original = _empty_hour()

    first = store.commit_hour(original)
    second = store.commit_hour(original)

    assert first.reused is False
    assert second.reused is True
    assert second.fact_commit_sha256 == first.fact_commit_sha256
    changed = HourProjection(
        source_hour=HOUR,
        raw_commit_sha256="b" * 64,
        tables=original.tables,
    )
    with pytest.raises(FactConflict, match="commit conflict"):
        store.commit_hour(changed)
    assert store.commit_hour(original).fact_commit_sha256 == first.fact_commit_sha256


def test_seal_requires_exactly_24_independently_verified_hours(tmp_path: Path) -> None:
    store = FactStore(tmp_path)
    for hour_number in range(23):
        store.commit_hour(_empty_hour(datetime(2026, 8, 10, hour_number, tzinfo=UTC)))

    with pytest.raises(FactMissing, match="2026-08-10T23"):
        store.seal_day("2026-08-10")

    store.commit_hour(_empty_hour(datetime(2026, 8, 10, 23, tzinfo=UTC)))
    damaged = tmp_path / "facts/hourly/source_hour=2026-08-10T07/quality.parquet"
    damaged.write_bytes(damaged.read_bytes()[:-1])

    with pytest.raises(FactCorrupt, match="size differs"):
        store.seal_day("2026-08-10")


def test_verify_rehashes_parquet_and_hour_paths_expose_only_a_sealed_window(
    tmp_path: Path,
) -> None:
    store = FactStore(tmp_path)
    for hour_number in range(24):
        store.commit_hour(_empty_hour(datetime(2026, 8, 10, hour_number, tzinfo=UTC)))
    store.seal_day("2026-08-10")

    paths = store.hour_paths(["2026-08-10"])
    assert set(paths) == {"runs", "battles", "battle_cards", "quality", "quarantine"}
    assert all(len(values) == 24 for values in paths.values())
    assert store.verify("2026-08-10").files_verified == 120

    damaged = tmp_path / "facts/hourly/source_hour=2026-08-10T07/runs.parquet"
    content = bytearray(damaged.read_bytes())
    content[len(content) // 2] ^= 1
    damaged.write_bytes(content)

    with pytest.raises(FactCorrupt, match="checksum differs"):
        store.verify("2026-08-10")


def test_prune_keeps_eight_latest_sealed_days_and_newer_partial_hours(tmp_path: Path) -> None:
    from tests.release_fixtures import sealed_store

    store = sealed_store(tmp_path, 9)
    partial = datetime(2026, 8, 16, 0, tzinfo=UTC)
    store.commit_hour(_empty_hour(partial))

    report = store.prune(retain_days=8)

    assert report.source_days == ("2026-08-07",)
    assert report.hours_pruned == 24
    assert [seal.source_day for seal in store.seals()] == [
        "2026-08-08",
        "2026-08-09",
        "2026-08-10",
        "2026-08-11",
        "2026-08-12",
        "2026-08-13",
        "2026-08-14",
        "2026-08-15",
    ]
    assert store.committed_hours()[0] == "2026-08-08T00"
    assert store.committed_hours()[-1] == "2026-08-16T00"
    assert store.verify().hours_verified == 8 * 24
