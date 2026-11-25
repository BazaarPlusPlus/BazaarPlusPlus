# /// script
# requires-python = ">=3.13,<3.15"
# dependencies = ["UnityPy==1.25.4", "Pillow==12.0.0"]
# ///
"""The live GameData is required because the depot copy lags behind.
Previous entries preserve Card Art, names and Card Text for Runs from older patches.
The index maps each TemplateId to its art file (`cards`, items and skills) and to its
English title (`names`, every card GameData has, encounters included).
"""

from contextlib import closing, contextmanager
from pathlib import Path

import argparse
import glob
import hashlib
import io
import json
import os
import re
import sqlite3
import struct
import sys
import tempfile
import warnings
import zipfile

# A Large card is 3 board units wide; at 2x it needs about 400 px of the square art.
ITEM_HEIGHT = 400
SKILL_HEIGHT = 128
MIN_ITEM_SHARE = 0.95
MIN_SKILL_SHARE = 0.90
NONE = 0xFFFFFFFF


class Catalog:
    def __init__(self, data: bytes):
        self.b = data
        magic, self.version, self.keys_offset = struct.unpack_from("<iII", data, 0)
        if magic != 0x0DE38942 or self.version not in (1, 2):
            raise ValueError("not an Addressables binary catalog")

    def _u32(self, offset):
        return struct.unpack_from("<I", self.b, offset)[0]

    def _offsets(self, offset):
        if offset == NONE:
            return []
        size = self._u32(offset - 4)
        return list(struct.unpack_from(f"<{size // 4}I", self.b, offset))

    def _string(self, encoded, sep=None):
        if encoded == NONE:
            return None
        unicode = bool(encoded & 0x80000000)
        offset = encoded & 0x3FFFFFFF
        if not (encoded & 0x40000000 and sep):
            raw = self.b[offset : offset + self._u32(offset - 4)]
            return raw.decode("utf-16-le" if unicode else "ascii")
        parts = []
        while True:
            part, following = struct.unpack_from("<II", self.b, offset)
            parts.append(self._string(part, sep))
            if following == NONE:
                break
            offset = following
        if self.version > 1:
            parts.reverse()
        return sep.join(parts)

    def _key(self, offset):
        type_offset, object_offset = struct.unpack_from("<II", self.b, offset)
        class_name = self._string(self._u32(type_offset + 4), ".")
        if class_name != "System.String":
            return None
        encoded, sep = struct.unpack_from("<II", self.b, object_offset)
        return self._string(encoded, chr(sep))

    def paths(self):
        result = {}
        offsets = self._offsets(self.keys_offset)
        for index in range(0, len(offsets), 2):
            key = self._key(offsets[index])
            locations = self._offsets(offsets[index + 1])
            if key is None or not locations:
                continue
            internal_id = struct.unpack_from("<I", self.b, locations[0] + 4)[0]
            result.setdefault(key, self._string(internal_id, "/"))
        return result


def unity_version(data_dir):
    with open(os.path.join(data_dir, "globalgamemanagers"), "rb") as file:
        head = file.read(256).decode("latin1")
    match = re.search(r"\d{4}\.\d+\.\d+[abfp]\d+", head)
    if not match:
        raise ValueError("no Unity version in globalgamemanagers")
    return match.group(0)


TIER_ORDER = ("Bronze", "Silver", "Gold", "Diamond", "Legendary")
TOKEN = re.compile(r"\{([^{}]+)\}")
DURATIONS = {
    "ChargeAmount",
    "HasteAmount",
    "SlowAmount",
    "FreezeAmount",
    "FlatCooldownReduction",
    "CooldownMax",
    "EnragedDurationMax",
}
ATTRIBUTE_KEYWORDS = {
    "CritChance": "Crit Chance",
    "FlatCooldownReduction": "Cooldown",
    "CooldownMax": "Cooldown",
    "EnragedDurationMax": "Enraged",
    "ShieldApplyAmount": "Shield",
    "RegenApplyAmount": "Regen",
    "PoisonApplyAmount": "Poison",
    "BurnApplyAmount": "Burn",
    "Multicast": "Multicast",
    "CritChanceAmount": "Crit Chance",
}


