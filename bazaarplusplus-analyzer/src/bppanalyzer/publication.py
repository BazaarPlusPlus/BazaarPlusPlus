"""Build and atomically replace the two Analyzer V5 consumer snapshots."""

import hashlib
import json
import math
from collections.abc import Callable, Iterable, Mapping
from dataclasses import dataclass
from datetime import UTC, date, datetime, timedelta
from pathlib import Path
from typing import Any

import duckdb
from jsonschema import Draft202012Validator

from bppanalyzer.accepted_runs import (
    CANONICAL_HEROES,
    normalize_hero_sql,
    population_projection_sql,
    recognized_hero_sql,
)
from bppanalyzer.fact_store import DaySeal, FactStore, canonical_json, parse_source_day
from bppanalyzer.object_store import ObjectStore

ANALYSIS_DAYS = 7
CORE_BUILD_LIMIT_PER_HERO = 500
WILSON_Z = 1.96
HEROES_KEY = "analyzer-v5/heroes/latest.json"
BUILDS_KEY = "analyzer-v5/builds/latest.json"
LATEST_CACHE_CONTROL = "public,max-age=60,must-revalidate"
JSON_CONTENT_TYPE = "application/json"


class PublicationError(RuntimeError):
    """A consumer snapshot could not be built or published safely."""


class AnalysisWindowError(PublicationError):
    """Complete Source Day seals cannot form an Analysis Window."""


class ContractViolation(PublicationError):
    """A consumer snapshot violates its schema or semantic invariants."""


@dataclass(frozen=True, slots=True)
class AnalysisWindow:
    seals: tuple[DaySeal, ...]
    start: date
    end: date

    @property
    def value(self) -> dict[str, object]:
        return {
            "start": self.start.isoformat(),
            "end": self.end.isoformat(),
            "days": len(self.seals),
        }


@dataclass(frozen=True, slots=True)
class HeroStats:
    participating_runs: int
    participating_matchup_battles: int


@dataclass(frozen=True, slots=True)
class BuildStats:
    eligible_layout_runs: int
    candidate_builds: int
    published_builds: int


@dataclass(frozen=True, slots=True)
class FactStats:
    raw_runs: int
    discarded_unknown_hero: int
    discarded_unknown_final_rank: int
    included_runs: int
    included_battles: int


@dataclass(frozen=True, slots=True)
class BuildRank:
    identity: tuple[str, ...]
    score: int
    ten_win: int
    p75: int | None


@dataclass(frozen=True, slots=True)
class BuiltSnapshot:
    product: str
    key: str
    content: bytes
    stats: HeroStats | BuildStats


def select_analysis_window(
    seals: Iterable[DaySeal],
    anchor_day: date | str | None = None,
    *,
    source_epoch: date | str | None = None,
) -> AnalysisWindow | None:
    """Return one to seven latest consecutive verified day seals."""
    epoch = parse_source_day(source_epoch) if source_epoch is not None else None
    by_day: dict[date, DaySeal] = {}
    for seal in seals:
        day = parse_source_day(seal.source_day)
        if epoch is not None and day < epoch:
            continue
        if day in by_day:
            raise AnalysisWindowError(f"Duplicate Complete Source Day: {day.isoformat()}")
        by_day[day] = seal
    if not by_day:
        return None
    anchor = parse_source_day(anchor_day) if anchor_day is not None else max(by_day)
    if (epoch is not None and anchor < epoch) or anchor not in by_day:
        return None
    descending = []
    for offset in range(ANALYSIS_DAYS):
        candidate = anchor - timedelta(days=offset)
        if candidate not in by_day:
            break
        descending.append(candidate)
    days = tuple(reversed(descending))
    selected = tuple(by_day[day] for day in days)
    return AnalysisWindow(selected, days[0], days[-1])


def wilson_score(successes: int, total: int) -> int:
    if total <= 0:
        return 0
    proportion = successes / total
    z_squared = WILSON_Z * WILSON_Z
    lower = (
        proportion
        + z_squared / (2 * total)
        - WILSON_Z
        * math.sqrt(proportion * (1 - proportion) / total + z_squared / (4 * total * total))
    ) / (1 + z_squared / total)
    return math.floor(lower * 1_000_000 + 0.5)


