"""Index shape tests for extract.py; stdlib only (UnityPy and Pillow load inside main)."""

import json
from pathlib import Path

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
            with extract.game_database(
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
            ) as database:
                rows, names = extract.game_cards(database)
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
            "zh1",
            {"en": "en.json", "zh": "zh.json"},
        )
        self.assertEqual(
            index,
            {
                "version": 2,
                "text": {"en": "en.json", "zh": "zh.json"},
                "game": {"buildid": "7", "gameData": "e1", "zh": "zh1"},
                "cards": {"aaaa": "2.webp", "old": "1.webp"},
                "names": {"aaaa": "Mantis Shrimp", "cccc": "Ande", "old": "Old Card"},
            },
        )


class CardTextTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.fixture = json.loads((Path(__file__).parent / "fixtures/card-text.json").read_text())
        cls.en = extract.CardLanguage("en", cls.fixture["keywords"], {})
        cls.raw = {c["Localization"]["Title"]["Text"]: c for c in cls.fixture["cards"].values()}
        cls.cards = {name: extract.card_definition(c, cls.en) for name, c in cls.raw.items()}

    def test_radar_module_values_units_and_added_tag(self):
        card = self.cards["Radar Module"]
        self.assertEqual(card["startingTier"], "Silver")
        self.assertEqual(card["tags"], ["Tech"])
        self.assertEqual(
            card["lines"],
            [
                {
                    "id": 0,
                    "available": True,
                    "resolved": "full",
                    "tiers": ["Silver", "Gold", "Diamond"],
                    "segments": [
                        {"text": "When an enemy uses an item, items adjacent to this gain "},
                        {
                            "token": "{ability.0}",
                            "unit": "percent",
                            "keyword": "Crit Chance",
                            "tiers": {"Silver": 4, "Gold": 8, "Diamond": 12},
                        },
                        {"text": "% "},
                        {"keyword": "Crit Chance", "label": "Crit Chance"},
                    ],
                }
            ],
        )
        icy = card["enchantments"]["Icy"]["lines"][0]
        self.assertIn(
            {
                "token": "{ability.e1}",
                "unit": "seconds",
                "keyword": "Freeze",
                "tiers": dict.fromkeys(icy["tiers"], 0.5),
            },
            icy["segments"],
        )
        self.assertIn("Weapon", card["enchantments"]["Obsidian"]["tags"])

    def test_damage_ammo_and_multicast(self):
        pistol = self.cards["Pistol Sword"]
        for line in pistol["lines"]:
            self.assertEqual(
                next(s["tiers"] for s in line["segments"] if "tiers" in s),
                {"Gold": 15, "Diamond": 30},
            )
        self.assertEqual([a["AmmoMax"] for a in pistol["attributes"].values()], [3, 3])
        sword = self.cards["Sword of Swords"]
        for line, amount in [
            (sword["lines"][0], 150),
            (sword["enchantments"]["Shiny"]["lines"][0], 1),
        ]:
            values = next(s["tiers"] for s in line["segments"] if "tiers" in s)
            self.assertEqual(values, {"Diamond": amount, "Legendary": amount})

    def test_dynamic_values_preserve_the_token(self):
        for name, token, status in [
            ("Healthy Heart", "{ability.0}", "none"),
            ("Shortened Rage", "{aura.9}", "partial"),
        ]:
            line = self.cards[name]["lines"][0]
            self.assertEqual(line["resolved"], status)
            self.assertIn({"token": token}, line["segments"])

    def test_locale_availability_and_keywords(self):
        raw = self.raw["Radar Module"]
        translations = dict(self.fixture["zh"])

        def chinese():
            language = extract.CardLanguage("zh", self.fixture["keywords"], translations)
            return extract.card_definition(raw, language)["lines"][0]

        line = chinese()
        self.assertEqual(line["resolved"], "full")
        label = self.fixture["zh"]["b62e02b57d80e3be252fe8484d70c8ea"]
        self.assertTrue(
            any(
                s.get("keyword") == "Crit Chance" and s.get("label", "!") in label
                for s in line["segments"]
            )
        )
        content = raw["Localization"]["Tooltips"][0]["Content"]
        for translation in ("缺少占位符", None):
            translations[content["Key"]] = translation
            line = chinese()
            self.assertFalse(line["available"])
            self.assertEqual((line["resolved"], line["segments"]), ("none", []))

    def test_all_enchantment_names_follow_the_same_definition_shape(self):
        raw = self.raw["Radar Module"].copy()
        names = "Deadly Ethereal Fiery Golden Heavy Icy Mystical Obsidian Radiant Restorative Shielded Shiny Tiny Toxic Turbo Mossy".split()
        raw["Enchantments"] = dict.fromkeys(names, raw["Enchantments"]["Icy"])
        self.assertEqual(set(extract.card_definition(raw, self.en)["enchantments"]), set(names))

    def test_digest_delivery_preserves_retired_definitions_in_each_locale(self):
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory)
            with sqlite3.connect(out / "zh-CN.bytes") as zh:
                zh.execute("CREATE TABLE translation (hash, text)")
                zh.executemany("INSERT INTO translation VALUES (?, ?)", self.fixture["zh"].items())
            with sqlite3.connect(":memory:") as db:
                db.execute("CREATE TABLE cards (id, data)")
                db.executemany(
                    "INSERT INTO cards VALUES (?, ?)",
                    [(k, json.dumps(c)) for k, c in self.fixture["cards"].items()],
                )
                db.execute("CREATE TABLE tooltips (data)")
                db.execute(
                    "INSERT INTO tooltips VALUES (?)", (json.dumps(self.fixture["keywords"]),)
                )

                def write(previous):
                    return extract.write_card_text(
                        db, out / "zh-CN.bytes", out / "index.json", previous, out
                    )

                files = write(None)
                db.execute("DELETE FROM cards WHERE id = ?", (next(iter(self.fixture["cards"])),))
                self.assertEqual(files, write({"version": 2, "text": files}))
                for locale, name in files.items():
                    payload = json.loads((out / name).read_bytes())
                    self.assertEqual((payload["version"], payload["locale"]), (1, locale))
                    expected_labels = (
                        {"Weapon": "Weapon", "Relic": "Relic", "Tech": "Tech"}
                        if locale == "en"
                        else {"Weapon": "武器", "Tech": "科技"}
                    )
                    self.assertEqual(payload["tagLabels"], expected_labels)
                    self.assertEqual(set(payload["cards"]), set(self.fixture["cards"]))


if __name__ == "__main__":
    unittest.main()