def static_value(value, attributes, blocked):
    kind = value.get("$type")
    if kind == "TFixedValue":
        return value["Value"]
    if kind != "TReferenceValueCardAttribute":
        return None
    target = value["Target"]
    key = value["AttributeType"]
    if target["$type"] != "TTargetCardSelf" or target.get("Conditions") or key in blocked:
        return None
    number = attributes.get(key)
    modifier = value.get("Modifier")
    if number is not None and modifier:
        factor = static_value(modifier["Value"], attributes, blocked)
        if factor is None or modifier["ModifyMode"] != "Multiply":
            return None
        number *= factor
        if modifier["ShouldRound"] and number != int(number):
            return None
    return number


def token_value(token, card, source, tier, attributes, blocked):
    parts = token.split(".")
    if len(parts) == 1:
        key = {"custom_0": "Custom_0", "TempoCost": "TempoCost"}.get(token)
        return attributes.get(key) if key not in blocked else None, key, "number"

    prefix, effect_id, *accessor = parts
    pool = {"ability": "Abilities", "aura": "Auras"}.get(prefix)
    effect = (source.get(pool) or {}).get(effect_id) or (card.get(pool) or {}).get(effect_id)
    if not effect or "??" in token:
        return None, None, "number"
    ids = "AbilityIds" if prefix == "ability" else "AuraIds"
    # A display-only effect can be absent from every tier's executable effects.
    if (
        source is card
        and effect_id not in tier[ids]
        and any(effect_id in other[ids] for other in card["Tiers"].values())
    ):
        return None, None, "number"

    action = effect["Action"]
    kind = action["$type"]
    verb = re.sub("^T(?:Aura)?Action(?:Player|Card)", "", kind)
    key = action.get("AttributeType") or verb + "Amount"
    value = action.get("Value")
    if accessor == ["mod"]:
        value = ((value or {}).get("Modifier") or {}).get("Value")
        key = None
    elif accessor in (["target"], ["targets"]):
        value, key = action.get("TargetCount"), verb + "Targets"
    elif accessor:
        return None, None, "number"
    elif kind == "TActionGameSpawnCards":
        value, key = (action.get("SpawnContext") or {}).get("Limit"), None

    number = (
        static_value(value, attributes, blocked)
        if value
        else (attributes.get(key) if key not in blocked else None)
    )
    unit = "number"
    if key in DURATIONS and action.get("Operation") not in ("Multiply", "Percent"):
        unit = "seconds"
        if number is not None:
            number /= 1000
    if number is not None and number == int(number):
        number = int(number)
    return number, key, unit


class CardLanguage:
    def __init__(self, locale, keywords, translations):
        self.locale = locale
        self.translations = translations

        def localize(text):
            if not text or locale == "en":
                return text
            return translations.get(hashlib.md5(text.encode()).hexdigest())

        self.keywords = {
            word: {
                "label": localize(word),
                "description": localize(entry["Keyword"]),
                "icon": entry["Icon"],
            }
            for word, entry in sorted(keywords.items())
            if word != "Invalid"
        }
        self.labels = {}
        for word, entry in self.keywords.items():
            if entry["label"]:
                self.labels.setdefault(entry["label"], word)
        pattern = "|".join(map(re.escape, sorted(self.labels, key=len, reverse=True))) or "(?!)"
        if locale == "en":
            pattern = r"(?<!\w)(?:" + pattern + r")(?!\w)"
        self.pattern = re.compile(pattern)

    def text(self, content):
        return content["Text"] if self.locale == "en" else self.translations.get(content["Key"])

    def literals(self, text):
        cursor = 0
        for match in self.pattern.finditer(text):
            if cursor < match.start():
                yield {"text": text[cursor : match.start()]}
            yield {"keyword": self.labels[match[0]], "label": match[0]}
            cursor = match.end()
        if cursor < len(text):
            yield {"text": text[cursor:]}


