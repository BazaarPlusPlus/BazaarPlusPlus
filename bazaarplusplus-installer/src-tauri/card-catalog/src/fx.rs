//! Enchantment FX: the inputs of the non-premium PremiumCard vertex and fragment programs
//! (ADR 0022), one immutable descriptor per card material.

use crate::json::{canonical, hex};
use crate::resample::{resize, Filter};
use crate::texture::{py_round, texture_raw, Raw};
use crate::unity::{Env, Serialized, Value};
use crate::webp;
use anyhow::{anyhow, Context, Result};
use serde_json::{json, Map, Value as Json};
use sha2::{Digest, Sha256};
use std::collections::{BTreeSet, HashMap};
use std::path::{Path, PathBuf};

/// Union of texture and UnityPerMaterial reads across the non-premium vertex and fragment
/// programs, all three sizes and both Tiny geometry modes, Steam build 26592248.
const INPUTS: &str = include_str!("premium-card-v1.json");

/// The game applies CardArtShaderVariables.EnchantmentKeywords to the cloned material.
const KEYWORDS: [(&str, &str); 16] = [
    ("Deadly", "DANGEROUS"),
    ("Ethereal", "SHINY"),
    ("Fiery", "FIERY"),
    ("Golden", "GOLDEN"),
    ("Heavy", "HEAVY"),
    ("Icy", "ICY"),
    ("Mystical", "MYSTICAL"),
    ("Obsidian", "OBSIDIAN"),
    ("Radiant", "PRISMATIC"),
    ("Restorative", "RESTORATIVE"),
    ("Shielded", "TEMPERED"),
    ("Shiny", "DAZZLING"),
    ("Tiny", "TINY"),
    ("Toxic", "TOXIC"),
    ("Turbo", "TURBO"),
    ("Mossy", "MOSSY"),
];

const POLAR_TEXTURES: [&str; 2] = ["_MysticalRunes", "_MysticalBorealLines"];

pub fn immutable(output: &Path, prefix: &str, suffix: &str, data: &[u8]) -> Result<String> {
    let filename = format!("{prefix}{}{suffix}", &hex(&Sha256::digest(data))[..20]);
    let path = output.join(&filename);
    if !path.exists() {
        std::fs::write(&path, data)?;
    }
    Ok(filename)
}

fn texture_limit(slot: &str) -> usize {
    if POLAR_TEXTURES.contains(&slot)
        || matches!(
            slot,
            "_EnchantmentMask" | "_Mossy_Noise" | "_Shared_Smoke" | "_DazzlingGlintMap"
        )
    {
        256
    } else {
        128
    }
}

fn texture_size(width: usize, height: usize, slot: &str) -> (usize, usize) {
    let limit = texture_limit(slot);
    if POLAR_TEXTURES.contains(&slot) {
        // Polar sampling magnifies the strip. Preserve its short axis and fine lines.
        return (width.min(limit), height.min(limit));
    }
    let longest = width.max(height);
    if limit >= longest {
        return (width, height);
    }
    let scale = limit as f64 / longest as f64;
    let side = |s: usize| usize::try_from(py_round(s as f64 * scale).max(1)).unwrap_or(1);
    (side(width), side(height))
}

/// Packed channels are independent signals, so each band is resized on its own; RGB under
/// transparent pixels is kept.
fn texture_webp(rgba: &[u8], width: usize, height: usize, size: (usize, usize)) -> Result<Vec<u8>> {
    let pixels = if (width, height) == size {
        rgba.to_vec()
    } else {
        let mut merged = vec![0u8; size.0 * size.1 * 4];
        for band in 0..4 {
            let plane: Vec<u8> = rgba.iter().skip(band).step_by(4).copied().collect();
            let resized = resize(&plane, width, height, 1, size.0, size.1, Filter::Box);
            for (i, v) in resized.into_iter().enumerate() {
                merged[i * 4 + band] = v;
            }
        }
        merged
    };
    webp::encode(
        &pixels,
        size.0,
        size.1,
        true,
        &webp::Options {
            lossless: true,
            quality: 80.0,
            method: 4,
            exact: true,
        },
    )
}

