"""Inputs of the non-premium PremiumCard vertex and fragment programs (ADR 0022)."""

import hashlib
import io
import json
from pathlib import Path

# Union of texture and UnityPerMaterial reads across the non-premium vertex and fragment
# programs, all three sizes and both Tiny geometry modes, Steam build 26592248.
INPUTS = json.loads(Path(__file__).with_name("premium-card-v1.json").read_text())
# The game applies CardArtShaderVariables.EnchantmentKeywords to the cloned material.
KEYWORDS = {
    "Deadly": "DANGEROUS",
    "Ethereal": "SHINY",
    "Fiery": "FIERY",
    "Golden": "GOLDEN",
    "Heavy": "HEAVY",
    "Icy": "ICY",
    "Mystical": "MYSTICAL",
    "Obsidian": "OBSIDIAN",
    "Radiant": "PRISMATIC",
    "Restorative": "RESTORATIVE",
    "Shielded": "TEMPERED",
    "Shiny": "DAZZLING",
    "Tiny": "TINY",
    "Toxic": "TOXIC",
    "Turbo": "TURBO",
    "Mossy": "MOSSY",
}


def immutable(output, prefix, suffix, data):
    filename = prefix + hashlib.sha256(data).hexdigest()[:20] + suffix
    if not (output / filename).exists():
        (output / filename).write_bytes(data)
    return filename


def png(image):
    buffer = io.BytesIO()
    image.convert("RGBA").save(buffer, "PNG")
    return buffer.getvalue()


def keyword_sets(saved):
    base = set(saved) - {"_ISPREMIUM_ON", "_ISTINY_ON"}
    base = {k for k in base if not k.startswith("_ENCHANTMENTSTATUS_")}
    variants = {}
    for name, suffix in KEYWORDS.items():
        active = base | {"_ENCHANTMENTSTATUS_" + suffix}
        variants[name] = {"normal": sorted(active), "tiny": sorted(active | {"_ISTINY_ON"})}
    return variants


class MaterialExporter:
    def __init__(self, shader, output):
        self.shader = shader
        self.properties = {p.m_Name: p for p in shader.m_ParsedForm.m_PropInfo.m_Props}
        self.output = output
        self.texture_files = {}

    def binding(self, name, saved, art, height):
        slot = saved.get(name)
        transform = (
            [1, 1, 0, 0]
            if not slot
            else [slot.m_Scale.x, slot.m_Scale.y, slot.m_Offset.x, slot.m_Offset.y]
        )
        if not slot or not slot.m_Texture.path_id:
            return {"default": self.properties[name].m_DefTexture.m_DefaultName, "st": transform}
        ref = slot.m_Texture.deref()
        key = (ref.assets_file.name, ref.path_id, art if name == "_MainTex" else None)
        if key not in self.texture_files:
            tex = ref.read()
            width = tex.m_Width
            source_height = tex.m_Height
            size = [width, source_height]
            # This profile samples RGB or RG from the illustration. Packed effect maps
            # need all four source channels, including RGB under transparent pixels.
            if name == "_MainTex":
                filename = art
                size = [max(1, round(width * height / source_height)), height]
            else:
                filename = immutable(self.output, "tex-", ".png", png(tex.image))
            settings = tex.m_TextureSettings
            self.texture_files[key] = {
                "file": filename,
                "size": size,
                "sourceSize": [width, source_height],
                "colorSpace": tex.m_ColorSpace,
                "wrap": [settings.m_WrapU, settings.m_WrapV],
                "filter": settings.m_FilterMode,
                "mipCount": tex.m_MipCount,
                "mipBias": settings.m_MipBias,
                "aniso": settings.m_Aniso,
            }
        return self.texture_files[key] | {"st": transform}

    def export(self, material, art, height):
        if material.m_Shader.deref() is not self.shader.object_reader:
            raise ValueError("unsupported card shader")
        saved = material.m_SavedProperties
        floats = dict(saved.m_Floats)
        colors = {n: [c.r, c.g, c.b, c.a] for n, c in saved.m_Colors}
        values = {}
        for name in INPUTS["values"]:
            if name.endswith("_ST"):
                continue  # carried on the texture binding
            prop = self.properties[name]
            if prop.m_Type in (0, 1):
                values[name] = colors.get(
                    name, [getattr(prop, f"m_DefValue_{i}_") for i in range(4)]
                )
            else:
                values[name] = floats.get(name, prop.m_DefValue_0_)
        slots = dict(saved.m_TexEnvs)
        size = next(
            k.removeprefix("_CRDSIZE_").lower()
            for k in material.m_ValidKeywords
            if k.startswith("_CRDSIZE_")
        )
        textures = {name: self.binding(name, slots, art, height) for name in INPUTS["textures"]}
        descriptor = {
            "version": 1,
            "profile": "premium-card-v1",
            "size": size,
            "keywords": keyword_sets(material.m_ValidKeywords),
            "values": values,
            "textures": textures,
        }
        data = json.dumps(
            descriptor, sort_keys=True, separators=(",", ":"), allow_nan=False
        ).encode()
        return immutable(self.output, "fx-", ".json", data)
