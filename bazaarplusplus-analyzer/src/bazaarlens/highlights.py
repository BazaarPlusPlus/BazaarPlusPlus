"""Daily Highlights: one Complete Source Day's players, top Ten-Win players,
popular Ten-Win cards and records, for the QQ daily report.

They name players, so they are never published to the metrics bucket; the
analyzer workflow hands them to the report job directly.
"""

from typing import Any

from bazaarlens.analysis import AnalysisResources, AnalysisWindow, open_analysis, sql_string
from bazaarlens.facts.admission import LEGEND_RANK
from bazaarlens.facts.store import FactStore, parse_source_day

LEADERS = 10
PERFECT_LEADERS = 5
CARDS = 10
SKILLS = 5
ENCHANTMENTS = 5

# One display name per account: the newest name its Battles carry that day.
_NAMES = """
CREATE TEMP VIEW player_names AS
SELECT player_account_id, arg_max(player_display_name, recorded_at_utc) AS name
FROM battles_fact
WHERE coalesce(player_display_name, '') <> ''
GROUP BY player_account_id
"""

# Accepted completed Runs of the day with the player's name.
_DAY_RUNS = """
CREATE TEMP VIEW day_runs AS
SELECT r.*, coalesce(n.name, '') AS name, (r.victories=10 AND r.losses >= 0) AS ten_win
FROM completed_runs r
LEFT JOIN player_names n USING (player_account_id)
WHERE r.source_day = {day}
"""

# Cards on the final player board of each Ten-Win Run. Socket overlays
# (card_type 7) share a slot with an item and are not cards of their own.
_BOARDS = """
CREATE TEMP VIEW ten_win_boards AS
SELECT r.run_id, r.bundle_id, r.player_account_id, r.name, r.hero, r.ended_at_utc,
       c.card_set_label, c.template_id, c.name AS card, c.tier, c.enchantment
FROM day_runs r
JOIN battle_cards_fact c
  ON c.bundle_id=r.bundle_id AND c.run_id=r.run_id AND c.battle_id=r.final_battle_id
WHERE r.ten_win AND c.card_set_label IN ('player_hand', 'player_skills')
  AND coalesce(c.card_type, 0) <> 7 AND coalesce(c.name, '') <> ''
"""

# The longest run of consecutive Ten-Win Runs per player, by end time.
_STREAKS = """
CREATE TEMP VIEW streaks AS
SELECT player_account_id, name, count(*) AS streak, mode(hero ORDER BY hero) AS hero
FROM (
  SELECT *,
         row_number() OVER (PARTITION BY player_account_id ORDER BY ended_at_utc, run_id)
         - row_number() OVER (
             PARTITION BY player_account_id, ten_win ORDER BY ended_at_utc, run_id
           ) AS grp
  FROM day_runs
)
WHERE ten_win
GROUP BY player_account_id, name, grp
"""


class HighlightsError(RuntimeError):
    """The requested Source Day has no seal in the Fact Store."""


def _rows(connection, sql: str) -> list[dict[str, Any]]:
    cursor = connection.execute(sql)
    columns = [column[0] for column in cursor.description]
    return [dict(zip(columns, row, strict=True)) for row in cursor.fetchall()]


def _first(connection, sql: str) -> dict[str, Any] | None:
    rows = _rows(connection, sql)
    if not rows or rows[0]["value"] is None:
        return None
    return {**rows[0], "value": int(rows[0]["value"])}


def _popular(connection, label: str, column: str, limit: int) -> list[dict[str, Any]]:
    """The most frequent cards or enchantments on Ten-Win boards, once per Run."""
    key = "template_id" if column == "card" else column
    return _rows(
        connection,
        f"""
        SELECT mode({column} ORDER BY {column}) AS name,
               mode(hero ORDER BY hero) AS hero,
               count(DISTINCT bundle_id || '/' || run_id) AS runs
        FROM ten_win_boards
        WHERE card_set_label={sql_string(label)} AND coalesce({column}, '') <> ''
        GROUP BY {key}
        ORDER BY runs DESC, name
        LIMIT {limit}
        """,
    )


def _run_record(connection, value: str, where: str) -> dict[str, Any] | None:
    """The single Run with the highest value; ties go to the earlier Run."""
    return _first(
        connection,
        f"""
        SELECT name, {value} AS value, hero
        FROM day_runs
        WHERE {where} AND {value} IS NOT NULL
        ORDER BY value DESC, ended_at_utc, name
        LIMIT 1
        """,
    )


def _board_record(connection, value: str, order: str) -> dict[str, Any] | None:
    """The Ten-Win final board with the extreme item count; ties go to the earlier Run."""
    return _first(
        connection,
        f"""
        SELECT name, {value} AS value, hero
        FROM ten_win_boards
        WHERE card_set_label='player_hand'
        GROUP BY bundle_id, run_id, name, hero, ended_at_utc
        ORDER BY value {order}, ended_at_utc, name
        LIMIT 1
        """,
    )


