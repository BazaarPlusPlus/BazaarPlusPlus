#!/usr/bin/env python3
"""
Fetches the monster database from BazaarDB and saves it as a JSON file.
Support BazaarDB by considering a subscription if you find this helpful.
Becoming a BazaarDB supporter by visiting https://bazaardb.gg/supporter.
"""

import argparse
import codecs
import json
from pathlib import Path
from urllib.request import Request, urlopen

DEFAULT_URL = "https://bazaardb.gg/search?c=monsters"
DEFAULT_OUT = Path("Data/monsters_bazaardb.json")


def _extract_json_array(text: str, marker: str) -> list[dict]:
    start = text.find(marker)
    if start < 0:
        raise ValueError(f"Could not find {marker!r} in source")

    i = start + len(marker) - 1
    depth = 0
    in_string = False
    escape = False
    end = None

    while i < len(text):
        ch = text[i]
        if in_string:
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == '"':
                in_string = False
        else:
            if ch == '"':
                in_string = True
            elif ch == "[":
                depth += 1
            elif ch == "]":
                depth -= 1
                if depth == 0:
                    end = i + 1
                    break
        i += 1

    if end is None:
        raise ValueError(f"Could not determine end of {marker!r} in source")

    return json.loads(text[start + len(marker) - 1 : end])


def _extract_cards_array(text: str) -> list[dict]:
    try:
        return _extract_json_array(text, '"cards":[')
    except ValueError:
        pass

    escaped_start = text.find('\\"cards\\":[')
    if escaped_start < 0:
        raise ValueError('Could not find "cards" array in source')

    decoded_text = codecs.decode(text[escaped_start:], "unicode_escape")
    return _extract_json_array(decoded_text, '"cards":[')


def _map_entry(card: dict) -> dict:
    monster = card.get("MonsterMetadata") or {}

    board = []
    for item in monster.get("board") or []:
        board.append(
            {
                "cardid": item.get("baseId"),
                "title": item.get("title"),
                "tier": item.get("tierOverride"),
                "size": item.get("size"),
                "enchant": item.get("enchantmentOverride"),
                "type": item.get("type"),
            }
        )

    skills = []
    for skill in monster.get("skills") or []:
        skills.append(
            {
                "skill_id": skill.get("baseId"),
                "title": skill.get("title"),
                "tier": skill.get("tierOverride"),
                "type": skill.get("type"),
            }
        )

    combatant = card.get("CombatantType") or {}
    rewards = {
        "gold": card.get("RewardCombatGold"),
        "xp": card.get("RewardCombatXp"),
    }

    return {
        "encounter_id": card.get("Id"),
        "title": (card.get("Title") or {}).get("Text"),
        "title_original": card.get("_originalTitleText"),
        "base_tier": card.get("BaseTier"),
        "rewards": rewards,
        "combatant": {
            "type": combatant.get("$type"),
            "level": combatant.get("Level"),
        },
        "monster_metadata": {
            "available": monster.get("available"),
            "day": monster.get("day"),
            "health": monster.get("health"),
            "board": board,
            "skills": skills,
        },
        "type": card.get("Type"),
        "heroes": card.get("Heroes") or [],
    }


def _fetch_url(url: str) -> str:
    headers = {
        "accept": "*/*",
        "rsc": "1",
        "user-agent": "Mozilla/5.0",
    }
    request = Request(url, headers=headers)
    with urlopen(request) as response:
        charset = response.headers.get_content_charset() or "utf-8"
        return response.read().decode(charset, errors="replace")


def get_monster_db(url: str = DEFAULT_URL, input_path: str | None = None) -> dict:
    if input_path:
        text = Path(input_path).read_text(encoding="utf-8", errors="replace")
    else:
        text = _fetch_url(url)
    cards = _extract_cards_array(text)
    return _map_cards(cards)


def _map_cards(cards: list[dict]) -> dict:
    result = {}

    for card in cards:
        if card.get("Type") != "CombatEncounter":
            continue
        if "MonsterMetadata" not in card:
            continue
        mapped = _map_entry(card)
        encounter_id = mapped["encounter_id"]
        if encounter_id:
            result[encounter_id] = mapped

    return result


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Export complete encounter monster metadata from BazaarDB"
    )
    parser.add_argument("--url", default=DEFAULT_URL, help="BazaarDB Monster URL")
    parser.add_argument(
        "--input",
        help="Read monster response from a local file instead of fetching BazaarDB",
    )
    parser.add_argument("--out", default=str(DEFAULT_OUT), help="Output JSON path")
    args = parser.parse_args()

    data = get_monster_db(args.url, input_path=args.input)
    out_path = Path(args.out)
    out_path.write_text(
        json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
