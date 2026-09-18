import json
from datetime import UTC, date, datetime, timedelta
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator

from bppanalyzer.fact_store import DaySeal, canonical_json
from bppanalyzer.object_store import LocalObjectStore
from bppanalyzer.publication import (
    BUILDS_KEY,
    HEROES_KEY,
    AnalysisWindow,
    AnalysisWindowError,
    BuildRank,
    LatestPublisher,
    SnapshotBuilder,
    select_analysis_window,
    select_build_identities,
    validate_snapshot,
    wilson_score,
)
from tests.release_fixtures import CARD_IDS, sealed_store_with_rows


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


def test_heroes_snapshot_is_daily_additive_and_matches_the_strict_contract(
    canonical_fact_store,
) -> None:
    root, store = canonical_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None

    built = SnapshotBuilder(
        root,
        store=store,
        clock=lambda: datetime(2026, 8, 14, 2, tzinfo=UTC),
        threads=4,
    ).build_heroes(window)
    payload = json.loads(built.content)

    validate_snapshot("heroes", payload)
    schema = json.loads((Path("contracts/v5/heroes.schema.json")).read_bytes())
    Draft202012Validator(schema).validate(payload)
    assert built.content == canonical_json(payload)
    assert payload["schema_version"] == 1
    assert payload["kind"] == "hero_metrics"
    assert payload["window"] == {"start": "2026-08-07", "end": "2026-08-13", "days": 7}
    assert [item["day"] for item in payload["days"]] == [
        f"2026-08-{number:02d}" for number in range(13, 6, -1)
    ]
    assert all(len(item["rows"]) == 16 for item in payload["days"])
    assert {row["segment"] for day in payload["days"] for row in day["rows"]} == {
        "legend",
        "non_legend",
    }
    assert (
        sum(row["runs"]["completed"] for day in payload["days"] for row in day["rows"])
        == built.stats.participating_runs
    )
    assert "battle_days" not in built.content.decode()

    first_day = payload["days"][0]
    dooley = {row["segment"]: row for row in first_day["rows"] if row["hero"] == "Dooley"}
    all_completed = sum(row["runs"]["completed"] for row in dooley.values())
    assert all_completed == 1
    legend = dooley["legend"]
    assert legend["runs"] == {"completed": 1, "scored": 1, "ten_win": 1}
    assert legend["outcomes"] == {"perfect": 1, "gold": 0, "silver": 0, "bronze": 0}
    assert legend["ten_win_days"] == {"known_count": 1, "sum_days": 10}
    assert legend["matchups"][0] == {
        "opponent_hero": "Dooley",
        "decided": 1,
        "wins": 1,
        "losses": 0,
    }
    empty = next(row for row in first_day["rows"] if row["hero"] == "Jules")
    assert empty["runs"] == {"completed": 0, "scored": 0, "ten_win": 0}
    assert empty["matchups"] == []
    assert built.stats.participating_runs == 7
    assert built.stats.participating_matchup_battles == 56


def test_heroes_schema_requires_days_length_to_equal_window_days(
    canonical_fact_store,
) -> None:
    root, store = canonical_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None
    payload = json.loads(SnapshotBuilder(root, store=store).build_heroes(window).content)
    payload["window"]["days"] = 6
    schema = json.loads(Path("contracts/v5/heroes.schema.json").read_bytes())

    assert list(Draft202012Validator(schema).iter_errors(payload))