def tooltip_line(index, content, contexts, card, source, language):
    active = list(contexts)
    text = language.text(content)
    english = content["Text"]
    line = {"id": index, "tiers": active, "available": False, "resolved": "none", "segments": []}
    if not text or sorted(TOKEN.findall(text)) != sorted(TOKEN.findall(english)):
        return line
    line["available"] = True
    known = total = cursor = 0
    for match in TOKEN.finditer(text):
        line["segments"].extend(language.literals(text[cursor : match.start()]))
        values = {}
        segment = {"token": match[0]}
        for tier, (attributes, blocked) in contexts.items():
            number, key, unit = token_value(
                match[1], card, source, card["Tiers"][tier], attributes, blocked
            )
            if number is None:
                continue
            values[tier] = number
            segment["unit"] = "percent" if match[0] + "%" in english else unit
            keyword = ATTRIBUTE_KEYWORDS.get(key, (key or "").removesuffix("Amount"))
            if keyword in language.keywords:
                segment["keyword"] = keyword
        if values:
            segment["tiers"] = values
        line["segments"].append(segment)
        cursor = match.end()
        known += len(values)
        total += len(active)
    line["segments"].extend(language.literals(text[cursor:]))
    line["resolved"] = "full" if known == total else "partial" if known else "none"
    return line


def card_definition(card, language):
    attributes = {}
    tiers = {}
    for tier in TIER_ORDER:
        if tier in card["Tiers"]:
            attributes.update(card["Tiers"][tier]["Attributes"])
            tiers[tier] = attributes.copy()
    definition = {
        "title": language.text(card["Localization"]["Title"]),
        "type": card["Type"],
        "size": card["Size"],
        "heroes": card["Heroes"],
        "tags": card["Tags"],
        "startingTier": card["StartingTier"],
        "attributes": tiers,
        "enchantments": {},
    }
    for enchant, source in [(None, card), *(card.get("Enchantments") or {}).items()]:
        contexts = {}
        for tier, attrs in tiers.items():
            auras = {
                key: aura
                for key, aura in card["Auras"].items()
                if key in card["Tiers"][tier]["AuraIds"]
            }
            if enchant:
                auras.update(source["Auras"])
            blocked = {
                action["AttributeType"]
                for aura in auras.values()
                if (action := aura["Action"]).get("AttributeType")
                and (action.get("Target") or {}).get("$type") == "TTargetCardSelf"
            }
            contexts[tier] = attrs | (source.get("Attributes") or {}), blocked
        lines = []
        for index, tooltip in enumerate((source.get("Localization") or {}).get("Tooltips") or []):
            active = {
                tier: ctx
                for tier, ctx in contexts.items()
                if enchant or index in card["Tiers"][tier]["TooltipIds"]
            }
            content = tooltip["Content"]
            if active and content["Text"]:
                lines.append(tooltip_line(index, content, active, card, source, language))
        if enchant:
            definition["enchantments"][enchant] = {"tags": source["Tags"], "lines": lines}
        else:
            definition["lines"] = lines
    return definition


