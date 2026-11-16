# /// script
# requires-python = ">=3.13,<3.15"
# dependencies = ["UnityPy==1.25.4", "Pillow==12.0.0"]
# ///
"""Extracts Card Art (root ADR 0020) from a The Bazaar Data directory.

    uv run --locked --script extract.py <Data dir> <GameData.db.zip> <out dir>
        [--buildid <id>] [--gamedata-etag <etag>] [--previous <index.json>]

Reads the live GameData (the game downloads it from
https://data.playthebazaar.com/static/GameData.db.zip; the copy in the depot is
months older), the Addressables catalog and the card and skill bundles under
StreamingAssets/aa, and writes one WebP per distinct art to <out dir>, named
by its content digest, plus index.json mapping each TemplateId to its file.
Entries of --previous whose card the new GameData no longer has are kept, so
runs from before a patch keep their art. It fails without writing index.json
when too few cards resolve, so a game update that moves the art stops the
publish instead of shipping a thin set. The bot reads index.json
(src/art.ts); release/card-art.ts publishes the directory.
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

import UnityPy
from PIL import Image

# A Large card is 3 board units wide; at 2x it needs about 400 px of the square art.
ITEM_HEIGHT = 400
SKILL_HEIGHT = 128
MIN_ITEM_SHARE = 0.95
MIN_SKILL_SHARE = 0.90
NONE = 0xFFFFFFFF


class Catalog:
    """Reads the string keys of an Addressables v2 binary catalog (catalog.bin)."""

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
        """Maps each string key to the internal id of its first location."""
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
    """Returns (TemplateId, type, ArtKey) for every item and skill in GameData."""
    with zipfile.ZipFile(game_data_zip) as archive:
        name = next(n for n in archive.namelist() if n.endswith("GameData.db"))
        with tempfile.TemporaryDirectory() as directory:
            path = archive.extract(name, directory)
            database = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
            rows = database.execute(
                """SELECT Id, json_extract(Data, '$."$type"'), json_extract(Data, '$.ArtKey')
                   FROM cards WHERE json_extract(Data, '$."$type"') IN ('TCardItem', 'TCardSkill')"""
            ).fetchall()
            database.close()
    return rows


def webp(image, height, mode):
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
    rows = game_cards(args.game_data)
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
    summary = {"items": items, "skills": skills, "files": len(set(cards.values()))}
    print(json.dumps(summary))
    if items[0] < items[1] * MIN_ITEM_SHARE or skills[0] < skills[1] * MIN_SKILL_SHARE:
        raise SystemExit(f"too few cards resolved: {summary}")
    if args.previous and os.path.exists(args.previous):
        with open(args.previous) as file:
            previous = json.load(file).get("cards", {})
        current = {row[0] for row in rows}
        for template_id, name in previous.items():
            if template_id not in current:
                cards[template_id] = name
    index = {
        "version": 1,
        "game": {"buildid": args.buildid, "gameData": args.gamedata_etag},
        "cards": dict(sorted(cards.items())),
    }
    with open(os.path.join(args.out_dir, "index.json"), "w") as file:
        json.dump(index, file, separators=(",", ":"))


if __name__ == "__main__":
    main()
