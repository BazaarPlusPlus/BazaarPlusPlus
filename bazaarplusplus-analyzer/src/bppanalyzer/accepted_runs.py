"""Accepted Run admission shared by ingest and analysis."""

import re
from dataclasses import dataclass

CANONICAL_HEROES = (
    "Dooley",
    "Jules",
    "Karnok",
    "Mak",
    "Pygmalien",
    "Stelle",
    "TheDragons",
    "Vanessa",
)
CANONICAL_RANKS = frozenset(
    {"Bronze", "Silver", "Gold", "Diamond", "Master", "Masters", "Legendary"}
)
LEGEND_RANK = "Legendary"

_HERO_ALIASES = {"Hero8": "TheDragons"}
_SQL_RELATION = re.compile(r"^[a-z_][a-z0-9_]*$")


@dataclass(frozen=True, slots=True)
class RunAdmission:
    hero: str
    final_rank: str | None
    segment: str | None
    unknown_hero: bool
    unknown_final_rank: bool

    @property
    def accepted(self) -> bool:
        return not self.unknown_hero and not self.unknown_final_rank


def admit_run(hero: str, final_rank: str | None) -> RunAdmission:
    """Normalize one Run and decide whether it belongs to the analyzed population."""
    normalized_hero = normalize_hero(hero)
    assert normalized_hero is not None
    normalized_rank = normalize_rank(final_rank)
    unknown_hero = normalized_hero not in CANONICAL_HEROES
    unknown_final_rank = normalized_rank not in CANONICAL_RANKS
    segment = None
    if not unknown_hero and not unknown_final_rank:
        segment = "legend" if normalized_rank == LEGEND_RANK else "non_legend"
    return RunAdmission(
        normalized_hero,
        normalized_rank,
        segment,
        unknown_hero,
        unknown_final_rank,
    )


def normalize_hero(value: str | None) -> str | None:
    if value is None:
        return None
    normalized = value.strip()
    return _HERO_ALIASES.get(normalized, normalized)


def normalize_rank(value: str | None) -> str | None:
    return value.strip() if value is not None else None


def normalize_hero_sql(expression: str) -> str:
    return f"CASE WHEN trim({expression})='Hero8' THEN 'TheDragons' ELSE trim({expression}) END"


def recognized_hero_sql(expression: str) -> str:
    """Return the DuckDB predicate for a normalized, canonical Hero."""
    heroes = ",".join(_sql_string(hero) for hero in CANONICAL_HEROES)
    return f"coalesce(({normalize_hero_sql(expression)}) IN ({heroes}), false)"


def population_projection_sql(source_relation: str) -> str:
    """Project canonical Accepted Run fields for DuckDB-backed analysis."""
    if _SQL_RELATION.fullmatch(source_relation) is None:
        raise ValueError("Source relation must be a simple SQL identifier")
    ranks = ",".join(_sql_string(rank) for rank in sorted(CANONICAL_RANKS))
    hero = normalize_hero_sql("hero")
    rank = "trim(final_rank)"
    hero_recognized = recognized_hero_sql("hero")
    rank_recognized = f"coalesce(({rank}) IN ({ranks}), false)"
    accepted = f"({hero_recognized} AND {rank_recognized})"
    return (
        "SELECT *, "
        f"{hero} AS hero_norm, "
        f"{rank} AS final_rank_norm, "
        f"{hero_recognized} AS hero_recognized, "
        f"{rank_recognized} AS final_rank_recognized, "
        f"{accepted} AS accepted, "
        f"CASE WHEN {accepted} AND {rank}={_sql_string(LEGEND_RANK)} THEN 'legend' "
        f"WHEN {accepted} THEN 'non_legend' ELSE NULL END AS segment "
        f"FROM {source_relation}"
    )


def _sql_string(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"