fn keyword_sets(valid: &[String]) -> Json {
    let base: BTreeSet<&str> = valid
        .iter()
        .map(String::as_str)
        .filter(|k| {
            !matches!(*k, "_ISPREMIUM_ON" | "_ISTINY_ON") && !k.starts_with("_ENCHANTMENTSTATUS_")
        })
        .collect();
    let mut variants = Map::new();
    for (name, suffix) in KEYWORDS {
        let status = format!("_ENCHANTMENTSTATUS_{suffix}");
        let mut active: BTreeSet<&str> = base.clone();
        active.insert(&status);
        let mut tiny = active.clone();
        tiny.insert("_ISTINY_ON");
        variants.insert(name.into(), json!({ "normal": active, "tiny": tiny }));
    }
    Json::Object(variants)
}

fn number(v: &Value) -> Json {
    match v {
        Value::Float(f) => json!(f),
        other => other.int().map_or(Json::Null, |i| json!(i)),
    }
}

/// A shader property's type and defaults.
#[derive(Clone)]
pub struct ShaderProperty {
    /// 0 colour, 1 vector, otherwise a scalar.
    pub kind: i64,
    pub defaults: [Json; 4],
    pub default_texture: Option<String>,
}

/// What a texture binding records; `key` identifies the texture object.
#[derive(Clone)]
pub struct TextureInfo {
    pub key: (String, i64),
    pub width: usize,
    pub height: usize,
    pub color_space: Json,
    pub mip_count: Json,
    pub wrap: [Json; 2],
    pub filter: Json,
    pub mip_bias: Json,
    pub aniso: Json,
}

pub struct Slot {
    pub texture: Option<TextureInfo>,
    /// Scale x, y and offset x, y.
    pub transform: [Json; 4],
}

pub struct Material {
    pub floats: HashMap<String, Json>,
    pub colors: HashMap<String, [Json; 4]>,
    pub slots: HashMap<String, Slot>,
    pub keywords: Vec<String>,
}

/// Writes descriptors; textures other than the illustration are encoded through `encode`.
#[derive(Clone)]
pub struct Describer {
    properties: HashMap<String, ShaderProperty>,
    values: Vec<String>,
    textures: Vec<String>,
    output: PathBuf,
    texture_files: HashMap<(String, i64, String), Map<String, Json>>,
}

pub type Encode<'a> = dyn FnMut(&TextureInfo, (usize, usize)) -> Result<String> + 'a;

impl Describer {
    pub fn new(properties: HashMap<String, ShaderProperty>, output: &Path) -> Result<Self> {
        let inputs: Json = serde_json::from_str(INPUTS)?;
        let names = |key: &str| -> Vec<String> {
            inputs[key]
                .as_array()
                .into_iter()
                .flatten()
                .filter_map(Json::as_str)
                .map(str::to_owned)
                .collect()
        };
        Ok(Self {
            properties,
            values: names("values"),
            textures: names("textures"),
            output: output.to_owned(),
            texture_files: HashMap::new(),
        })
    }

    fn property(&self, name: &str) -> Result<&ShaderProperty> {
        self.properties
            .get(name)
            .ok_or_else(|| anyhow!("shader has no property {name}"))
    }

