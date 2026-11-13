"""Build Corpus: eligible final boards, representative layouts, and selection."""

import json
import math
from collections.abc import Iterable, Mapping
from dataclasses import dataclass
from datetime import date, datetime
from pathlib import Path
from typing import Any

import duckdb

from bazaarlens.analysis import AnalysisWindow, required_row, sql_list
from bazaarlens.durable import utc_timestamp
from bazaarlens.facts.admission import CANONICAL_HEROES
from bazaarlens.products.base import ConsumerProduct, ContractViolation, ProductData

CORE_BUILD_LIMIT_PER_HERO = 500
WILSON_Z = 1.96
TIERS = ("bronze", "silver", "gold", "diamond", "legendary")
_TIER_VALUES = {tier: rank for rank, tier in enumerate(TIERS, start=1)}


@dataclass(frozen=True, slots=True)
class BuildRank:
    identity: tuple[str, ...]
    score: int
    ten_win: int
    p75: int | None


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


def build_builds(
    connection: duckdb.DuckDBPyConnection, window: AnalysisWindow, generated_at: datetime
) -> ProductData:
    connection.execute(
        f"""
        CREATE TEMP TABLE eligible_layout_runs AS
        WITH final_battles AS (
          SELECT r.bundle_id, r.run_id, r.source_day, r.hero,
                 r.victories, r.losses, r.run_day, r.final_battle_id
          FROM completed_runs r
          JOIN battles_fact b
            ON b.bundle_id=r.bundle_id AND b.run_id=r.run_id
          GROUP BY ALL
          HAVING count(*) FILTER (WHERE b.is_final_battle)=1
             AND min(b.battle_id) FILTER (WHERE b.is_final_battle)=r.final_battle_id
        ), layouts AS (
          SELECT f.bundle_id, f.run_id, f.source_day, f.hero,
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
          GROUP BY f.bundle_id, f.run_id, f.source_day, f.hero,
                   f.victories, f.losses, f.run_day, f.final_battle_id
          HAVING count(*) > 0
             AND bool_and(lower(trim(coalesce(c.card_set_status,'missing')))<>'missing')
             AND bool_and(c.template_id IS NOT NULL AND regexp_full_match(
               lower(c.template_id),
               '^[0-9a-f]{{8}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{12}}$'
             ))
             AND bool_and(c.size IS NOT NULL AND c.size BETWEEN 1 AND 3)
             AND bool_and(c.tier IS NOT NULL AND lower(trim(c.tier)) IN ({sql_list(TIERS)}))
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
        required_row(
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

    candidates_by_hero: dict[str, list[dict[str, Any]]] = {hero: [] for hero in CANONICAL_HEROES}
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
        "generated_at": utc_timestamp(generated_at),
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
    return ProductData(
        payload,
        {
            "eligible_layout_runs": eligible_count,
            "candidate_builds": len(candidate_rows),
            "published_builds": sum(len(value) for value in selected_by_hero.values()),
        },
    )


def _validate_builds(payload: Mapping[str, Any], _start: date, _end: date, _days: int) -> None:
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
    return _TIER_VALUES.get(value.strip().lower())


BUILDS = ConsumerProduct(
    name="builds",
    public_key="builds/latest.json",
    schema_file="builds.schema.json",
    local_relpath=Path("snapshots", "builds", "latest.json"),
    tables=("runs", "battles", "battle_cards"),
    build=build_builds,
    validate_semantics=_validate_builds,
    counter_names=("eligible_layout_runs", "candidate_builds", "published_builds"),
)