def write_card_text(database, translations_path, previous_path, previous, output):
    with closing(sqlite3.connect(translations_path.as_uri() + "?mode=ro", uri=True)) as zh:
        translations = dict(zh.execute("SELECT hash, text FROM translation"))
    keywords = json.loads(database.execute("SELECT data FROM tooltips").fetchone()[0])
    definitions = {
        key: json.loads(data)
        for key, data in database.execute(
            """SELECT id, data FROM cards WHERE json_extract(data, '$."$type"')
           IN ('TCardItem', 'TCardSkill')"""
        )
    }
    current_ids = {row[0] for row in database.execute("SELECT id FROM cards")}
    files = {}
    for locale in ("en", "zh"):
        language = CardLanguage(locale, keywords, translations)
        cards = {key: card_definition(card, language) for key, card in definitions.items()}
        tags = {
            tag
            for card in cards.values()
            for source in (card, *card["enchantments"].values())
            for tag in source["tags"]
        }
        tag_labels = {
            entry["Tag"]: label
            for word, entry in keywords.items()
            if entry["Tag"] in tags and (label := language.keywords[word]["label"])
        }
        if previous and locale in previous.get("text", {}):
            filename = previous["text"][locale]
            match = re.fullmatch(rf"text-{locale}-([0-9a-f]{{20}})\.json", filename)
            if not match:
                raise ValueError(f"unexpected previous Card Text file: {filename}")
            data = (Path(previous_path).parent / filename).read_bytes()
            if hashlib.sha256(data).hexdigest()[:20] != match[1]:
                raise ValueError(f"previous Card Text digest mismatch: {filename}")
            prior = json.loads(data)
            cards.update(
                {key: card for key, card in prior["cards"].items() if key not in current_ids}
            )
            for key, entry in prior["keywords"].items():
                language.keywords.setdefault(key, entry)
            for tag, label in prior.get("tagLabels", {}).items():
                if tag not in tags:
                    tag_labels[tag] = label
        payload = {
            "version": 1,
            "locale": locale,
            "cards": cards,
            "keywords": language.keywords,
            "tagLabels": tag_labels,
        }
        data = json.dumps(
            payload, ensure_ascii=False, sort_keys=True, separators=(",", ":")
        ).encode()
        filename = f"text-{locale}-{hashlib.sha256(data).hexdigest()[:20]}.json"
        (output / filename).write_bytes(data)
        files[locale] = filename
    return files


@contextmanager
def game_database(game_data_zip):
    with zipfile.ZipFile(game_data_zip) as archive:
        name = next(n for n in archive.namelist() if n.endswith("GameData.db"))
        with tempfile.TemporaryDirectory() as directory:
            path = archive.extract(name, directory)
            with closing(sqlite3.connect(f"file:{path}?mode=ro", uri=True)) as database:
                yield database


def game_cards(database):
    rows = database.execute(
        """SELECT Id, json_extract(Data, '$."$type"'), json_extract(Data, '$.ArtKey')
           FROM cards WHERE json_extract(Data, '$."$type"') IN ('TCardItem', 'TCardSkill')"""
    ).fetchall()
    titles = database.execute(
        """SELECT Id, json_extract(Data, '$.Localization.Title.Text') FROM cards"""
    ).fetchall()
    names = {
        template_id: title.strip()
        for template_id, title in titles
        if isinstance(title, str) and title.strip()
    }
    return rows, names


def card_index(cards, names, current, previous, buildid, gamedata_etag, zh_etag, text):
    """The published index. A TemplateId GameData no longer has keeps its earlier art and name,
    so Runs from older patches still resolve."""
    cards, names = dict(cards), dict(names)
    for key, mapping in (("cards", cards), ("names", names)):
        for template_id, value in (previous or {}).get(key, {}).items():
            if template_id not in current:
                mapping.setdefault(template_id, value)
    return {
        "version": 2,
        "text": text,
        "game": {"buildid": buildid, "gameData": gamedata_etag, "zh": zh_etag},
        "cards": dict(sorted(cards.items())),
        "names": dict(sorted(names.items())),
    }