def test_builds_snapshot_matches_mod_schema_and_has_bidirectional_card_index(
    canonical_fact_store,
) -> None:
    root, store = canonical_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None

    built = SnapshotBuilder(
        root,
        store=store,
        clock=lambda: datetime(2026, 8, 14, 2, tzinfo=UTC),
    ).build_builds(window)
    payload = json.loads(built.content)

    validate_snapshot("builds", payload)
    schema = json.loads((Path("contracts/v5/builds.schema.json")).read_bytes())
    Draft202012Validator(schema).validate(payload)
    assert built.content == canonical_json(payload)
    assert payload["schema_version"] == 2
    assert payload["kind"] == "ten_win_builds"
    assert payload["schemas"] == {
        "build": ["card_refs", "layout", "stats"],
        "layout": ["card_ref", "slot", "tier", "enchant_ref", "size"],
        "stats": [
            "completed_run_count",
            "ten_win_run_count",
            "ten_win_rate_bps",
            "p75_ten_win_final_day",
            "score",
        ],
    }
    assert isinstance(payload["heroes"], dict)
    assert list(payload["heroes"]) == [
        "Dooley",
        "Jules",
        "Karnok",
        "Mak",
        "Pygmalien",
        "Stelle",
        "TheDragons",
        "Vanessa",
    ]
    assert payload["enchantments"][0] is None
    assert payload["cards"] == list(CARD_IDS)
    build = payload["heroes"]["Dooley"]["builds"][0]
    assert len(build) == 3
    assert build[0] == list(range(10))
    assert sum(item[4] for item in build[1]) == 10
    assert build[2] == [7, 4, 5714, 10, wilson_score(4, 7)]
    assert payload["heroes"]["Dooley"]["card_index"] == [[card_ref, [0]] for card_ref in range(10)]
    assert payload["heroes"]["Jules"] == {"builds": [], "card_index": []}
    assert built.stats.eligible_layout_runs == 7
    assert built.stats.candidate_builds == 1
    assert built.stats.published_builds == 1


def test_latest_publisher_validates_and_only_writes_the_two_public_keys(
    tmp_path: Path, canonical_fact_store
) -> None:
    root, store = canonical_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None
    builder = SnapshotBuilder(root, store=store)
    heroes = builder.build_heroes(window)
    builds = builder.build_builds(window)
    objects = LocalObjectStore(tmp_path / "objects")
    publisher = LatestPublisher(objects)

    assert publisher.replace(heroes) is True
    assert publisher.replace(builds) is True

    assert [request.key for request in objects.requests if request.operation == "put"] == [
        HEROES_KEY,
        BUILDS_KEY,
    ]
    for key in (HEROES_KEY, BUILDS_KEY):
        observed = objects.get(key)
        assert observed is not None
        assert observed.stat.cache_control == "public,max-age=60,must-revalidate"
        assert observed.stat.content_type == "application/json"


def test_product_validation_failure_preserves_old_object_and_does_not_block_other_product(
    tmp_path: Path, canonical_fact_store
) -> None:
    objects = LocalObjectStore(tmp_path / "objects")
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
    builds = SnapshotBuilder(root, store=store).build_builds(window)

    with pytest.raises(Exception):
        publisher.replace(type(builds)("heroes", HEROES_KEY, b"{}\n", builds.stats))
    assert publisher.replace(builds) is True

    assert objects.get(HEROES_KEY).body == b'{"old":"heroes"}\n'
    assert objects.get(BUILDS_KEY).body == builds.content


def _layout_run(
    index: int,
    card_ids: tuple[str, ...],
    sizes: tuple[int, ...],
    *,
    victories: int = 10,
    losses: int = 0,
    run_day: int | None = 10,
    slots: tuple[int, ...] | None = None,
    tier: str = "Gold",
    status: str = "Captured",
    final_count: int = 1,
    final_battle_id: str | None = None,
    socket_effect_slots: tuple[int, ...] = (),
) -> dict[str, list[dict[str, object]]]:
    day = "2026-08-07"
    hour = f"{day}T00"
    bundle_id = f"bundle-{index}"
    run_id = f"run-{index}"
    actual_final = f"battle-{index}-0"
    run = {
        "source_hour": hour,
        "source_day": day,
        "available_at_ms": index,
        "bundle_id": bundle_id,
        "bundle_sha256": "0" * 64,
        "run_id": run_id,
        "player_account_id": f"account-{index}",
        "hero": "Dooley",
        "status": "completed",
        "run_day": run_day,
        "victories": victories,
        "losses": losses,
        "final_rank": "Legendary",
        "final_battle_id": final_battle_id or actual_final,
        "final_player_item_signature": "1" * 64,
        "projection_code_version": "fixture",
    }
    battles = [
        {
            "source_hour": hour,
            "source_day": day,
            "available_at_ms": index,
            "bundle_id": bundle_id,
            "run_id": run_id,
            "battle_id": f"battle-{index}-{number}",
            "is_final_battle": True,
            "player_account_id": f"account-{index}",
            "player_hero": "Dooley",
            "opponent_hero": "Jules",
            "winner_combatant_id": "Player",
        }
        for number in range(final_count)
    ]
    if slots is None:
        tiled: list[int] = []
        cursor = 0
        for size in sizes:
            tiled.append(cursor)
            cursor += size
        observed_slots: tuple[int, ...] = tuple(tiled)
    else:
        observed_slots = slots
    cards = [
        {
            "source_hour": hour,
            "source_day": day,
            "available_at_ms": index,
            "bundle_id": bundle_id,
            "run_id": run_id,
            "battle_id": actual_final,
            "card_set_label": "player_hand",
            "card_set_status": status,
            "owner_side": "player",
            "card_kind": "item",
            "slot_index": slot,
            "instance_id": f"instance-{index}-{position}",
            "template_id": card_id,
            "size": sizes[position],
            "socket": slot,
            "tier": tier,
            "card_type": 0,
        }
        for position, (card_id, slot) in enumerate(zip(card_ids, observed_slots, strict=True))
    ]
    cards.extend(
        {
            "source_hour": hour,
            "source_day": day,
            "available_at_ms": index,
            "bundle_id": bundle_id,
            "run_id": run_id,
            "battle_id": actual_final,
            "card_set_label": "player_hand",
            "card_set_status": status,
            "owner_side": "player",
            "card_kind": "item",
            "slot_index": slot,
            "instance_id": f"effect-{index}-{position}",
            "template_id": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
            "name": "[Cooler] Socket Effect",
            "size": 1,
            "socket": slot,
            "tier": tier,
            "card_type": 7,
        }
        for position, slot in enumerate(socket_effect_slots)
    )
    return {"runs": [run], "battles": battles, "battle_cards": cards}