    fn binding(
        &mut self,
        name: &str,
        slot: Option<&Slot>,
        art: &str,
        height: usize,
        encode: &mut Encode<'_>,
    ) -> Result<Json> {
        let transform = slot.map_or_else(|| json!([1, 1, 0, 0]), |s| json!(s.transform));
        let Some(info) = slot.and_then(|s| s.texture.as_ref()) else {
            let default = self
                .property(name)?
                .default_texture
                .clone()
                .ok_or_else(|| anyhow!("{name} has no default texture"))?;
            return Ok(json!({ "default": default, "st": transform }));
        };
        let profile = if name == "_MainTex" {
            art.to_owned()
        } else {
            format!("{}:{}", texture_limit(name), POLAR_TEXTURES.contains(&name))
        };
        let key = (info.key.0.clone(), info.key.1, profile);
        if !self.texture_files.contains_key(&key) {
            // This profile samples RGB or RG from the illustration. Packed effect maps need
            // all four source channels, including RGB under transparent pixels.
            let (file, size) = if name == "_MainTex" {
                let w = usize::try_from(
                    py_round(info.width as f64 * height as f64 / info.height as f64).max(1),
                )?;
                (art.to_owned(), (w, height))
            } else {
                let size = texture_size(info.width, info.height, name);
                (encode(info, size)?, size)
            };
            let mut entry = Map::new();
            entry.insert("file".into(), json!(file));
            entry.insert("size".into(), json!([size.0, size.1]));
            entry.insert("sourceSize".into(), json!([info.width, info.height]));
            entry.insert("colorSpace".into(), info.color_space.clone());
            entry.insert("wrap".into(), json!(info.wrap));
            entry.insert("filter".into(), info.filter.clone());
            entry.insert("mipCount".into(), info.mip_count.clone());
            entry.insert("mipBias".into(), info.mip_bias.clone());
            entry.insert("aniso".into(), info.aniso.clone());
            self.texture_files.insert(key.clone(), entry);
        }
        let mut entry = self.texture_files[&key].clone();
        entry.insert("st".into(), transform);
        Ok(Json::Object(entry))
    }

    /// Write the material's descriptor and return its file name.
    pub fn describe(
        &mut self,
        material: &Material,
        art: &str,
        height: usize,
        encode: &mut Encode<'_>,
    ) -> Result<String> {
        let mut values = Map::new();
        for name in self.values.clone() {
            if name.ends_with("_ST") {
                continue; // carried on the texture binding
            }
            let prop = self.property(&name)?;
            let value = if prop.kind == 0 || prop.kind == 1 {
                json!(material.colors.get(&name).unwrap_or(&prop.defaults))
            } else {
                material
                    .floats
                    .get(&name)
                    .cloned()
                    .unwrap_or_else(|| prop.defaults[0].clone())
            };
            values.insert(name, value);
        }
        let size = material
            .keywords
            .iter()
            .find_map(|k| k.strip_prefix("_CRDSIZE_"))
            .map(str::to_lowercase)
            .context("material without a _CRDSIZE_ keyword")?;
        let mut textures = Map::new();
        for name in self.textures.clone() {
            let binding = self.binding(&name, material.slots.get(&name), art, height, encode)?;
            textures.insert(name, binding);
        }
        let descriptor = json!({
            "version": 1,
            "profile": "premium-card-v1",
            "size": size,
            "keywords": keyword_sets(&material.keywords),
            "values": values,
            "textures": textures,
        });
        immutable(
            &self.output,
            "fx-",
            ".json",
            canonical(&descriptor).as_bytes(),
        )
    }
}

/// Reads card materials from the bundles and describes them.
pub struct MaterialExporter {
    shader: (String, i64),
    describer: Describer,
}

impl MaterialExporter {
    pub fn new(shader_file: &Serialized, shader_id: i64, output: &Path) -> Result<Self> {
        let shader = shader_file.read(shader_id)?;
        let mut properties = HashMap::new();
        for p in shader.at("m_ParsedForm.m_PropInfo.m_Props")?.list()? {
            let defaults =
                [0, 1, 2, 3].map(|i| p.at(&format!("m_DefValue[{i}]")).map_or(Json::Null, number));
            properties.insert(
                p.at("m_Name")?.str()?.to_owned(),
                ShaderProperty {
                    kind: p.at("m_Type")?.int()?,
                    defaults,
                    default_texture: p
                        .at("m_DefTexture.m_DefaultName")
                        .ok()
                        .and_then(|v| v.str().ok())
                        .map(str::to_owned),
                },
            );
        }
        Ok(Self {
            shader: (shader_file.name.clone(), shader_id),
            describer: Describer::new(properties, output)?,
        })
    }

    /// A copy of the descriptor writer for one worker thread.
    pub fn describer(&self) -> Describer {
        self.describer.clone()
    }