def webp(image, height, mode):
    from PIL import Image

    image = image.convert(mode)
    width = max(1, round(image.width * height / image.height))
    image = image.resize((width, height), Image.LANCZOS)
    buffer = io.BytesIO()
    # method 6 takes ~1 s per RGBA image for a 2 % smaller file.
    image.save(buffer, "WEBP", quality=80, method=4)
    return buffer.getvalue()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("data_dir")
    parser.add_argument("game_data")
    parser.add_argument("out_dir")
    parser.add_argument("--buildid")
    parser.add_argument("--gamedata-etag")
    parser.add_argument("--zh-etag", required=True)
    parser.add_argument("--previous")
    args = parser.parse_args()

    import UnityPy

    warnings.filterwarnings("ignore", module="UnityPy")
    UnityPy.config.FALLBACK_UNITY_VERSION = unity_version(args.data_dir)
    streaming = os.path.join(args.data_dir, "StreamingAssets")
    with open(os.path.join(streaming, "aa", "catalog.bin"), "rb") as file:
        paths = Catalog(file.read()).paths()
    bundles = sorted(
        glob.glob(os.path.join(streaming, "aa", "*", "card_*_assets_all.bundle"))
        + glob.glob(os.path.join(streaming, "aa", "*", "skills_assets_all.bundle"))
    )
    if not bundles:
        raise SystemExit("no card or skill bundles under StreamingAssets/aa")
    env = UnityPy.load(*bundles)
    objects = {}
    for path, reader in env.container.items():
        # A sprite and its texture share a path; the sprite holds the cut-out.
        if path not in objects or reader.type.name == "Sprite":
            objects[path] = reader

    def asset(key):
        path = paths.get(key)
        return objects.get(re.sub(r"\[.*\]$", "", path)) if path else None

    os.makedirs(args.out_dir, exist_ok=True)
    files_by_art = {}

    def save(art_key, image, height, mode):
        data = webp(image, height, mode)
        name = hashlib.sha256(data).hexdigest()[:20] + ".webp"
        with open(os.path.join(args.out_dir, name), "wb") as file:
            file.write(data)
        files_by_art[art_key] = name
        return name

    def item_art(art_key):
        card_data = asset(art_key)
        if card_data is None or card_data.type.name != "MonoBehaviour":
            return None
        reference = card_data.read_typetree().get("cardMaterialReference") or {}
        material = asset(reference.get("m_AssetGUID", ""))
        if material is None or material.type.name != "Material":
            return None
        for name, texture in material.read().m_SavedProperties.m_TexEnvs:
            if name == "_MainTex" and texture.m_Texture.path_id:
                texture = texture.m_Texture.deref_parse_as_object()
                return save(art_key, texture.image, ITEM_HEIGHT, "RGB")
        return None

    def skill_art(art_key):
        sprite = asset(art_key)
        if sprite is None or sprite.type.name not in ("Sprite", "Texture2D"):
            return None
        return save(art_key, sprite.read().image, SKILL_HEIGHT, "RGBA")

    cards = {}
    counts = {"TCardItem": [0, 0], "TCardSkill": [0, 0]}
    previous = None
    if args.previous and os.path.exists(args.previous):
        with open(args.previous) as file:
            previous = json.load(file)
    with game_database(args.game_data) as database:
        text = write_card_text(
            database,
            Path(args.game_data).resolve().with_name("zh-CN.bytes"),
            args.previous,
            previous,
            Path(args.out_dir),
        )
        rows, names = game_cards(database)
    for template_id, kind, art_key in rows:
        if not art_key:
            continue
        counts[kind][1] += 1
        name = files_by_art.get(art_key)
        if name is None:
            try:
                name = (item_art if kind == "TCardItem" else skill_art)(art_key)
            except Exception as error:  # one broken asset must not stop the set
                print(f"skip {kind} {template_id} {art_key}: {error}", file=sys.stderr)
                name = None
        if name:
            cards[template_id] = name
            counts[kind][0] += 1

    items, skills = counts["TCardItem"], counts["TCardSkill"]
    summary = {
        "items": items,
        "skills": skills,
        "files": len(set(cards.values())),
        "names": len(names),
    }
    print(json.dumps(summary))
    if items[0] < items[1] * MIN_ITEM_SHARE or skills[0] < skills[1] * MIN_SKILL_SHARE:
        raise SystemExit(f"too few cards resolved: {summary}")
    current = {row[0] for row in rows} | set(names)
    index = card_index(
        cards, names, current, previous, args.buildid, args.gamedata_etag, args.zh_etag, text
    )
    with open(os.path.join(args.out_dir, "index.json"), "w") as file:
        json.dump(index, file, separators=(",", ":"))


if __name__ == "__main__":
    main()