def _combine_rows(*parts: dict[str, list[dict[str, object]]]) -> dict[str, list[dict[str, object]]]:
    result: dict[str, list[dict[str, object]]] = {}
    for part in parts:
        for table, rows in part.items():
            result.setdefault(table, []).extend(rows)
    return result


@pytest.fixture(scope="module")
def policy_fact_store(tmp_path_factory: pytest.TempPathFactory):
    valid_ids = CARD_IDS[:2]
    identity = CARD_IDS[2:4]
    rows = _combine_rows(
        _layout_run(0, valid_ids, (5, 5)),
        _layout_run(1, valid_ids, (5, 5), final_count=2),
        _layout_run(2, valid_ids, (5, 5), final_battle_id="different-battle"),
        _layout_run(3, valid_ids, (5, 5), status="Missing"),
        _layout_run(4, ("not-a-card-id",), (10,)),
        _layout_run(5, valid_ids, (0, 10)),
        _layout_run(6, valid_ids, (4, 5)),
        _layout_run(7, valid_ids, (5, 5), slots=(0, 3)),
        _layout_run(8, valid_ids, (5, 5), slots=(2, 5)),
        _layout_run(9, valid_ids, (5, 5), socket_effect_slots=(0, 5)),
        _layout_run(10, identity, (5, 5), run_day=1, slots=(0, 5), tier="Gold"),
        _layout_run(11, identity, (5, 5), run_day=2, slots=(0, 5), tier="Gold"),
        _layout_run(12, identity, (5, 5), run_day=3, slots=(0, 5), tier="Diamond"),
        _layout_run(13, identity, (5, 5), run_day=100, slots=(0, 5), tier="Diamond"),
        _layout_run(14, identity, (5, 5), victories=8, losses=2, run_day=8),
    )
    for run in rows["runs"]:
        if int(str(run["run_id"]).split("-")[-1]) >= 10:
            run["hero"] = "Jules"
    rows["quarantine"] = [
        {
            "source_hour": "2026-08-07T00",
            "source_day": "2026-08-07",
            "bundle_id": "discarded-bundle",
            "run_id": "discarded-run",
            "stage": "fact_filter",
            "reason_code": "unaccepted_run",
            "raw_run": True,
            "discarded_unknown_hero": True,
            "discarded_unknown_final_rank": True,
            "first_seen_at": "2026-08-07T01:00:00Z",
            "decoder_code_version": "fixture",
            "diagnostic_json": "{}",
        }
    ]
    root = tmp_path_factory.mktemp("policy-facts")
    return root, sealed_store_with_rows(root, rows)


def test_build_eligibility_rejects_every_incomplete_final_layout_boundary(
    policy_fact_store,
) -> None:
    root, store = policy_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None

    built = SnapshotBuilder(root, store=store).build_builds(window)
    payload = json.loads(built.content)

    assert built.stats.eligible_layout_runs == 7
    assert built.stats.candidate_builds == 2
    assert built.stats.published_builds == 2
    assert len(payload["heroes"]["Dooley"]["builds"]) == 1


