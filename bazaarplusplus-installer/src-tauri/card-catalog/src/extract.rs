//! The whole Card Art set: art files, Card Text, Enchantment FX and the index naming them.

use crate::addressables;
use crate::fx::{MaterialExporter, Prepared};
use crate::json::{hex, json_string};
use crate::resample::{resize, resize_rgba, Filter};
use crate::text::{write_card_text, Previous};
use crate::texture::{py_round, sprite_raw, texture_raw, Image, Raw};
use crate::unity::{Env, Serialized};
use crate::webp;
use anyhow::{anyhow, bail, Context, Result};
use rusqlite::Connection;
use serde_json::{json, Map, Value as Json};
use sha2::{Digest, Sha256};
use std::collections::{BTreeMap, HashMap, HashSet};
use std::fmt::Write as _;
use std::path::{Path, PathBuf};
use std::rc::Rc;

/// A Large card is 3 board units wide; at 2x it needs about 400 px of the square art.
pub const ITEM_HEIGHT: usize = 400;
pub const SKILL_HEIGHT: usize = 128;
const MIN_ITEM_SHARE: f64 = 0.95;
const MIN_SKILL_SHARE: f64 = 0.90;

pub struct Inputs<'a> {
    /// The game's `Data` (`TheBazaar_Data`, or `Contents/Resources/Data` on macOS).
    pub data_dir: &'a Path,
    /// An unpacked `GameData.db`.
    pub game_data: &'a Path,
    /// `zh-CN.bytes`.
    pub translations: &'a Path,
    /// The previous `index.json`; its directory holds its Card Text files.
    pub previous: Option<&'a Path>,
    pub out_dir: &'a Path,
    pub buildid: Option<String>,
    pub gamedata_etag: Option<String>,
    pub zh_etag: Option<String>,
    /// Publication refuses a set that resolved too few cards.
    pub publication: bool,
    pub require_fx: bool,
    /// Called with (steps done, total steps) across card art and Enchantment FX export.
    pub progress: Option<&'a dyn Fn(usize, usize)>,
    /// Threads that decode, resize and encode art.
    pub threads: usize,
}

#[derive(Debug, Default)]
pub struct Summary {
    pub items: (usize, usize),
    pub skills: (usize, usize),
    pub files: usize,
    pub names: usize,
    pub fx_items: usize,
    pub fx_materials: usize,
    pub fx_textures: usize,
    pub fx_bytes: u64,
    pub fx_sizes: BTreeMap<String, usize>,
    pub fx_masks: usize,
}

impl Summary {
    pub fn json(&self) -> String {
        json!({
            "fx": {
                "items": self.fx_items, "materials": self.fx_materials, "textures": self.fx_textures,
                "bytes": self.fx_bytes, "sizes": self.fx_sizes, "masks": self.fx_masks,
            },
            "items": [self.items.0, self.items.1],
            "skills": [self.skills.0, self.skills.1],
            "files": self.files,
            "names": self.names,
        })
        .to_string()
    }
}

/// Python's `str.strip()`.
fn strip(s: &str) -> &str {
    s.trim_matches(|c: char| c.is_whitespace() || ('\u{1c}'..='\u{1f}').contains(&c))
}

fn bundles(data_dir: &Path) -> Result<Vec<PathBuf>> {
    let aa = data_dir.join("StreamingAssets").join("aa");
    let mut out = Vec::new();
    for entry in std::fs::read_dir(&aa).with_context(|| aa.display().to_string())? {
        let dir = entry?.path();
        if !dir.is_dir() {
            continue;
        }
        for file in std::fs::read_dir(&dir)? {
            let path = file?.path();
            let name = path
                .file_name()
                .map(|n| n.to_string_lossy().into_owned())
                .unwrap_or_default();
            if (name.starts_with("card_")
                && name.ends_with("_assets_all.bundle")
                && name != "card_vfx_assets_all.bundle")
                || name == "skills_assets_all.bundle"
            {
                out.push(path);
            }
        }
    }
    out.sort();
    if out.is_empty() {
        bail!("no card or skill bundles under StreamingAssets/aa");
    }
    Ok(out)
}