class SnapshotBuilder:
    def __init__(
        self,
        data_root: str | Path,
        *,
        store: FactStore | None = None,
        clock: Callable[[], datetime] = lambda: datetime.now(UTC),
        memory_limit: str = "1GB",
        threads: int = 1,
    ) -> None:
        self.root = Path(data_root)
        self.store = store or FactStore(self.root)
        self.clock = clock
        self.memory_limit = memory_limit
        self.threads = threads

    def build_heroes(self, window: AnalysisWindow) -> BuiltSnapshot:
        connection = self._connection(window, ("runs", "battles"))
        try:
            run_rows = connection.execute(
                """
                SELECT source_day, hero_norm, segment,
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
                GROUP BY source_day, hero_norm, segment
                ORDER BY source_day DESC, hero_norm, segment
                """
            ).fetchall()
            matchup_rows = connection.execute(
                f"""
                SELECT r.source_day, r.hero_norm, r.segment, b.opponent_hero_norm,
                       count(*) AS decided,
                       count(*) FILTER (WHERE b.winner_side_norm='player') AS wins,
                       count(*) FILTER (WHERE b.winner_side_norm='opponent') AS losses
                FROM accepted_battles b
                JOIN completed_runs r
                  ON r.bundle_id=b.bundle_id AND r.run_id=b.run_id
                WHERE b.winner_side_norm IN ('player','opponent')
                  AND {recognized_hero_sql("b.opponent_hero")}
                GROUP BY r.source_day, r.hero_norm, r.segment, b.opponent_hero_norm
                ORDER BY r.source_day DESC, r.hero_norm, r.segment, b.opponent_hero_norm
                """
            ).fetchall()
        finally:
            connection.close()

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
                for segment in ("legend", "non_legend"):
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
            "generated_at": _timestamp(self.clock()),
            "window": window.value,
            "days": days,
        }
        validate_snapshot("heroes", payload)
        content = canonical_json(payload)
        return BuiltSnapshot(
            "heroes",
            HEROES_KEY,
            content,
            HeroStats(
                participating_runs=sum(values[0] for values in runs.values()),
                participating_matchup_battles=sum(int(item[4]) for item in matchup_rows),
            ),
        )

    def fact_stats(self, window: AnalysisWindow) -> FactStats:
        connection = self._connection(window, ("runs", "battles", "quarantine"))
        try:
            run_counts = _required_row(
                connection.execute(
                    """
                SELECT count(*) AS raw_runs,
                       count(*) FILTER (WHERE NOT hero_recognized) AS unknown_hero,
                       count(*) FILTER (WHERE NOT final_rank_recognized) AS unknown_final_rank,
                       count(*) FILTER (WHERE accepted) AS included_runs
                FROM run_population
                """
                ).fetchone(),
                "Run counts",
            )
            quarantine_columns = {
                row[0] for row in connection.execute("DESCRIBE quarantine_fact").fetchall()
            }

            def _flag(column: str) -> str:
                # Fact hours written before the quarantine schema gained the
                # discard flags lack these columns entirely.
                return column if column in quarantine_columns else "false"

            discarded = _required_row(
                connection.execute(
                    f"""
                SELECT count(*) FILTER (WHERE coalesce({_flag("raw_run")}, false)),
                       count(*) FILTER (WHERE coalesce({_flag("discarded_unknown_hero")}, false)),
                       count(*) FILTER (
                         WHERE coalesce({_flag("discarded_unknown_final_rank")}, false)
                       )
                FROM quarantine_fact
                """
                ).fetchone(),
                "Discarded Run counts",
            )
            included_battles = int(
                _required_row(
                    connection.execute(
                        """
                        SELECT count(*) FROM battles_fact b
                        JOIN accepted_runs r
                          ON r.bundle_id=b.bundle_id AND r.run_id=b.run_id
                        """
                    ).fetchone(),
                    "Included Battle count",
                )[0]
            )
        finally:
            connection.close()
        return FactStats(
            raw_runs=int(run_counts[0]) + int(discarded[0]),
            discarded_unknown_hero=int(run_counts[1]) + int(discarded[1]),
            discarded_unknown_final_rank=int(run_counts[2]) + int(discarded[2]),
            included_runs=int(run_counts[3]),
            included_battles=included_battles,
        )

    def build_builds(self, window: AnalysisWindow) -> BuiltSnapshot:
        connection = self._connection(window, ("runs", "battles", "battle_cards"))
        try:
            connection.execute(
                """
                CREATE TEMP TABLE eligible_layout_runs AS
                WITH final_battles AS (
                  SELECT r.bundle_id, r.run_id, r.source_day, r.hero_norm,
                         r.victories, r.losses, r.run_day, r.final_battle_id
                  FROM completed_runs r
                  JOIN battles_fact b
                    ON b.bundle_id=r.bundle_id AND b.run_id=r.run_id
                  GROUP BY ALL
                  HAVING count(*) FILTER (WHERE b.is_final_battle)=1
                     AND min(b.battle_id) FILTER (WHERE b.is_final_battle)=r.final_battle_id
                ), layouts AS (
                  SELECT f.bundle_id, f.run_id, f.source_day, f.hero_norm AS hero,
                         f.victories, f.losses, f.run_day, f.final_battle_id,
                         string_agg(lower(c.template_id), '|' ORDER BY lower(c.template_id))
                           AS build_key,
                         to_json(list(struct_pack(
                           template_id := lower(c.template_id),
                           slot := coalesce(c.socket,c.slot_index),
                           tier := c.tier,
                           enchantment := CASE
                             WHEN c.enchantment IS NULL
                               OR lower(trim(c.enchantment)) IN ('','none') THEN NULL
                             ELSE trim(c.enchantment)
                           END,
                           size := c.size
                         ) ORDER BY coalesce(c.socket,c.slot_index), lower(c.template_id),
                                    coalesce(c.tier,''), coalesce(c.enchantment,''), c.size))
                           AS layout_json
                  FROM final_battles f
                  JOIN completed_runs r
                    ON r.bundle_id=f.bundle_id AND r.run_id=f.run_id
                  JOIN battle_cards_fact c
                    ON c.bundle_id=f.bundle_id AND c.run_id=f.run_id
                   AND c.battle_id=f.final_battle_id
                  WHERE c.card_set_label='player_hand'
                    AND c.owner_side='player' AND c.card_kind='item'
                    -- Socket-effect overlays (e.g. Jules cooler/heater sockets,
                    -- card_type 7) share sockets with real items and are not
                    -- part of the board.
                    AND coalesce(c.card_type, 0)=0
                    AND r.final_player_item_signature IS NOT NULL
                  GROUP BY f.bundle_id, f.run_id, f.source_day, f.hero_norm,
                           f.victories, f.losses, f.run_day, f.final_battle_id
                  HAVING count(*) > 0
                     AND bool_and(lower(trim(coalesce(c.card_set_status,'missing')))<>'missing')
                     AND bool_and(c.template_id IS NOT NULL AND regexp_full_match(
                       lower(c.template_id),
                       '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                     ))
                     AND bool_and(c.size IS NOT NULL AND c.size > 0)
                     AND sum(c.size)=10
                     AND bool_and(coalesce(c.socket,c.slot_index) IS NOT NULL
                                  AND coalesce(c.socket,c.slot_index) >= 0
                                  AND coalesce(c.socket,c.slot_index) + c.size <= 10)
                     AND bit_or(
                       ((1::BIGINT << c.size) - 1)
                       << coalesce(c.socket,c.slot_index)
                     ) = 1023
                )
                SELECT *, victories=10 AND losses >= 0 AS ten_win
                FROM layouts
                """
            )
            eligible_count = int(
                _required_row(
                    connection.execute("SELECT count(*) FROM eligible_layout_runs").fetchone(),
                    "Eligible layout count",
                )[0]
            )
            candidate_rows = connection.execute(
                """
                WITH candidates AS (
                  SELECT hero, build_key, count(*) AS completed,
                         count(*) FILTER (WHERE ten_win) AS ten_win_count,
                         list(run_day ORDER BY run_day) FILTER (
                           WHERE ten_win AND run_day >= 0
                         ) AS known_days
                  FROM eligible_layout_runs
                  GROUP BY hero, build_key
                  HAVING count(*) FILTER (WHERE ten_win) >= 1
                ), layout_counts AS (
                  SELECT hero, build_key, layout_json, count(*) AS observations
                  FROM eligible_layout_runs
                  WHERE ten_win
                  GROUP BY hero, build_key, layout_json
                ), representatives AS (
                  SELECT hero, build_key, layout_json,
                         row_number() OVER (
                           PARTITION BY hero, build_key
                           ORDER BY observations DESC, layout_json ASC
                         ) AS layout_rank
                  FROM layout_counts
                )
                SELECT c.hero, c.build_key, c.completed, c.ten_win_count,
                       c.known_days, r.layout_json
                FROM candidates c
                JOIN representatives r USING (hero, build_key)
                WHERE r.layout_rank=1
                ORDER BY c.hero, c.build_key
                """
            ).fetchall()
        finally:
            connection.close()

        candidates_by_hero: dict[str, list[dict[str, Any]]] = {
            hero: [] for hero in CANONICAL_HEROES
        }
        for hero, build_key, completed, ten_win, known_days, layout_json in candidate_rows:
            days = [int(value) for value in known_days] if known_days is not None else []
            p75 = days[math.ceil(0.75 * len(days)) - 1] if days else None
            candidate = {
                "identity": tuple(str(build_key).split("|")),
                "completed": int(completed),
                "ten_win": int(ten_win),
                "p75": p75,
                "score": wilson_score(int(ten_win), int(completed)),
                "layout": json.loads(str(layout_json)),
            }
            candidates_by_hero[str(hero)].append(candidate)

        selected_by_hero: dict[str, list[dict[str, Any]]] = {}
        for hero, candidates in candidates_by_hero.items():
            selected_identities = select_build_identities(
                BuildRank(
                    identity=candidate["identity"],
                    score=candidate["score"],
                    ten_win=candidate["ten_win"],
                    p75=candidate["p75"],
                )
                for candidate in candidates
            )
            by_identity = {candidate["identity"]: candidate for candidate in candidates}
            selected_by_hero[hero] = [by_identity[identity] for identity in selected_identities]

        cards = sorted(
            {
                card
                for candidates in selected_by_hero.values()
                for candidate in candidates
                for card in candidate["identity"]
            }
        )
        card_ref = {card: index for index, card in enumerate(cards)}
        enchantment_names = sorted(
            {
                str(item["enchantment"])
                for candidates in selected_by_hero.values()
                for candidate in candidates
                for item in candidate["layout"]
                if item["enchantment"] is not None
            }
        )
        enchantments: list[str | None] = [None, *enchantment_names]
        enchant_ref = {name: index + 1 for index, name in enumerate(enchantment_names)}
        heroes: dict[str, dict[str, object]] = {}
        for hero in CANONICAL_HEROES:
            selected = selected_by_hero[hero]
            builds = [
                _build_row(candidate, card_ref=card_ref, enchant_ref=enchant_ref)
                for candidate in selected
            ]
            index: dict[int, list[int]] = {}
            for build_id, candidate in enumerate(selected):
                for card in sorted(set(candidate["identity"])):
                    index.setdefault(card_ref[card], []).append(build_id)
            heroes[hero] = {
                "builds": builds,
                "card_index": [[ref, index[ref]] for ref in sorted(index)],
            }
        payload = {
            "schema_version": 2,
            "kind": "ten_win_builds",
            "generated_at": _timestamp(self.clock()),
            "window": window.value,
            "cards": cards,
            "enchantments": enchantments,
            "schemas": {
                "build": ["card_refs", "layout", "stats"],
                "layout": ["card_ref", "slot", "tier", "enchant_ref", "size"],
                "stats": [
                    "completed_run_count",
                    "ten_win_run_count",
                    "ten_win_rate_bps",
                    "p75_ten_win_final_day",
                    "score",
                ],
            },
            "heroes": heroes,
        }
        validate_snapshot("builds", payload)
        content = canonical_json(payload)
        return BuiltSnapshot(
            "builds",
            BUILDS_KEY,
            content,
            BuildStats(
                eligible_layout_runs=eligible_count,
                candidate_builds=len(candidate_rows),
                published_builds=sum(len(value) for value in selected_by_hero.values()),
            ),
        )

    def _connection(
        self, window: AnalysisWindow, tables: tuple[str, ...]
    ) -> duckdb.DuckDBPyConnection:
        if not isinstance(self.threads, int) or isinstance(self.threads, bool) or self.threads < 1:
            raise ValueError("DuckDB threads must be a positive integer")
        connection = duckdb.connect(database=":memory:")
        try:
            connection.execute(f"SET memory_limit={_sql_string(self.memory_limit)}")
            connection.execute(f"SET threads={self.threads}")
            temporary = self.root / "duckdb-tmp"
            temporary.mkdir(parents=True, exist_ok=True)
            connection.execute(f"SET temp_directory={_sql_string(str(temporary))}")
            paths = self.store.hour_paths(seal.source_day for seal in window.seals)
            for table in tables:
                explicit = ",".join(_sql_string(str(path)) for path in paths[table])
                connection.execute(
                    f"CREATE TEMP VIEW {table}_fact AS "
                    f"SELECT * FROM read_parquet([{explicit}], hive_partitioning=false, "
                    f"union_by_name=true)"
                )
            connection.execute(
                "CREATE TEMP VIEW run_population AS " + population_projection_sql("runs_fact")
            )
            connection.execute(
                "CREATE TEMP VIEW accepted_runs AS SELECT * FROM run_population WHERE accepted"
            )
            connection.execute(
                "CREATE TEMP VIEW completed_runs AS SELECT * FROM accepted_runs "
                "WHERE lower(trim(status))='completed'"
            )
            if "battles" in tables:
                connection.execute(
                    "CREATE TEMP VIEW accepted_battles AS "
                    f"SELECT *, {normalize_hero_sql('opponent_hero')} AS opponent_hero_norm, "
                    "CASE WHEN winner_combatant_id='Player' THEN 'player' "
                    "WHEN winner_combatant_id='Opponent' THEN 'opponent' "
                    "WHEN winner_combatant_id IS NOT NULL "
                    "AND winner_combatant_id=player_account_id THEN 'player' "
                    "WHEN winner_combatant_id IS NOT NULL "
                    "AND winner_combatant_id=opponent_account_id THEN 'opponent' "
                    "ELSE NULL END AS winner_side_norm FROM battles_fact"
                )
            return connection
        except BaseException:
            connection.close()
            raise