def test_fact_report_counts_each_rejection_reason_without_admitting_the_run(
    policy_fact_store,
) -> None:
    root, store = policy_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None

    stats = SnapshotBuilder(root, store=store).fact_stats(window)

    assert stats.raw_runs == 16
    assert stats.discarded_unknown_hero == 1
    assert stats.discarded_unknown_final_rank == 1
    assert stats.included_runs == 15
    assert stats.included_battles == 16


def test_representative_layout_mode_tie_break_and_nearest_rank_p75(
    policy_fact_store,
) -> None:
    root, store = policy_fact_store
    window = select_analysis_window(store.seals())
    assert window is not None

    payload = json.loads(SnapshotBuilder(root, store=store).build_builds(window).content)
    build = payload["heroes"]["Jules"]["builds"][0]

    assert [item[1] for item in build[1]] == [0, 5]
    assert [item[2] for item in build[1]] == [4, 4]
    assert build[2] == [5, 4, 8000, 3, wilson_score(4, 5)]
    assert wilson_score(0, 1) == 0
    assert wilson_score(1, 1) == 206543


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


@pytest.mark.parametrize("include_modern_hour", (True, False))
def test_fact_report_tolerates_legacy_quarantine_schema(
    tmp_path_factory: pytest.TempPathFactory,
    include_modern_hour: bool,
) -> None:
    import pyarrow as pa
    import pyarrow.parquet as pq

    from bppanalyzer.projection import table_schemas

    root = tmp_path_factory.mktemp("legacy-quarantine-facts")
    schemas = table_schemas()
    modern = schemas["quarantine"]
    legacy_names = [
        name
        for name in modern.names
        if name not in ("raw_run", "discarded_unknown_hero", "discarded_unknown_final_rank")
    ]
    legacy = pa.schema([modern.field(name) for name in legacy_names])

    legacy_dir = root / "source_hour=2026-08-07T00"
    modern_dir = root / "source_hour=2026-08-07T01"
    for directory in (legacy_dir, modern_dir):
        directory.mkdir(parents=True)
        for name in ("runs", "battles"):
            pq.write_table(
                pa.Table.from_pylist([], schema=schemas[name]), directory / f"{name}.parquet"
            )
    base = {
        "source_hour": "2026-08-07T00",
        "source_day": "2026-08-07",
        "bundle_id": "legacy-bundle",
        "run_id": "legacy-run",
        "stage": "bundle_validation",
        "reason_code": "bundle_missing",
        "first_seen_at": "2026-08-07T01:00:00Z",
        "decoder_code_version": "legacy",
        "diagnostic_json": "{}",
    }
    pq.write_table(pa.Table.from_pylist([base], schema=legacy), legacy_dir / "quarantine.parquet")
    discarded = {
        **base,
        "source_hour": "2026-08-07T01",
        "bundle_id": "modern-bundle",
        "run_id": "modern-run",
        "stage": "fact_filter",
        "reason_code": "unaccepted_run",
        "raw_run": True,
        "discarded_unknown_hero": True,
        "discarded_unknown_final_rank": False,
    }
    second_hour = (
        pa.Table.from_pylist([discarded], schema=modern)
        if include_modern_hour
        else pa.Table.from_pylist([base], schema=legacy)
    )
    pq.write_table(second_hour, modern_dir / "quarantine.parquet")

    class _StubStore:
        def seals(self):
            return ()

        def hour_paths(self, days):
            return {
                name: (legacy_dir / f"{name}.parquet", modern_dir / f"{name}.parquet")
                for name in ("runs", "battles", "battle_cards", "quality", "quarantine")
            }

    seal = DaySeal(
        source_day="2026-08-07",
        hourly_fact_commits=(),
        row_counts={},
        day_seal_sha256="0" * 64,
    )
    window = AnalysisWindow(seals=(seal,), start=date(2026, 8, 7), end=date(2026, 8, 7))

    stats = SnapshotBuilder(root, store=_StubStore()).fact_stats(window)

    expected = 1 if include_modern_hour else 0
    assert stats.raw_runs == expected
    assert stats.discarded_unknown_hero == expected
    assert stats.discarded_unknown_final_rank == 0
    assert stats.included_runs == 0
