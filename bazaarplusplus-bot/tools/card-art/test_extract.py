"""Index shape tests for extract.py; stdlib only (UnityPy and Pillow load inside main)."""

import json
import os
import sqlite3
import tempfile
import unittest
import zipfile

import extract


def game_data_zip(directory, cards):
    path = os.path.join(directory, "GameData.db")
    database = sqlite3.connect(path)
    database.execute("CREATE TABLE cards (Id TEXT, Data TEXT)")
    database.executemany("INSERT INTO cards VALUES (?, ?)", [(i, json.dumps(d)) for i, d in cards])
    database.commit()
    database.close()
    archive = os.path.join(directory, "GameData.db.zip")
    with zipfile.ZipFile(archive, "w") as out:
        out.write(path, "GameData.db")
    return archive


def card(kind, art, title):
    data = {"$type": kind, "ArtKey": art}
    if title is not None:
        data["Localization"] = {"Title": {"Text": title}}
    return data


class IndexTest(unittest.TestCase):
    def test_every_card_is_named_and_only_items_and_skills_carry_art(self):
        with tempfile.TemporaryDirectory() as directory:
            rows, names = extract.game_cards(
                game_data_zip(
                    directory,
                    [
                        ("aaaa", card("TCardItem", "art-a", "Mantis Shrimp")),
                        ("bbbb", card("TCardSkill", "art-b", " Keen Eye ")),
                        ("cccc", card("TCardEncounterEvent", "art-c", "Ande")),
                        ("dddd", card("TCardEncounterStep", "art-d", "")),
                        ("eeee", card("TCardPlayerEffect", "art-e", None)),
                    ],
                )
            )
        self.assertEqual(sorted(r[0] for r in rows), ["aaaa", "bbbb"])
        self.assertEqual(names, {"aaaa": "Mantis Shrimp", "bbbb": "Keen Eye", "cccc": "Ande"})

    def test_the_index_keeps_art_and_names_of_cards_gamedata_dropped(self):
        previous = {
            "cards": {"old": "1.webp", "aaaa": "stale.webp"},
            "names": {"old": "Old Card", "aaaa": "Stale"},
        }
        index = extract.card_index(
            {"aaaa": "2.webp"},
            {"aaaa": "Mantis Shrimp", "cccc": "Ande"},
            {"aaaa", "cccc"},
            previous,
            "7",
            "e1",
        )
        self.assertEqual(
            index,
            {
                "version": 1,
                "game": {"buildid": "7", "gameData": "e1"},
                "cards": {"aaaa": "2.webp", "old": "1.webp"},
                "names": {"aaaa": "Mantis Shrimp", "cccc": "Ande", "old": "Old Card"},
            },
        )


if __name__ == "__main__":
    unittest.main()