    /// Everything a descriptor needs from the bundles: the material's values and the raw
    /// pixels of its profile textures. Describing it needs no bundle access.
    pub fn prepare(
        &self,
        env: &Env,
        material_file: &Serialized,
        material: &Value,
    ) -> Result<Prepared> {
        let (shader_file, shader_id) = env.deref(material_file, material.at("m_Shader")?)?;
        if (shader_file.name.clone(), shader_id) != self.shader {
            return Err(anyhow!("unsupported card shader"));
        }
        let saved = material.at("m_SavedProperties")?;
        let pairs = |key: &str| -> Result<Vec<(String, &Value)>> {
            saved
                .at(key)?
                .list()?
                .iter()
                .map(|p| Ok((p.at("first")?.str()?.to_owned(), p.at("second")?)))
                .collect()
        };
        let floats = pairs("m_Floats")?
            .into_iter()
            .map(|(k, v)| (k, number(v)))
            .collect();
        let mut colors = HashMap::new();
        for (k, c) in pairs("m_Colors")? {
            colors.insert(
                k,
                [
                    number(c.at("r")?),
                    number(c.at("g")?),
                    number(c.at("b")?),
                    number(c.at("a")?),
                ],
            );
        }
        let mut slots = HashMap::new();
        let mut raws: HashMap<(String, i64), Raw> = HashMap::new();
        for (name, s) in pairs("m_TexEnvs")? {
            // Only the profile's slots are read; others can point into bundles the set does not load.
            if !self.describer.textures.contains(&name) {
                continue;
            }
            let transform = [
                number(s.at("m_Scale.x")?),
                number(s.at("m_Scale.y")?),
                number(s.at("m_Offset.x")?),
                number(s.at("m_Offset.y")?),
            ];
            let texture = if s.at("m_Texture.m_PathID")?.int()? == 0 {
                None
            } else {
                let (file, path_id) = env.deref(material_file, s.at("m_Texture")?)?;
                let tex = file.read(path_id)?;
                let settings = tex.at("m_TextureSettings")?;
                let key = (file.name.clone(), path_id);
                // The illustration is the art file already written; only effect maps are encoded.
                if name != "_MainTex" && !raws.contains_key(&key) {
                    raws.insert(key.clone(), texture_raw(env, &file, path_id, true)?);
                }
                Some(TextureInfo {
                    key,
                    width: usize::try_from(tex.at("m_Width")?.int()?)?,
                    height: usize::try_from(tex.at("m_Height")?.int()?)?,
                    color_space: number(tex.at("m_ColorSpace")?),
                    mip_count: number(tex.at("m_MipCount")?),
                    wrap: [
                        number(settings.at("m_WrapU")?),
                        number(settings.at("m_WrapV")?),
                    ],
                    filter: number(settings.at("m_FilterMode")?),
                    mip_bias: number(settings.at("m_MipBias")?),
                    aniso: number(settings.at("m_Aniso")?),
                })
            };
            slots.insert(name, Slot { texture, transform });
        }
        let keywords = material
            .at("m_ValidKeywords")?
            .list()?
            .iter()
            .map(|k| k.str().map(str::to_owned))
            .collect::<Result<_>>()?;
        Ok(Prepared {
            material: Material {
                floats,
                colors,
                slots,
                keywords,
            },
            raws,
        })
    }
}

pub struct Prepared {
    material: Material,
    raws: HashMap<(String, i64), Raw>,
}