class LatestPublisher:
    def __init__(self, object_store: ObjectStore) -> None:
        self.object_store = object_store

    def replace(self, snapshot: BuiltSnapshot) -> bool:
        expected_key = {"heroes": HEROES_KEY, "builds": BUILDS_KEY}.get(snapshot.product)
        if expected_key is None or snapshot.key != expected_key:
            raise ContractViolation("Snapshot product and public key disagree")
        try:
            payload = json.loads(snapshot.content)
        except (json.JSONDecodeError, UnicodeDecodeError) as error:
            raise ContractViolation("Snapshot is not valid JSON") from error
        if not isinstance(payload, dict):
            raise ContractViolation("Snapshot root must be an object")
        validate_snapshot(snapshot.product, payload)
        digest = hashlib.sha256(snapshot.content).hexdigest()
        observed = self.object_store.stat(snapshot.key)
        if (
            observed is not None
            and observed.sha256 == digest
            and observed.bytes == len(snapshot.content)
            and observed.cache_control == LATEST_CACHE_CONTROL
            and observed.content_type == JSON_CONTENT_TYPE
        ):
            return False
        self.object_store.put(
            snapshot.key,
            snapshot.content,
            cache_control=LATEST_CACHE_CONTROL,
            content_type=JSON_CONTENT_TYPE,
        )
        confirmed = self.object_store.get(snapshot.key)
        if (
            confirmed is None
            or confirmed.body != snapshot.content
            or confirmed.stat.cache_control != LATEST_CACHE_CONTROL
            or confirmed.stat.content_type != JSON_CONTENT_TYPE
        ):
            raise PublicationError(f"Published snapshot did not confirm: {snapshot.key}")
        return True