type Object = (Rc<Serialized>, i64, i32);

/// Each loaded bundle's container: asset path → object; a Sprite wins over its texture.
fn containers(env: &Env, files: &[String], objects: &mut HashMap<String, Object>) -> Result<()> {
    for name in files {
        let file = env.file(name)?;
        let mut ids: Vec<i64> = file
            .objects
            .keys()
            .copied()
            .filter(|&id| file.class_id(id) == Some(142))
            .collect();
        ids.sort_unstable();
        for id in ids {
            let bundle = file.read(id)?;
            for pair in bundle.at("m_Container")?.list()? {
                let path = pair.at("first")?.str()?.to_owned();
                let (target, pid) = env.deref(&file, pair.at("second.asset")?)?;
                let class = target.class_id(pid).unwrap_or(-1);
                if !objects.contains_key(&path) || class == 213 {
                    objects.insert(path, (target, pid, class));
                }
            }
        }
    }
    Ok(())
}

fn art_webp(image: &Image, height: usize, alpha: bool) -> Result<Vec<u8>> {
    let width =
        usize::try_from(py_round(image.width as f64 * height as f64 / image.height as f64).max(1))?;
    let pixels = if alpha {
        resize_rgba(
            &image.rgba,
            image.width,
            image.height,
            width,
            height,
            Filter::Lanczos,
        )
    } else {
        // Pillow resizes RGB, so alpha plays no part.
        let rgb: Vec<u8> = image
            .rgba
            .chunks(4)
            .flat_map(|p| [p[0], p[1], p[2]])
            .collect();
        resize(
            &rgb,
            image.width,
            image.height,
            3,
            width,
            height,
            Filter::Lanczos,
        )
        .chunks(3)
        .flat_map(|p| [p[0], p[1], p[2], 255])
        .collect()
    };
    // method 6 takes ~1 s per RGBA image for a 2 % smaller file.
    webp::encode(
        &pixels,
        width,
        height,
        alpha,
        &webp::Options {
            lossless: false,
            quality: 80.0,
            method: 4,
            exact: false,
        },
    )
}

struct Rows {
    cards: Vec<(String, String, Option<String>)>,
    names: BTreeMap<String, String>,
    current: HashSet<String>,
}

fn game_cards(db: &Connection) -> Result<Rows> {
    let mut stmt = db.prepare(
        r#"SELECT Id, json_extract(CAST(Data AS TEXT), '$."$type"'), json_extract(CAST(Data AS TEXT), '$.ArtKey')
           FROM cards WHERE json_extract(CAST(Data AS TEXT), '$."$type"') IN ('TCardItem', 'TCardSkill')"#,
    )?;
    let cards = stmt
        .query_map([], |r| {
            Ok((
                r.get::<_, String>(0)?,
                r.get::<_, String>(1)?,
                r.get::<_, Option<String>>(2)?,
            ))
        })?
        .collect::<rusqlite::Result<Vec<_>>>()?;
    let mut stmt = db.prepare(
        "SELECT Id, json_extract(CAST(Data AS TEXT), '$.Localization.Title.Text') FROM cards",
    )?;
    let mut names = BTreeMap::new();
    let mut current = HashSet::new();
    let mut rows = stmt.query([])?;
    while let Some(row) = rows.next()? {
        let id: String = row.get(0)?;
        if let Ok(Some(title)) = row.get::<_, Option<String>>(1) {
            let title = strip(&title);
            if !title.is_empty() {
                names.insert(id.clone(), title.to_owned());
            }
        }
        current.insert(id);
    }
    Ok(Rows {
        cards,
        names,
        current,
    })
}

