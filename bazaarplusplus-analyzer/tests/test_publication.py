import json
from datetime import date, timedelta
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator

from bppanalyzer.fact_store import DaySeal, FactStore
from bppanalyzer.publication import (
    BUILDS_KEY,
    HEROES_KEY,
    AnalysisWindowError,
    BuildRank,
    LatestPublisher,
    SnapshotBuilder,
    select_analysis_window,
    select_build_identities,
)
from tests.fakes import MemoryObjectStore


def _builder(root: Path, store: FactStore, **kwargs) -> SnapshotBuilder:
    return SnapshotBuilder(root, store=store, memory_limit="1GB", threads=1, **kwargs)


def _seal(day: date) -> DaySeal:
    return DaySeal(day.isoformat(), (), {}, day.isoformat(), reused=True)


@pytest.mark.parametrize("count", (1, 5, 7))
def test_analysis_window_grows_from_one_to_seven_complete_source_days(count: int) -> None:
    start = date(2026, 8, 1)
    seals = tuple(_seal(start + timedelta(days=offset)) for offset in range(count))

    selected = select_analysis_window(seals)

    assert selected is not None
    assert selected.start == start
    assert selected.end == start + timedelta(days=count - 1)
    assert len(selected.seals) == count
    assert selected.value["days"] == count


def test_analysis_window_keeps_only_the_latest_seven_consecutive_days() -> None:
    start = date(2026, 8, 1)
    seals = tuple(_seal(start + timedelta(days=offset)) for offset in range(9))

    selected = select_analysis_window(seals)

    assert selected is not None
    assert selected.start == date(2026, 8, 3)
    assert selected.end == date(2026, 8, 9)
    assert len(selected.seals) == 7
    with pytest.raises(AnalysisWindowError, match="Duplicate"):
        select_analysis_window((*seals, seals[0]))


def test_explicit_window_anchor_uses_the_available_consecutive_suffix() -> None:
    start = date(2026, 8, 1)
    seals = tuple(_seal(start + timedelta(days=offset)) for offset in range(8))

    selected = select_analysis_window(seals, start + timedelta(days=5))
    assert selected is not None
    assert selected.start == start
    assert selected.end == start + timedelta(days=5)
    assert selected.value["days"] == 6

    selected = select_analysis_window(seals, start + timedelta(days=7))
    assert selected is not None
    assert selected.start == start + timedelta(days=1)
    assert selected.end == start + timedelta(days=7)


def test_analysis_window_never_considers_seals_before_the_source_epoch() -> None:
    start = date(2026, 8, 1)
    seals = tuple(_seal(start + timedelta(days=offset)) for offset in range(9))

    selected = select_analysis_window(seals, source_epoch=date(2026, 8, 7))

    assert selected is not None
    assert selected.start == date(2026, 8, 7)
    assert selected.end == date(2026, 8, 9)
    assert selected.value["days"] == 3
    assert (
        select_analysis_window(
            seals,
            anchor_day=date(2026, 8, 6),
            source_epoch=date(2026, 8, 7),
        )
        is None
    )


def test_heroes_schema_requires_days_length_to_equal_window_days(
    canonical_fact_store,
) -> None:
    root, store = canonical_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None
    payload = json.loads(_builder(root, store).build_heroes(window).content)
    payload["window"]["days"] = 6
    schema = json.loads(Path("contracts/v5/heroes.schema.json").read_bytes())

    assert list(Draft202012Validator(schema).iter_errors(payload))


def test_latest_publisher_validates_and_only_writes_the_two_public_keys(
    tmp_path: Path, canonical_fact_store
) -> None:
    root, store = canonical_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None
    builder = _builder(root, store)
    heroes = builder.build_heroes(window)
    builds = builder.build_builds(window)
    objects = MemoryObjectStore()
    publisher = LatestPublisher(objects)

    assert publisher.replace(heroes) is True
    assert publisher.replace(builds) is True

    assert objects.put_keys() == [
        HEROES_KEY,
        BUILDS_KEY,
    ]
    for key in (HEROES_KEY, BUILDS_KEY):
        observed = objects.get(key)
        assert observed is not None
        assert observed.stat.cache_control == "public,max-age=60,must-revalidate"
        assert observed.stat.content_type == "application/json"


def test_product_validation_failure_preserves_old_object_and_does_not_block_other_product(
    canonical_fact_store,
) -> None:
    objects = MemoryObjectStore()
    objects.put(
        HEROES_KEY,
        b'{"old":"heroes"}\n',
        cache_control="public,max-age=60,must-revalidate",
        content_type="application/json",
    )
    publisher = LatestPublisher(objects)
    root, store = canonical_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None
    builds = _builder(root, store).build_builds(window)

    assert publisher.replace(builds) is True

    assert objects.get(HEROES_KEY).body == b'{"old":"heroes"}\n'
    assert objects.get(BUILDS_KEY).body == builds.content


def test_top_500_then_coverage_appends_only_highest_ranked_uncovered_build() -> None:
    common = tuple(f"10000000-0000-0000-0000-{number:012d}" for number in range(500))
    rare = "ffffffff-ffff-ffff-ffff-ffffffffffff"
    candidates = [BuildRank((card_id,), score=100, ten_win=1, p75=10) for card_id in common]
    candidates.append(BuildRank((rare,), score=100, ten_win=1, p75=10))
    candidates.append(BuildRank((rare, rare), score=100, ten_win=1, p75=10))

    selected = select_build_identities(candidates)

    assert len(candidates) == 502
    assert len(selected) == 501
    containing = [identity for identity in selected if rare in identity]
    assert containing == [(rare,)]
