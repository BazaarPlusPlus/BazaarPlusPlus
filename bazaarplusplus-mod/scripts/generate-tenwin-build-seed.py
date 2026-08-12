#!/usr/bin/env python3
"""Generate the deterministic offline analyzer-v5 fallback from the contract fixture."""

from __future__ import annotations

import argparse
import json
from datetime import date
from pathlib import Path


CANONICAL_HEROES = (
    "Vanessa",
    "Pygmalien",
    "Dooley",
    "Mak",
    "Jules",
    "Karnok",
    "Stelle",
)


def validate_window(payload: dict[str, object]) -> None:
    window = payload.get("window")
    if not isinstance(window, dict):
        raise ValueError("window must be an object")

    start = date.fromisoformat(str(window.get("start")))
    end = date.fromisoformat(str(window.get("end")))
    days = window.get("days")
    if not isinstance(days, int) or isinstance(days, bool) or not 1 <= days <= 7:
        raise ValueError("window.days must be an integer from 1 through 7")
    if (end - start).days + 1 != days:
        raise ValueError("window.start/window.end must span window.days inclusively")


def render_seed(repository_root: Path) -> str:
    fixture_path = (
        repository_root
        / "tests"
        / "LiveBuildRecommendations.Tests"
        / "Fixtures"
        / "analyzer-v5-schema2-contract.json"
    )
    payload = json.loads(fixture_path.read_text(encoding="utf-8"))
    validate_window(payload)
    fixture_heroes = payload["heroes"]
    payload["heroes"] = {
        hero: fixture_heroes.get(hero, {"builds": [], "card_index": []})
        for hero in CANONICAL_HEROES
    }
    return json.dumps(payload, ensure_ascii=False, indent=2) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--check",
        action="store_true",
        help="fail if the committed fallback differs from generated output",
    )
    args = parser.parse_args()

    repository_root = Path(__file__).resolve().parent.parent
    destination = (
        repository_root
        / "src"
        / "BazaarPlusPlus"
        / "Data"
        / "BuildRecommendations"
        / "tenwin_builds.json"
    )
    generated = render_seed(repository_root)
    if args.check:
        if not destination.exists() or destination.read_text(encoding="utf-8") != generated:
            parser.error(
                "embedded fallback is stale; run scripts/generate-tenwin-build-seed.py"
            )
        return 0

    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(generated, encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