def validate_snapshot(product: str, payload: Mapping[str, Any]) -> None:
    if product not in {"heroes", "builds"}:
        raise ValueError(f"Unknown consumer product: {product}")
    schema_path = _contracts_dir() / f"{product}.schema.json"
    try:
        schema = json.loads(schema_path.read_bytes())
        Draft202012Validator(schema).validate(payload)
    except Exception as error:
        raise ContractViolation(f"{product} snapshot failed JSON Schema validation") from error
    window = payload["window"]
    start = parse_source_day(window["start"])
    end = parse_source_day(window["end"])
    window_days = int(window["days"])
    if not 1 <= window_days <= ANALYSIS_DAYS or end - start != timedelta(days=window_days - 1):
        raise ContractViolation("Analysis Window must contain one to seven consecutive days")
    if product == "heroes":
        _validate_heroes(payload, start, end, window_days)
    else:
        _validate_builds(payload)


def _validate_heroes(payload: Mapping[str, Any], start: date, end: date, window_days: int) -> None:
    days = payload["days"]
    expected_days = [(end - timedelta(days=offset)).isoformat() for offset in range(window_days)]
    if [item["day"] for item in days] != expected_days or expected_days[-1] != start.isoformat():
        raise ContractViolation("Hero days must be newest-first and match the Analysis Window")
    expected_rows = [
        (hero, segment) for hero in CANONICAL_HEROES for segment in ("legend", "non_legend")
    ]
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