impl Describer {
    /// The descriptor for a prepared material, encoding its textures here.
    pub fn describe_prepared(
        &mut self,
        prepared: &Prepared,
        art: &str,
        height: usize,
    ) -> Result<String> {
        let output = self.output.clone();
        let mut encode = |info: &TextureInfo, size: (usize, usize)| -> Result<String> {
            let image = prepared
                .raws
                .get(&info.key)
                .ok_or_else(|| anyhow!("texture pixels missing"))?
                .decode()?;
            immutable(
                &output,
                "tex-",
                ".webp",
                &texture_webp(&image.rgba, image.width, image.height, size)?,
            )
        };
        self.describe(&prepared.material, art, height, &mut encode)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::extract::ITEM_HEIGHT;

    #[test]
    fn enchantment_and_tiny_geometry_are_independent() {
        let base = [
            "_CRDSIZE_MEDIUM",
            "_DEBUG_OFF",
            "_ENCHANTMENTSTATUS_UNENCHANTED",
            "_ISPREMIUM_ON",
        ]
        .map(str::to_owned);
        let variants = keyword_sets(&base);
        assert_eq!(variants.as_object().unwrap().len(), 16);
        assert_eq!(
            variants["Ethereal"]["normal"],
            json!(["_CRDSIZE_MEDIUM", "_DEBUG_OFF", "_ENCHANTMENTSTATUS_SHINY"])
        );
        assert_eq!(
            variants["Tiny"]["tiny"],
            json!([
                "_CRDSIZE_MEDIUM",
                "_DEBUG_OFF",
                "_ENCHANTMENTSTATUS_TINY",
                "_ISTINY_ON"
            ])
        );
        assert!(!variants["Tiny"]["normal"]
            .as_array()
            .unwrap()
            .contains(&json!("_ISTINY_ON")));
    }

    #[test]
    fn web_dimensions_preserve_packed_mask_aspect_and_polar_strip_height() {
        assert_eq!(texture_size(1024, 512, "_EnchantmentMask"), (256, 128));
        assert_eq!(texture_size(2048, 256, "_MysticalRunes"), (256, 256));
        assert_eq!(texture_size(128, 64, "_Shared_Smoke"), (128, 64));
    }

    fn decode(data: &[u8]) -> Vec<u8> {
        let (mut w, mut h) = (0, 0);
        // SAFETY: `data` is a complete WebP file; the returned buffer is copied, then freed.
        unsafe {
            let p = libwebp_sys::WebPDecodeRGBA(data.as_ptr(), data.len(), &mut w, &mut h);
            assert!(!p.is_null());
            let out = std::slice::from_raw_parts(p, (w * h * 4) as usize).to_vec();
            libwebp_sys::WebPFree(p.cast());
            out
        }
    }

    #[test]
    fn data_texture_preserves_hidden_rgb_and_alpha() {
        let pixels = [9, 77, 239, 0, 43, 81, 255, 127];
        assert_eq!(
            decode(&texture_webp(&pixels, 2, 1, (2, 1)).unwrap()),
            pixels
        );
        assert_eq!(
            decode(&texture_webp(&pixels, 2, 1, (1, 1)).unwrap()),
            [26, 79, 247, 64]
        );
    }

    fn info(key: i64, width: usize, height: usize, meta: &[Json]) -> TextureInfo {
        TextureInfo {
            key: ("fixture".into(), key),
            width,
            height,
            color_space: meta[0].clone(),
            mip_count: meta[1].clone(),
            wrap: [meta[2].clone(), meta[3].clone()],
            filter: meta[4].clone(),
            mip_bias: meta[5].clone(),
            aniso: meta[6].clone(),
        }
    }

    #[test]
    fn bindings_include_shader_defaults_and_each_slots_own_transform() {
        let dir = tempfile::tempdir().unwrap();
        let properties = HashMap::from([(
            "_EnchantmentMask".to_owned(),
            ShaderProperty {
                kind: 4,
                defaults: [Json::Null, Json::Null, Json::Null, Json::Null],
                default_texture: Some("white".into()),
            },
        )]);
        let mut describer = Describer::new(properties, dir.path()).unwrap();
        let mut never = |_: &TextureInfo, _: (usize, usize)| -> Result<String> { unreachable!() };
        assert_eq!(
            describer
                .binding("_EnchantmentMask", None, "", 400, &mut never)
                .unwrap(),
            json!({ "default": "white", "st": [1, 1, 0, 0] })
        );
        let texture = info(
            7,
            512,
            256,
            &[
                json!(0),
                json!(1),
                json!(0),
                json!(1),
                json!(2),
                json!(0),
                json!(1),
            ],
        );
        let slot = |x: f64| Slot {
            texture: Some(texture.clone()),
            transform: [json!(2), json!(1), json!(x), json!(0.25)],
        };
        let main = describer
            .binding("_MainTex", Some(&slot(0.0)), "face.webp", 400, &mut never)
            .unwrap();
        assert_eq!(
            (
                &main["size"],
                &main["sourceSize"],
                &main["st"],
                &main["wrap"]
            ),
            (
                &json!([800, 400]),
                &json!([512, 256]),
                &json!([2, 1, 0.0, 0.25]),
                &json!([0, 1])
            )
        );
        assert_eq!(
            describer
                .binding("_MainTex", Some(&slot(0.5)), "face.webp", 400, &mut never)
                .unwrap()["st"],
            json!([2, 1, 0.5, 0.25])
        );
    }

    /// Recorded materials from a real build: only the encoded WebP bytes are replaced by
    /// their recorded file names; defaults, bindings and the JSON digest are production code.
    #[test]
    fn real_materials_match_descriptor_digests_without_game_pixels() {
        let fixture: Json =
            serde_json::from_str(include_str!("../tests/fixtures/enchantment-fx.json")).unwrap();
        let mut properties = HashMap::new();
        for (name, default) in fixture["defaults"].as_object().unwrap() {
            let (kind, defaults) = match default.as_array() {
                Some(v) => (1, [v[0].clone(), v[1].clone(), v[2].clone(), v[3].clone()]),
                None => (2, [default.clone(), json!(0), json!(0), json!(0)]),
            };
            properties.insert(
                name.clone(),
                ShaderProperty {
                    kind,
                    defaults,
                    default_texture: None,
                },
            );
        }
        for (name, value) in fixture["textureDefaults"].as_object().unwrap() {
            properties.insert(
                name.clone(),
                ShaderProperty {
                    kind: 4,
                    defaults: [Json::Null, Json::Null, Json::Null, Json::Null],
                    default_texture: value.as_str().map(str::to_owned),
                },
            );
        }
        let mut textures = HashMap::new();
        let mut names = HashMap::new();
        for (i, (file, meta)) in fixture["textures"].as_object().unwrap().iter().enumerate() {
            let m = meta.as_array().unwrap();
            let t = info(
                i as i64 + 1,
                m[0].as_u64().unwrap() as usize,
                m[1].as_u64().unwrap() as usize,
                &m[2..],
            );
            names.insert(t.key.1, file.clone());
            textures.insert(file.clone(), t);
        }
        let dir = tempfile::tempdir().unwrap();
        let mut describer = Describer::new(properties, dir.path()).unwrap();
        let mut encode =
            |t: &TextureInfo, _: (usize, usize)| -> Result<String> { Ok(names[&t.key.1].clone()) };
        for item in fixture["items"].as_array().unwrap() {
            let mut slots = HashMap::new();
            for (name, s) in item["slots"].as_object().unwrap() {
                let s = s.as_array().unwrap();
                slots.insert(
                    name.clone(),
                    Slot {
                        texture: s[0].as_str().and_then(|f| textures.get(f)).cloned(),
                        transform: [s[1].clone(), s[2].clone(), s[3].clone(), s[4].clone()],
                    },
                );
            }
            let material = Material {
                floats: item["floats"]
                    .as_object()
                    .unwrap()
                    .iter()
                    .map(|(k, v)| (k.clone(), v.clone()))
                    .collect(),
                colors: item["colors"]
                    .as_object()
                    .unwrap()
                    .iter()
                    .map(|(k, v)| {
                        let c = v.as_array().unwrap();
                        (
                            k.clone(),
                            [c[0].clone(), c[1].clone(), c[2].clone(), c[3].clone()],
                        )
                    })
                    .collect(),
                slots,
                keywords: item["keywords"]
                    .as_array()
                    .unwrap()
                    .iter()
                    .map(|k| k.as_str().unwrap().to_owned())
                    .collect(),
            };
            let art = item["slots"]["_MainTex"][0].as_str().unwrap();
            assert_eq!(
                describer
                    .describe(&material, art, ITEM_HEIGHT, &mut encode)
                    .unwrap(),
                item["descriptor"],
                "{}",
                item["title"]
            );
        }
    }
}