def _player_record(connection, value: str, where: str) -> dict[str, Any] | None:
    """The player with the highest value over the day; ties go to the name."""
    return _first(
        connection,
        f"""
        SELECT name, {value} AS value, mode(hero ORDER BY hero) AS hero
        FROM day_runs
        WHERE {where}
        GROUP BY player_account_id, name
        ORDER BY value DESC, name, player_account_id
        LIMIT 1
        """,
    )


def build_highlights(store: FactStore, day: str, resources: AnalysisResources) -> dict[str, Any]:
    source_day = parse_source_day(day)
    seal = next((item for item in store.seals() if item.source_day == source_day.isoformat()), None)
    if seal is None:
        raise HighlightsError(f"No Complete Source Day seal for {source_day.isoformat()}")
    window = AnalysisWindow((seal,), source_day, source_day)
    legend = f"final_rank={sql_string(LEGEND_RANK)}"
    with open_analysis(store, window, ("runs", "battles", "battle_cards"), resources) as connection:
        for view in (_NAMES, _DAY_RUNS.format(day=sql_string(source_day.isoformat())), _BOARDS):
            connection.execute(view)
        connection.execute(_STREAKS)
        summary = _rows(
            connection,
            """
            SELECT count(DISTINCT player_account_id) AS players,
                   count(*) FILTER (WHERE ten_win) AS ten_win_runs,
                   count(*) FILTER (WHERE victories=10 AND losses=0) AS perfect_runs
            FROM day_runs
            """,
        )[0]
        # Runs by the UTC hour their client says they started.
        by_hour = dict(
            connection.execute(
                """
                SELECT hour(try_cast(started_at_utc AS TIMESTAMP)) AS h, count(*)
                FROM day_runs
                WHERE try_cast(started_at_utc AS TIMESTAMP) IS NOT NULL
                GROUP BY h
                """
            ).fetchall()
        )
        # Ties go to fewer Runs, then more Perfect Runs, then the name.
        leaders = _rows(
            connection,
            f"""
            SELECT name,
                   count(*) FILTER (WHERE ten_win) AS ten_wins,
                   count(*) AS runs,
                   count(*) FILTER (WHERE victories=10 AND losses=0) AS perfect,
                   mode(hero ORDER BY hero) FILTER (WHERE ten_win) AS hero
            FROM day_runs
            GROUP BY player_account_id, name
            HAVING ten_wins > 0
            ORDER BY ten_wins DESC, runs, perfect DESC, name, player_account_id
            LIMIT {LEADERS}
            """,
        )
        perfect_leaders = _rows(
            connection,
            f"""
            SELECT name, count(*) AS perfect, mode(hero ORDER BY hero) AS hero
            FROM day_runs
            WHERE victories=10 AND losses=0
            GROUP BY player_account_id, name
            ORDER BY perfect DESC, name, player_account_id
            LIMIT {PERFECT_LEADERS}
            """,
        )
        records = {
            "legend_rating": _run_record(connection, "final_rating", legend),
            "legend_gain": _player_record(connection, "sum(final_rating_delta)", legend),
            "ten_win_streak": _first(
                connection,
                """
                SELECT name, streak AS value, hero FROM streaks
                ORDER BY streak DESC, name, player_account_id LIMIT 1
                """,
            ),
            "ten_win_heroes": _player_record(connection, "count(DISTINCT hero)", "ten_win"),
            "income": _run_record(connection, "income", "TRUE"),
            "gold": _run_record(connection, "gold", "TRUE"),
            "fewest_items": _board_record(connection, "count(*)", "ASC"),
            "diamond_items": _board_record(
                connection, "count(*) FILTER (WHERE lower(trim(tier))='diamond')", "DESC"
            ),
            "enchanted_items": _board_record(
                connection, "count(*) FILTER (WHERE coalesce(enchantment, '') <> '')", "DESC"
            ),
        }
        cards = _popular(connection, "player_hand", "card", CARDS)
        skills = _popular(connection, "player_skills", "card", SKILLS)
        enchantments = _popular(connection, "player_hand", "enchantment", ENCHANTMENTS)
    return {
        "day": source_day.isoformat(),
        **{key: int(value) for key, value in summary.items()},
        "hourly_runs": [int(by_hour.get(hour, 0)) for hour in range(24)],
        "ten_win_leaders": leaders,
        "perfect_leaders": perfect_leaders,
        "ten_win_cards": cards,
        "ten_win_skills": skills,
        "ten_win_enchantments": enchantments,
        "records": records,
    }