def _validate_builds(payload: Mapping[str, Any]) -> None:
    if list(payload["heroes"]) != list(CANONICAL_HEROES):
        raise ContractViolation("Build heroes must be the ordered canonical hero object")
    cards = payload["cards"]
    enchantments = payload["enchantments"]
    if cards != sorted(set(cards)):
        raise ContractViolation("Card table must be uniquely and deterministically sorted")
    if (
        not enchantments
        or enchantments[0] is not None
        or enchantments[1:] != sorted(set(enchantments[1:]))
    ):
        raise ContractViolation("Enchantment table must start with null and be sorted")
    for hero in CANONICAL_HEROES:
        value = payload["heroes"][hero]
        expected_index: dict[int, list[int]] = {}
        for build_id, build in enumerate(value["builds"]):
            card_refs, layout, _stats = build
            if any(ref >= len(cards) for ref in card_refs):
                raise ContractViolation("Build references an unknown card")
            layout_refs = [item[0] for item in layout]
            if card_refs != sorted(layout_refs) or sum(item[4] for item in layout) != 10:
                raise ContractViolation("Build Identity and complete layout disagree")
            for item in layout:
                if item[0] >= len(cards) or item[3] >= len(enchantments):
                    raise ContractViolation("Build layout references an unknown table row")
            for ref in sorted(set(card_refs)):
                expected_index.setdefault(ref, []).append(build_id)
        observed = [[ref, expected_index[ref]] for ref in sorted(expected_index)]
        if value["card_index"] != observed:
            raise ContractViolation("Builds and card_index must agree in both directions")


