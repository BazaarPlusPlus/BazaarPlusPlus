# /// script
# requires-python = ">=3.13,<3.15"
# dependencies = ["UnityPy==1.25.4", "Pillow==12.0.0"]
# ///
"""The live GameData is required because the depot copy lags behind.
Previous entries preserve Card Art and names for Runs from older patches.
The index maps each TemplateId to its art file (`cards`, items and skills) and to its
English title (`names`, every card GameData has, encounters included).
"""

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


def game_cards(game_data_zip):
    """Item and skill rows for art, and the English title of every card (encounters too)."""
    with zipfile.ZipFile(game_data_zip) as archive:
        name = next(n for n in archive.namelist() if n.endswith("GameData.db"))
        with tempfile.TemporaryDirectory() as directory:
            path = archive.extract(name, directory)
            database = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
            rows = database.execute(
                """SELECT Id, json_extract(Data, '$."$type"'), json_extract(Data, '$.ArtKey')
                   FROM cards WHERE json_extract(Data, '$."$type"') IN ('TCardItem', 'TCardSkill')"""
            ).fetchall()
            titles = database.execute(
                """SELECT Id, json_extract(Data, '$.Localization.Title.Text') FROM cards"""
            ).fetchall()
            database.close()
    names = {
        template_id: title.strip()
        for template_id, title in titles
        if isinstance(title, str) and title.strip()
    }
    return rows, names


def card_index(cards, names, current, previous, buildid, gamedata_etag):
    """The published index. A TemplateId GameData no longer has keeps its earlier art and name,
    so Runs from older patches still resolve."""
    cards, names = dict(cards), dict(names)
    for key, mapping in (("cards", cards), ("names", names)):
        for template_id, value in (previous or {}).get(key, {}).items():
            if template_id not in current:
                mapping.setdefault(template_id, value)
    return {
        "version": 1,
        "game": {"buildid": buildid, "gameData": gamedata_etag},
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
    rows, names = game_cards(args.game_data)
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
    previous = None
    if args.previous and os.path.exists(args.previous):
        with open(args.previous) as file:
            previous = json.load(file)
    current = {row[0] for row in rows} | set(names)
    index = card_index(cards, names, current, previous, args.buildid, args.gamedata_etag)
    with open(os.path.join(args.out_dir, "index.json"), "w") as file:
        json.dump(index, file, separators=(",", ":"))


if __name__ == "__main__":
    main()