pub fn extract(inputs: &Inputs<'_>) -> Result<Summary> {
    let paths = addressables::paths(&std::fs::read(
        inputs.data_dir.join("StreamingAssets/aa/catalog.bin"),
    )?)?;
    let bundle_paths = bundles(inputs.data_dir)?;
    let mut env = Env::new();
    let mut files = Vec::new();
    for path in &bundle_paths {
        files.extend(env.add(path)?);
    }
    let mut objects = HashMap::new();
    containers(&env, &files, &mut objects)?;
    std::fs::create_dir_all(inputs.out_dir)?;
    let exporter = (|| -> Result<MaterialExporter> {
        let mut extra = Vec::new();
        for name in ["shaders_cards", "textures_fx"] {
            let bundle = bundle_paths[0].with_file_name(format!("{name}_assets_all.bundle"));
            if !bundle.is_file() {
                bail!("{name}_assets_all.bundle not found");
            }
            extra.extend(env.add(&bundle)?);
        }
        containers(&env, &extra, &mut objects)?;
        let (file, id, _) = objects
            .iter()
            .find(|(p, _)| p.ends_with("/PremiumCard.shader"))
            .map(|(_, o)| o.clone())
            .ok_or_else(|| anyhow!("PremiumCard.shader not found"))?;
        MaterialExporter::new(&file, id, inputs.out_dir)
    })()
    .map_err(|error| eprintln!("Enchantment FX unavailable: {error:#}"))
    .ok();

    let strip_suffix = |p: &str| match p.rfind('[') {
        Some(i) if p.ends_with(']') => p[..i].to_owned(),
        _ => p.to_owned(),
    };
    let asset = |key: &str| {
        paths
            .get(key)
            .and_then(|p| objects.get(&strip_suffix(p)))
            .cloned()
    };

    let previous_text = inputs
        .previous
        .filter(|p| p.exists())
        .map(std::fs::read_to_string)
        .transpose()?;
    let previous: Option<Json> = previous_text
        .as_deref()
        .map(serde_json::from_str)
        .transpose()?;
    let db =
        Connection::open_with_flags(inputs.game_data, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .with_context(|| inputs.game_data.display().to_string())?;
    let empty = Map::new();
    let text = {
        let prior = match (&previous, inputs.previous) {
            (Some(p), Some(path)) => p["text"].as_object().map(|text| Previous {
                dir: path.parent().unwrap_or(Path::new(".")),
                text,
            }),
            _ => None,
        };
        write_card_text(&db, inputs.translations, prior.as_ref(), inputs.out_dir)?
    };
    let rows = game_cards(&db)?;

    let mut files_by_art: HashMap<String, String> = HashMap::new();
    let mut fx_by_art: HashMap<String, String> = HashMap::new();
    let mut fx_by_material: HashMap<(String, i64), String> = HashMap::new();
    // Reading bundles stays on this thread; decoding, resizing and encoding run on workers.
    struct Job {
        art_key: String,
        raw: Raw,
        height: usize,
        alpha: bool,
    }
    let workers = inputs.threads.max(1);
    let (jobs, queue) = std::sync::mpsc::sync_channel::<Job>(workers * 2);
    let queue = std::sync::Mutex::new(queue);
    let (done_tx, done_rx) = std::sync::mpsc::channel::<(String, Result<String>)>();
    let out_dir = inputs.out_dir;
    let encode = |job: &Job| -> Result<String> {
        let data = art_webp(&job.raw.decode()?, job.height, job.alpha)?;
        let name = format!("{}.webp", &hex(&Sha256::digest(&data))[..20]);
        std::fs::write(out_dir.join(&name), &data)?;
        Ok(name)
    };

    // Progress counts each card read, then each item's Enchantment FX.
    let fx_steps = if exporter.is_some() {
        rows.cards
            .iter()
            .filter(|(_, kind, _)| kind == "TCardItem")
            .count()
    } else {
        0
    };
    let steps = rows.cards.len() + fx_steps;
    let report = |done: usize| {
        if let Some(progress) = inputs.progress {
            progress(done.min(steps), steps);
        }
    };
    let mut cards: BTreeMap<String, String> = BTreeMap::new();
    let (mut items, mut skills) = ((0usize, 0usize), (0usize, 0usize));
    // An art key is read once; every card that names it shares the result.
    let mut queued: HashSet<String> = HashSet::new();
    // An item's material, for its Enchantment FX once its art is written.
    let mut materials_by_art: Vec<(String, Rc<Serialized>, i64, crate::unity::Value)> = Vec::new();
    let mut card_arts: Vec<(&String, &String, &String)> = Vec::new();
    std::thread::scope(|scope| -> Result<()> {
        for _ in 0..workers {
            let (queue, done_tx, encode) = (&queue, done_tx.clone(), &encode);
            scope.spawn(move || loop {
                let next = queue.lock().ok().and_then(|q| q.recv().ok());
                let Some(job) = next else { break };
                let result = encode(&job);
                if done_tx.send((job.art_key, result)).is_err() {
                    break;
                }
            });
        }
        drop(done_tx);
        for (done, (template_id, kind, art_key)) in rows.cards.iter().enumerate() {
            report(done);
            let Some(art_key) = art_key.as_ref().filter(|k| !k.is_empty()) else {
                continue;
            };
            if kind == "TCardItem" {
                items.1 += 1;
            } else {
                skills.1 += 1;
            }
            card_arts.push((template_id, kind, art_key));
            if queued.contains(art_key.as_str()) {
                continue;
            }
            let attempt = (|| -> Result<Option<Job>> {
                let Some((file, pid, class)) = asset(art_key) else {
                    return Ok(None);
                };
                if kind == "TCardItem" {
                    if class != 114 {
                        return Ok(None);
                    }
                    let card_data = file.read(pid)?;
                    let guid = card_data
                        .get("cardMaterialReference")
                        .and_then(|r| r.get("m_AssetGUID"))
                        .and_then(|g| g.str().ok())
                        .unwrap_or("");
                    let Some((material_file, material_id, 21)) = asset(guid) else {
                        return Ok(None);
                    };
                    let material = material_file.read(material_id)?;
                    for pair in material.at("m_SavedProperties.m_TexEnvs")?.list()? {
                        let main = pair.at("second.m_Texture")?;
                        if pair.at("first")?.str()? != "_MainTex"
                            || main.at("m_PathID")?.int()? == 0
                        {
                            continue;
                        }
                        let (tf, tid) = env.deref(&material_file, main)?;
                        let raw = texture_raw(&env, &tf, tid, true)?;
                        materials_by_art.push((
                            art_key.clone(),
                            material_file.clone(),
                            material_id,
                            material.clone(),
                        ));
                        return Ok(Some(Job {
                            art_key: art_key.clone(),
                            raw,
                            height: ITEM_HEIGHT,
                            alpha: false,
                        }));
                    }
                    Ok(None)
                } else {
                    let raw = match class {
                        213 => sprite_raw(&env, &file, pid)?,
                        28 => texture_raw(&env, &file, pid, true)?,
                        _ => return Ok(None),
                    };
                    Ok(Some(Job {
                        art_key: art_key.clone(),
                        raw,
                        height: SKILL_HEIGHT,
                        alpha: true,
                    }))
                }
            })();
            match attempt {
                Ok(Some(job)) => {
                    queued.insert(art_key.clone());
                    jobs.send(job)
                        .map_err(|_| anyhow!("the art encoders stopped"))?;
                }
                Ok(None) => {}
                // One broken asset must not stop the set.
                Err(error) => eprintln!("skip {kind} {template_id} {art_key}: {error:#}"),
            }
        }
        drop(jobs);
        Ok(())
    })?;
    for (art_key, result) in done_rx {
        match result {
            Ok(name) => {
                files_by_art.insert(art_key, name);
            }
            Err(error) => eprintln!("skip art {art_key}: {error:#}"),
        }
    }
    for (template_id, kind, art_key) in card_arts {
        if let Some(name) = files_by_art.get(art_key.as_str()) {
            cards.insert(template_id.clone(), name.clone());
            if kind == "TCardItem" {
                items.0 += 1;
            } else {
                skills.0 += 1;
            }
        }
    }
    if let Some(exporter) = &exporter {
        // The first art written for a material names it in that material's descriptor.
        let mut identities: Vec<(String, (String, i64))> = Vec::new();
        let (jobs, queue) =
            std::sync::mpsc::sync_channel::<((String, i64), Prepared, String)>(workers * 2);
        let queue = std::sync::Mutex::new(queue);
        let (done_tx, done_rx) = std::sync::mpsc::channel::<((String, i64), Result<String>)>();
        std::thread::scope(|scope| {
            for _ in 0..workers {
                let (queue, done_tx, mut describer) =
                    (&queue, done_tx.clone(), exporter.describer());
                scope.spawn(move || loop {
                    let next = queue.lock().ok().and_then(|q| q.recv().ok());
                    let Some((identity, prepared, art)) = next else {
                        break;
                    };
                    let result = describer.describe_prepared(&prepared, &art, ITEM_HEIGHT);
                    if done_tx.send((identity, result)).is_err() {
                        break;
                    }
                });
            }
            drop(done_tx);
            let mut dispatched = HashSet::new();
            for (step, (art_key, material_file, material_id, material)) in
                materials_by_art.iter().enumerate()
            {
                report(rows.cards.len() + step);
                let Some(art) = files_by_art.get(art_key) else {
                    continue;
                };
                let identity = (material_file.name.clone(), *material_id);
                identities.push((art_key.clone(), identity.clone()));
                if !dispatched.insert(identity.clone()) {
                    continue;
                }
                match exporter.prepare(&env, material_file, material) {
                    Ok(prepared) => {
                        if jobs.send((identity, prepared, art.clone())).is_err() {
                            break;
                        }
                    }
                    Err(error) => eprintln!("skip FX {art_key}: {error:#}"),
                }
            }
            drop(jobs);
        });
        for (identity, result) in done_rx {
            match result {
                Ok(file) => {
                    fx_by_material.insert(identity, file);
                }
                Err(error) => eprintln!("skip FX {}#{}: {error:#}", identity.0, identity.1),
            }
        }
        for (art_key, identity) in identities {
            if let Some(file) = fx_by_material.get(&identity) {
                fx_by_art.insert(art_key, file.clone());
            }
        }
    }

    report(steps);
    let fx: Vec<(String, String)> = rows
        .cards
        .iter()
        .filter(|(_, kind, _)| kind == "TCardItem")
        .filter_map(|(id, _, art)| Some((id.clone(), fx_by_art.get(art.as_deref()?)?.clone())))
        .collect();
    let mut summary = Summary {
        items,
        skills,
        files: cards.values().collect::<HashSet<_>>().len(),
        names: rows.names.len(),
        fx_items: fx.len(),
        ..Summary::default()
    };
    let mut materials: BTreeMap<&str, usize> = BTreeMap::new();
    for (_, file) in &fx {
        *materials.entry(file).or_default() += 1;
    }
    let mut textures = HashSet::new();
    for (file, count) in &materials {
        let descriptor: Json = serde_json::from_slice(&std::fs::read(inputs.out_dir.join(file))?)?;
        *summary
            .fx_sizes
            .entry(descriptor["size"].as_str().unwrap_or_default().to_owned())
            .or_default() += count;
        for (slot, binding) in descriptor["textures"].as_object().unwrap_or(&empty) {
            if slot != "_MainTex" {
                if let Some(f) = binding["file"].as_str() {
                    textures.insert(f.to_owned());
                }
            }
        }
        if descriptor["textures"]["_EnchantmentMask"]
            .get("file")
            .is_some()
        {
            summary.fx_masks += count;
        }
    }
    summary.fx_materials = materials.len();
    summary.fx_textures = textures.len();
    for name in textures
        .iter()
        .map(String::as_str)
        .chain(materials.keys().copied())
    {
        summary.fx_bytes += std::fs::metadata(inputs.out_dir.join(name))?.len();
    }
    if inputs.publication
        && ((items.0 as f64) < items.1 as f64 * MIN_ITEM_SHARE
            || (skills.0 as f64) < skills.1 as f64 * MIN_SKILL_SHARE)
    {
        bail!("too few cards resolved: {}", summary.json());
    }
    let game = [
        inputs.buildid.as_ref(),
        inputs.gamedata_etag.as_ref(),
        inputs.zh_etag.as_ref(),
    ];
    let index = card_index(
        &cards,
        &rows,
        previous.as_ref(),
        game,
        &text,
        (!fx.is_empty()).then_some(&fx),
    );
    // Readers may serve this directory while it is written; they see the old index or the new one.
    let staged = inputs.out_dir.join("index.json.partial");
    std::fs::write(&staged, index)?;
    std::fs::rename(staged, inputs.out_dir.join("index.json"))?;
    // All 1,428 resolved item illustrations in build 26592248 have a material and mask.
    if inputs.require_fx
        && (fx.is_empty() || (summary.fx_masks as f64) < items.0 as f64 * MIN_ITEM_SHARE)
    {
        bail!(
            "too few Enchantment FX masks for publication: {}",
            summary.json()
        );
    }
    Ok(summary)
}

/// The published index. A TemplateId GameData no longer has keeps its earlier art and name,
/// so Runs from older patches still resolve. Written like Python's `json.dump(...,
/// separators=(",", ":"))`: insertion order, ASCII only.
fn card_index(
    cards: &BTreeMap<String, String>,
    rows: &Rows,
    previous: Option<&Json>,
    game: [Option<&String>; 3],
    text: &BTreeMap<String, String>,
    fx: Option<&Vec<(String, String)>>,
) -> String {
    let mut cards = cards.clone();
    let mut names = rows.names.clone();
    let empty = Map::new();
    let prior = |key: &str| previous.and_then(|p| p[key].as_object()).unwrap_or(&empty);
    for (mapping, key) in [(&mut cards, "cards"), (&mut names, "names")] {
        for (id, value) in prior(key) {
            if !rows.current.contains(id) {
                if let Some(v) = value.as_str() {
                    mapping.entry(id.clone()).or_insert_with(|| v.to_owned());
                }
            }
        }
    }
    let object = |entries: &mut dyn Iterator<Item = (&str, String)>| {
        let mut out = String::from("{");
        for (i, (k, v)) in entries.enumerate() {
            if i > 0 {
                out.push(',');
            }
            let _ = write!(out, "{}:{v}", json_string(k, true));
        }
        out.push('}');
        out
    };
    let string_or_null =
        |v: Option<&String>| v.map_or_else(|| "null".to_owned(), |s| json_string(s, true));
    let mut parts: Vec<(&str, String)> = Vec::new();
    if let Some(fx) = fx {
        let retained: Vec<(String, String)> = prior("fx")
            .iter()
            .filter(|(k, _)| !rows.current.contains(*k))
            .filter_map(|(k, v)| Some((k.clone(), v.as_str()?.to_owned())))
            .collect();
        parts.push((
            "fx",
            object(
                &mut retained
                    .iter()
                    .chain(fx.iter())
                    .map(|(k, v)| (k.as_str(), json_string(v, true))),
            ),
        ));
    }
    parts.push(("version", "3".into()));
    parts.push((
        "text",
        object(&mut text.iter().map(|(k, v)| (k.as_str(), json_string(v, true)))),
    ));
    parts.push((
        "game",
        object(
            &mut [
                ("buildid", string_or_null(game[0])),
                ("gameData", string_or_null(game[1])),
                ("zh", string_or_null(game[2])),
            ]
            .into_iter(),
        ),
    ));
    parts.push((
        "cards",
        object(
            &mut cards
                .iter()
                .map(|(k, v)| (k.as_str(), json_string(v, true))),
        ),
    ));
    parts.push((
        "names",
        object(
            &mut names
                .iter()
                .map(|(k, v)| (k.as_str(), json_string(v, true))),
        ),
    ));
    object(&mut parts.into_iter())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn game_data(cards: &[(&str, Json)]) -> Connection {
        let db = Connection::open_in_memory().unwrap();
        db.execute_batch("CREATE TABLE cards (Id TEXT, Data TEXT)")
            .unwrap();
        for (id, data) in cards {
            db.execute("INSERT INTO cards VALUES (?1, ?2)", (id, data.to_string()))
                .unwrap();
        }
        db
    }

    fn card(kind: &str, art: &str, title: Option<&str>) -> Json {
        let mut data = json!({ "$type": kind, "ArtKey": art });
        if let Some(t) = title {
            data["Localization"] = json!({ "Title": { "Text": t } });
        }
        data
    }

    #[test]
    fn every_card_is_named_and_only_items_and_skills_carry_art() {
        let db = game_data(&[
            ("aaaa", card("TCardItem", "art-a", Some("Mantis Shrimp"))),
            ("bbbb", card("TCardSkill", "art-b", Some(" Keen Eye "))),
            ("cccc", card("TCardEncounterEvent", "art-c", Some("Ande"))),
            ("dddd", card("TCardEncounterStep", "art-d", Some(""))),
            ("eeee", card("TCardPlayerEffect", "art-e", None)),
        ]);
        let rows = game_cards(&db).unwrap();
        let mut ids: Vec<&str> = rows.cards.iter().map(|r| r.0.as_str()).collect();
        ids.sort_unstable();
        assert_eq!(ids, ["aaaa", "bbbb"]);
        assert_eq!(
            rows.names.into_iter().collect::<Vec<_>>(),
            [
                ("aaaa", "Mantis Shrimp"),
                ("bbbb", "Keen Eye"),
                ("cccc", "Ande")
            ]
            .map(|(a, b)| (a.to_owned(), b.to_owned()))
        );
    }

    fn rows(names: &[(&str, &str)], current: &[&str]) -> Rows {
        Rows {
            cards: vec![],
            names: names
                .iter()
                .map(|(a, b)| ((*a).to_owned(), (*b).to_owned()))
                .collect(),
            current: current.iter().map(|s| (*s).to_owned()).collect(),
        }
    }

    #[test]
    fn the_index_keeps_art_and_names_of_cards_gamedata_dropped() {
        let previous = json!({
            "cards": { "old": "1.webp", "aaaa": "stale.webp" },
            "names": { "old": "Old Card", "aaaa": "Stale" },
        });
        let cards = BTreeMap::from([("aaaa".to_owned(), "2.webp".to_owned())]);
        let text = BTreeMap::from([
            ("en".to_owned(), "en.json".to_owned()),
            ("zh".to_owned(), "zh.json".to_owned()),
        ]);
        let (b, g, z) = ("7".to_owned(), "e1".to_owned(), "zh1".to_owned());
        let index = card_index(
            &cards,
            &rows(
                &[("aaaa", "Mantis Shrimp"), ("cccc", "Ande")],
                &["aaaa", "cccc"],
            ),
            Some(&previous),
            [Some(&b), Some(&g), Some(&z)],
            &text,
            None,
        );
        assert_eq!(
            index,
            r#"{"version":3,"text":{"en":"en.json","zh":"zh.json"},"game":{"buildid":"7","gameData":"e1","zh":"zh1"},"cards":{"aaaa":"2.webp","old":"1.webp"},"names":{"aaaa":"Mantis Shrimp","cccc":"Ande","old":"Old Card"}}"#
        );
    }

    #[test]
    fn retired_materials_survive_and_current_skills_stay_plain() {
        let previous = json!({ "fx": { "retired": "old", "plain-skill": "stale" } });
        let fx = vec![("item".to_owned(), "new".to_owned())];
        let index = card_index(
            &BTreeMap::new(),
            &rows(&[], &["item", "plain-skill"]),
            Some(&previous),
            [None, None, None],
            &BTreeMap::new(),
            Some(&fx),
        );
        let parsed: Json = serde_json::from_str(&index).unwrap();
        assert_eq!(parsed["fx"], json!({ "item": "new", "retired": "old" }));
    }
}
