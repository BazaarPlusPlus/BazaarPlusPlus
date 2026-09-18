import hashlib
from datetime import UTC, datetime

import httpx
import pytest

from bppanalyzer.bundle_source import (
    BundleRef,
    BundleSource,
    RawHourIndex,
    RetryableSourceError,
    admit_bundle,
    raw_commit_sha256,
)
from bppanalyzer.projection import project_hour
from tests.bundle_fixtures import SOURCE_HOUR, bundle_bytes, payload


def _project_payload(run_payload: bytes):
    content = bundle_bytes("bundle-a", run_payload=run_payload)
    ref = BundleRef(
        bundle_id="bundle-a",
        available_at_ms=int(SOURCE_HOUR.timestamp() * 1_000),
        download_url="https://download.invalid/a",
        download_expires_at_ms=int(SOURCE_HOUR.timestamp() * 1_000) + 60_000,
        sha256=hashlib.sha256(content).hexdigest(),
        bytes=len(content),
    )
    index = RawHourIndex(
        source_hour=SOURCE_HOUR,
        items=(ref,),
        raw_commit_sha256=raw_commit_sha256((ref,)),
        pages=1,
    )
    return project_hour(
        index,
        [admit_bundle(ref, content)],
    )


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

    battle = projected.tables["battles"].to_pylist()[0]
    run = projected.tables["runs"].to_pylist()[0]
    assert battle["winner_combatant_id"] == winner_id
    assert battle["loser_combatant_id"] == loser_id
    assert battle["winner_side"] == winner_side
    assert battle["winner_hero"] == winner_hero
    assert run["battle_decided_count"] == 1
    assert run["battle_player_win_count"] == (winner_side == "player")
    assert run["battle_player_loss_count"] == (winner_side == "opponent")
    assert "run_outcome_count_mismatch" not in {
        row["code"] for row in projected.tables["quality"].to_pylist()
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
    battle = (
        _project_payload(
            payload(
                winner_combatant_id=winner_id,
                loser_combatant_id=loser_id,
            )
        )
        .tables["battles"]
        .to_pylist()[0]
    )

    assert battle["winner_side"] == winner_side
    assert battle["winner_hero"] == winner_hero


def test_battle_outcome_keeps_an_unknown_combatant_undecided() -> None:
    projected = _project_payload(
        payload(
            winner_combatant_id="unknown-combatant",
            loser_combatant_id="another-unknown-combatant",
        )
    )

    battle = projected.tables["battles"].to_pylist()[0]
    run = projected.tables["runs"].to_pylist()[0]
    assert battle["winner_side"] is None
    assert battle["winner_hero"] is None
    assert run["battle_decided_count"] == 0


def test_bundle_admission_failure_stops_the_source_hour_before_commit() -> None:
    valid = bundle_bytes("bundle-a")
    wrong_declared_digest = bundle_bytes("bundle-b")
    corrupt_magic = b"NOTBNDL5" + bundle_bytes("bundle-c")[8:]
    corrupt_segment = bytearray(bundle_bytes("bundle-d"))
    corrupt_segment[-1] ^= 1
    contents = {
        "bundle-a": valid,
        "bundle-b": wrong_declared_digest,
        "bundle-c": corrupt_magic,
        "bundle-d": bytes(corrupt_segment),
    }

    def server(request: httpx.Request) -> httpx.Response:
        if request.url.host == "api.invalid":
            items = []
            for offset, (bundle_id, content) in enumerate(contents.items()):
                digest = hashlib.sha256(content).hexdigest()
                if bundle_id == "bundle-b":
                    digest = "0" * 64
                items.append(
                    {
                        "bundle_id": bundle_id,
                        "available_at_ms": int(SOURCE_HOUR.timestamp() * 1_000) + offset,
                        "download_url": f"https://download.invalid/{bundle_id}",
                        "download_expires_at_ms": int(SOURCE_HOUR.timestamp() * 1_000) + 60_000,
                        "sha256": digest,
                        "bytes": len(content),
                    }
                )
            return httpx.Response(
                200,
                json={
                    "window": {
                        "available_from_ms": int(SOURCE_HOUR.timestamp() * 1_000),
                        "available_before_ms": int(SOURCE_HOUR.timestamp() * 1_000) + 3_600_000,
                    },
                    "items": items,
                    "next_after": None,
                },
            )
        return httpx.Response(200, content=contents[request.url.path.removeprefix("/")])

    source = BundleSource(
        api_base_url="https://api.invalid",
        sync_token="test-token",
        client=httpx.Client(transport=httpx.MockTransport(server)),
        clock=lambda: datetime(2026, 8, 11, tzinfo=UTC),
    )

    index = source.hour_index(SOURCE_HOUR)
    with pytest.raises(RetryableSourceError) as raised:
        project_hour(index, source.stream(index)).tables

    assert raised.value.reason == "bundle_validation_failed"


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
        sha256=hashlib.sha256(content).hexdigest(),
        bytes=len(content),
    )
    index = RawHourIndex(
        source_hour=SOURCE_HOUR,
        items=(ref,),
        raw_commit_sha256=raw_commit_sha256((ref,)),
        pages=1,
    )

    projected = project_hour(
        index,
        [admit_bundle(ref, content)],
    )

    quality_codes = {row["code"] for row in projected.tables["quality"].to_pylist()}
    assert {"client_clock_future", "client_clock_before_run"} <= quality_codes
    for table_name in ("runs", "battles", "battle_cards", "quality"):
        table = projected.tables[table_name]
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

    assert projected.tables["runs"].num_rows == 0
    assert projected.tables["battles"].num_rows == 0
    assert projected.tables["battle_cards"].num_rows == 0
    discarded = projected.tables["quarantine"].to_pylist()
    assert len(discarded) == 1
    assert discarded[0]["stage"] == "fact_filter"
    assert discarded[0]["raw_run"] is True
    assert discarded[0]["discarded_unknown_hero"] is unknown_hero
    assert discarded[0]["discarded_unknown_final_rank"] is unknown_final_rank
