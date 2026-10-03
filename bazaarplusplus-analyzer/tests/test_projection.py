from datetime import UTC, datetime

import pytest

from bppanalyzer.bundle_source import (
    BundleRef,
    RawHourIndex,
    admit_bundle,
    raw_commit_sha256,
)
from bppanalyzer.projection import project_hour
from tests.bundle_fixtures import SOURCE_HOUR, bundle_bytes, payload
from tests.fakes import collect_tables


def _project_payload(run_payload: bytes):
    content = bundle_bytes("bundle-a", run_payload=run_payload)
    ref = BundleRef(
        bundle_id="bundle-a",
        available_at_ms=int(SOURCE_HOUR.timestamp() * 1_000),
        download_url="https://download.invalid/a",
        download_expires_at_ms=int(SOURCE_HOUR.timestamp() * 1_000) + 60_000,
    )
    index = RawHourIndex(
        source_hour=SOURCE_HOUR,
        items=(ref,),
        raw_commit_sha256=raw_commit_sha256((ref,)),
        pages=1,
    )
    return collect_tables(project_hour(index, [admit_bundle(ref, content)]))


@pytest.mark.parametrize(
    ("winner_id", "loser_id", "winner_side", "winner_hero"),
    (
        ("Player", "Opponent", "player", "Vanessa"),
        ("Opponent", "Player", "opponent", "Pygmalien"),
    ),
)
def test_battle_outcome_uses_bundle_side_names(
    winner_id: str,
    loser_id: str,
    winner_side: str,
    winner_hero: str,
) -> None:
    projected = _project_payload(
        payload(
            winner_combatant_id=winner_id,
            loser_combatant_id=loser_id,
            victories=int(winner_side == "player"),
            losses=int(winner_side == "opponent"),
        )
    )

    battle = projected["battles"].to_pylist()[0]
    run = projected["runs"].to_pylist()[0]
    assert battle["winner_combatant_id"] == winner_id
    assert battle["loser_combatant_id"] == loser_id
    assert battle["winner_side"] == winner_side
    assert battle["winner_hero"] == winner_hero
    assert run["battle_decided_count"] == 1
    assert run["battle_player_win_count"] == (winner_side == "player")
    assert run["battle_player_loss_count"] == (winner_side == "opponent")
    assert "run_outcome_count_mismatch" not in {
        row["code"] for row in projected["quality"].to_pylist()
    }


@pytest.mark.parametrize(
    ("winner_id", "loser_id", "winner_side", "winner_hero"),
    (
        ("account-1", "account-2", "player", "Vanessa"),
        ("account-2", "account-1", "opponent", "Pygmalien"),
    ),
)
def test_battle_outcome_falls_back_to_participant_account_ids(
    winner_id: str,
    loser_id: str,
    winner_side: str,
    winner_hero: str,
) -> None:
    battle = _project_payload(
        payload(
            winner_combatant_id=winner_id,
            loser_combatant_id=loser_id,
        )
    )["battles"].to_pylist()[0]

    assert battle["winner_side"] == winner_side
    assert battle["winner_hero"] == winner_hero


def test_battle_outcome_keeps_an_unknown_combatant_undecided() -> None:
    projected = _project_payload(
        payload(
            winner_combatant_id="unknown-combatant",
            loser_combatant_id="another-unknown-combatant",
        )
    )

    battle = projected["battles"].to_pylist()[0]
    run = projected["runs"].to_pylist()[0]
    assert battle["winner_side"] is None
    assert battle["winner_hero"] is None
    assert run["battle_decided_count"] == 0


def test_client_timestamp_anomalies_are_quality_rows_and_never_repartition_a_bundle() -> None:
    content = bundle_bytes(
        "bundle-a",
        run_payload=payload(
            started_at="2026-08-11T01:00:00Z",
            battle_at="2026-08-09T23:00:00Z",
        ),
        created_at_ms=int(datetime(2026, 8, 11, tzinfo=UTC).timestamp() * 1_000),
    )
    ref = BundleRef(
        bundle_id="bundle-a",
        available_at_ms=int(SOURCE_HOUR.timestamp() * 1_000),
        download_url="https://download.invalid/a",
        download_expires_at_ms=int(SOURCE_HOUR.timestamp() * 1_000) + 60_000,
    )
    index = RawHourIndex(
        source_hour=SOURCE_HOUR,
        items=(ref,),
        raw_commit_sha256=raw_commit_sha256((ref,)),
        pages=1,
    )

    projected = collect_tables(project_hour(index, [admit_bundle(ref, content)]))

    quality_codes = {row["code"] for row in projected["quality"].to_pylist()}
    assert {"client_clock_future", "client_clock_before_run"} <= quality_codes
    for table_name in ("runs", "battles", "battle_cards", "quality"):
        table = projected[table_name]
        assert set(table.column("source_hour").to_pylist()) == {"2026-08-10T12"}
        assert set(table.column("source_day").to_pylist()) == {"2026-08-10"}


@pytest.mark.parametrize(
    ("hero", "final_rank", "unknown_hero", "unknown_final_rank"),
    (
        ("UnknownHero", "Legendary", True, False),
        ("Vanessa", None, False, True),
        ("Vanessa", "Mythic", False, True),
        ("UnknownHero", "Mythic", True, True),
    ),
)
def test_unaccepted_run_is_discarded_with_all_battles_and_cards(
    hero: str,
    final_rank: str | None,
    unknown_hero: bool,
    unknown_final_rank: bool,
) -> None:
    projected = _project_payload(payload(hero=hero, final_rank=final_rank))

    assert projected["runs"].num_rows == 0
    assert projected["battles"].num_rows == 0
    assert projected["battle_cards"].num_rows == 0
    discarded = projected["quarantine"].to_pylist()
    assert len(discarded) == 1
    assert discarded[0]["stage"] == "fact_filter"
    assert discarded[0]["raw_run"] is True
    assert discarded[0]["discarded_unknown_hero"] is unknown_hero
    assert discarded[0]["discarded_unknown_final_rank"] is unknown_final_rank
