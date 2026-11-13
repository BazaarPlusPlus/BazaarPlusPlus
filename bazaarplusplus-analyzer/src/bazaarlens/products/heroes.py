"""Hero metrics: additive outcomes and decided Matchups by Source Day."""

from collections.abc import Mapping
from datetime import date, datetime, timedelta
from pathlib import Path
from typing import Any

import duckdb

from bazaarlens.analysis import AnalysisWindow, sql_list
from bazaarlens.durable import utc_timestamp
from bazaarlens.facts.admission import CANONICAL_HEROES
from bazaarlens.products.base import ConsumerProduct, ContractViolation, ProductData

SEGMENTS = ("legend", "non_legend")


def build_heroes(
    connection: duckdb.DuckDBPyConnection, window: AnalysisWindow, generated_at: datetime
) -> ProductData:
    run_rows = connection.execute(
        """
        SELECT source_day, hero, segment,
               count(*) AS completed,
               count(*) FILTER (
                 WHERE victories BETWEEN 0 AND 10 AND losses >= 0
               ) AS scored,
               count(*) FILTER (WHERE victories=10 AND losses >= 0) AS ten_win,
               count(*) FILTER (WHERE victories=10 AND losses=0) AS perfect,
               count(*) FILTER (WHERE victories=10 AND losses >= 1) AS gold,
               count(*) FILTER (WHERE victories BETWEEN 7 AND 9 AND losses >= 0) AS silver,
               count(*) FILTER (WHERE victories BETWEEN 4 AND 6 AND losses >= 0) AS bronze,
               count(*) FILTER (
                 WHERE victories=10 AND losses >= 0 AND run_day >= 0
               ) AS known_count,
               coalesce(sum(run_day) FILTER (
                 WHERE victories=10 AND losses >= 0 AND run_day >= 0
               ), 0) AS sum_days
        FROM completed_runs
        GROUP BY source_day, hero, segment
        ORDER BY source_day DESC, hero, segment
        """
    ).fetchall()
    matchup_rows = connection.execute(
        f"""
        SELECT r.source_day, r.hero, r.segment, b.opponent_hero,
               count(*) AS decided,
               count(*) FILTER (WHERE b.winner_side='player') AS wins,
               count(*) FILTER (WHERE b.winner_side='opponent') AS losses
        FROM battles_fact b
        JOIN completed_runs r
          ON r.bundle_id=b.bundle_id AND r.run_id=b.run_id
        WHERE b.winner_side IN ('player','opponent')
          AND b.opponent_hero IN ({sql_list(CANONICAL_HEROES)})
        GROUP BY r.source_day, r.hero, r.segment, b.opponent_hero
        ORDER BY r.source_day DESC, r.hero, r.segment, b.opponent_hero
        """
    ).fetchall()

    runs = {
        (str(row[0]), str(row[1]), str(row[2])): tuple(int(value) for value in row[3:])
        for row in run_rows
    }
    matchups: dict[tuple[str, str, str], list[dict[str, object]]] = {}
    for day, hero, segment, opponent, decided, wins, losses in matchup_rows:
        matchups.setdefault((str(day), str(hero), str(segment)), []).append(
            {
                "opponent_hero": str(opponent),
                "decided": int(decided),
                "wins": int(wins),
                "losses": int(losses),
            }
        )

    days = []
    for day in reversed(tuple(seal.source_day for seal in window.seals)):
        rows = []
        for hero in CANONICAL_HEROES:
            for segment in SEGMENTS:
                values = runs.get((day, hero, segment), (0,) * 9)
                rows.append(
                    {
                        "hero": hero,
                        "segment": segment,
                        "runs": {
                            "completed": values[0],
                            "scored": values[1],
                            "ten_win": values[2],
                        },
                        "outcomes": {
                            "perfect": values[3],
                            "gold": values[4],
                            "silver": values[5],
                            "bronze": values[6],
                        },
                        "ten_win_days": {
                            "known_count": values[7],
                            "sum_days": values[8],
                        },
                        "matchups": matchups.get((day, hero, segment), []),
                    }
                )
        days.append({"day": day, "rows": rows})
    payload = {
        "schema_version": 1,
        "kind": "hero_metrics",
        "generated_at": utc_timestamp(generated_at),
        "window": window.value,
        "days": days,
    }
    return ProductData(
        payload,
        {
            "participating_runs": sum(values[0] for values in runs.values()),
            "participating_matchup_battles": sum(int(item[4]) for item in matchup_rows),
        },
    )


def _validate_heroes(payload: Mapping[str, Any], start: date, end: date, window_days: int) -> None:
    days = payload["days"]
    expected_days = [(end - timedelta(days=offset)).isoformat() for offset in range(window_days)]
    if [item["day"] for item in days] != expected_days or expected_days[-1] != start.isoformat():
        raise ContractViolation("Hero days must be newest-first and match the Analysis Window")
    expected_rows = [(hero, segment) for hero in CANONICAL_HEROES for segment in SEGMENTS]
    for day in days:
        rows = day["rows"]
        if [(row["hero"], row["segment"]) for row in rows] != expected_rows:
            raise ContractViolation("Every hero day must contain the stable canonical row set")
        for row in rows:
            runs = row["runs"]
            outcomes = row["outcomes"]
            ten_win_days = row["ten_win_days"]
            if runs["ten_win"] != outcomes["perfect"] + outcomes["gold"]:
                raise ContractViolation("Ten-Win Runs must equal perfect plus gold")
            if sum(outcomes.values()) > runs["scored"] or runs["scored"] > runs["completed"]:
                raise ContractViolation("Hero outcome denominators are inconsistent")
            if ten_win_days["known_count"] > runs["ten_win"]:
                raise ContractViolation("Known Ten-Win final days exceed Ten-Win Runs")
            for matchup in row["matchups"]:
                if matchup["decided"] != matchup["wins"] + matchup["losses"]:
                    raise ContractViolation("Matchup decided count differs from wins plus losses")


HEROES = ConsumerProduct(
    name="heroes",
    public_key="heroes/latest.json",
    schema_file="heroes.schema.json",
    local_relpath=Path("snapshots", "heroes", "latest.json"),
    tables=("runs", "battles"),
    build=build_heroes,
    validate_semantics=_validate_heroes,
    counter_names=("participating_runs", "participating_matchup_battles"),
)
