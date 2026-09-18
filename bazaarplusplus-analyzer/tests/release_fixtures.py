import hashlib
from datetime import UTC, date, datetime, timedelta
from pathlib import Path

import pyarrow as pa

from bppanalyzer.fact_store import FactStore
from bppanalyzer.projection import HourProjection, table_schemas

CARD_IDS = tuple(f"00000000-0000-0000-0000-{index:012d}" for index in range(1, 11))


def sealed_store(root: Path, days: int, *, start: date = date(2026, 8, 7)) -> FactStore:
    store = FactStore(root)
    for offset in range(days):
        commit_sealed_day(store, start + timedelta(days=offset), day_offset=offset)
    return store


def sealed_store_with_rows(
    root: Path,
    rows: dict[str, list[dict[str, object]]],
    *,
    start: date = date(2026, 8, 7),
) -> FactStore:
    store = FactStore(root)
    for offset in range(7):
        source_day = start + timedelta(days=offset)
        for hour_number in range(24):
            source_hour = datetime.combine(source_day, datetime.min.time(), UTC) + timedelta(
                hours=hour_number
            )
            values = rows if offset == 0 and hour_number == 0 else {}
            store.commit_hour(_projection(source_hour, values))
        store.seal_day(source_day)
    return store


def commit_sealed_day(store: FactStore, source_day: date, *, day_offset: int = 0) -> None:
    for hour_number in range(24):
        source_hour = datetime.combine(source_day, datetime.min.time(), UTC) + timedelta(
            hours=hour_number
        )
        rows = _populated_rows(source_hour, day_offset) if hour_number == 0 else {}
        store.commit_hour(_projection(source_hour, rows))
    store.seal_day(source_day)


def _projection(source_hour: datetime, rows: dict[str, list[dict[str, object]]]) -> HourProjection:
    schemas = table_schemas()
    tables = {
        name: pa.Table.from_pylist(rows.get(name, []), schema=schema)
        for name, schema in schemas.items()
    }
    identity = source_hour.strftime("%Y-%m-%dT%H").encode()
    return HourProjection(
        source_hour=source_hour,
        raw_commit_sha256=hashlib.sha256(identity).hexdigest(),
        tables=tables,
    )


def _populated_rows(source_hour: datetime, day_offset: int) -> dict[str, list[dict[str, object]]]:
    hour = source_hour.strftime("%Y-%m-%dT%H")
    day = source_hour.date().isoformat()
    run_id = f"run-{day}"
    battle_count = 50 if day_offset == 0 else 1
    final_battle_id = f"battle-{day}-{battle_count - 1}"
    is_ten_win = day_offset % 2 == 0
    run = {
        "source_hour": hour,
        "source_day": day,
        "available_at_ms": int(source_hour.timestamp() * 1000),
        "bundle_id": f"bundle-{day}",
        "bundle_sha256": hashlib.sha256(day.encode()).hexdigest(),
        "run_id": run_id,
        "player_account_id": f"account-{day_offset % 3}",
        "hero": "Dooley",
        "status": "completed",
        "run_day": 10 if is_ten_win else 8,
        "victories": 10 if is_ten_win else 5,
        "losses": 0 if is_ten_win else 5,
        "final_rank": "Legendary" if is_ten_win else "Gold",
        "final_rating": 1300 if is_ten_win else 1200,
        "final_rating_delta": 25 if is_ten_win else -10,
        "final_battle_id": final_battle_id,
        "final_player_item_signature": hashlib.sha256(day.encode()).hexdigest(),
        "projection_code_version": "fixture-v1",
    }
    battles: list[dict[str, object]] = []
    for index in range(battle_count):
        battle_id = f"battle-{day}-{index}"
        winner = "Player" if index < 30 else "Opponent"
        battles.append(
            {
                "source_hour": hour,
                "source_day": day,
                "available_at_ms": int(source_hour.timestamp() * 1000) + index,
                "bundle_id": f"bundle-{day}",
                "run_id": run_id,
                "battle_id": battle_id,
                "game_day": min(index + 1, 13),
                "is_final_battle": battle_id == final_battle_id,
                "player_account_id": f"account-{day_offset % 3}",
                "player_hero": "Dooley",
                "opponent_account_id": f"ghost-{index}",
                "opponent_hero": "Dooley",
                "opponent_rank": "Gold",
                "opponent_rating": 1000 if day_offset == 0 else None,
                "winner_combatant_id": winner,
                "loser_combatant_id": "Opponent" if winner == "Player" else "Player",
                "winner_side": None,
                "winner_hero": None,
            }
        )
    cards = [
        {
            "source_hour": hour,
            "source_day": day,
            "available_at_ms": int(source_hour.timestamp() * 1000),
            "bundle_id": f"bundle-{day}",
            "run_id": run_id,
            "battle_id": final_battle_id,
            "card_set_label": "player_hand",
            "card_set_status": "Captured",
            "owner_side": "player",
            "card_kind": "item",
            "slot_index": index,
            "instance_id": f"instance-{day}-{index}",
            "template_id": card_id,
            "size": 1,
            "socket": index,
            "tier": "Gold",
            "enchantment": "Burning" if index == 0 else None,
        }
        for index, card_id in enumerate(CARD_IDS)
    ]
    quality = [
        {
            "source_hour": hour,
            "source_day": day,
            "bundle_id": f"bundle-{day}",
            "run_id": run_id,
            "code": "timestamp_anomaly" if day_offset == 0 else "fixture_quality",
            "severity": "warning",
            "blocks_release": False,
            "detail_json": "{}",
        }
    ]
    return {
        "runs": [run],
        "battles": battles,
        "battle_cards": cards,
        "quality": quality,
    }
