"""Accepted Run admission and the canonical hero and rank sets."""

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