def _timestamp(value: datetime) -> str:
    if value.tzinfo is None or value.utcoffset() is None:
        raise ValueError("Snapshot clock must be timezone-aware")
    return value.astimezone(UTC).isoformat(timespec="seconds").replace("+00:00", "Z")


def _sql_string(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def _contracts_dir() -> Path:
    return Path(__file__).resolve().parents[2] / "contracts" / "v5"


def _required_row(value: tuple[Any, ...] | None, label: str) -> tuple[Any, ...]:
    if value is None:
        raise PublicationError(f"{label} query returned no row")
    return value


def select_build_identities(candidates: Iterable[BuildRank]) -> tuple[tuple[str, ...], ...]:
    """Apply deterministic Top-500 ranking and uncovered-card backfill."""
    ranked = sorted(
        candidates,
        key=lambda candidate: (
            -candidate.score,
            -candidate.ten_win,
            candidate.p75 is None,
            candidate.p75 if candidate.p75 is not None else 0,
            candidate.identity,
        ),
    )
    selected = list(ranked[:CORE_BUILD_LIMIT_PER_HERO])
    selected_ids = {candidate.identity for candidate in selected}
    candidate_cards = {card for candidate in ranked for card in candidate.identity}
    covered = {card for candidate in selected for card in candidate.identity}
    for card in sorted(candidate_cards - covered):
        if card in covered:
            continue
        candidate = next(item for item in ranked if card in item.identity)
        if candidate.identity not in selected_ids:
            selected.append(candidate)
            selected_ids.add(candidate.identity)
            covered.update(candidate.identity)
    return tuple(candidate.identity for candidate in selected)


def _build_row(
    candidate: Mapping[str, Any],
    *,
    card_ref: Mapping[str, int],
    enchant_ref: Mapping[str, int],
) -> list[object]:
    layout = [
        [
            card_ref[str(item["template_id"])],
            int(item["slot"]),
            _tier_value(item["tier"]),
            enchant_ref.get(item["enchantment"], 0),
            int(item["size"]),
        ]
        for item in candidate["layout"]
    ]
    completed = int(candidate["completed"])
    ten_win = int(candidate["ten_win"])
    stats = [
        completed,
        ten_win,
        math.floor(ten_win * 10_000 / completed + 0.5),
        candidate["p75"],
        int(candidate["score"]),
    ]
    return [
        sorted(card_ref[str(card)] for card in candidate["identity"]),
        layout,
        stats,
    ]


def _tier_value(value: object) -> int | None:
    if not isinstance(value, str):
        return None
    return {
        "bronze": 1,
        "silver": 2,
        "gold": 3,
        "diamond": 4,
        "legendary": 5,
    }.get(value.strip().lower())
