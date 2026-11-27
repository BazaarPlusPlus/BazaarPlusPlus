//! Card Text: each item's and skill's localized definition, per locale (ADR 0021).
//!
//! The output is byte-for-byte what the publication pipeline has always written, so a
//! file's digest names the same content wherever it was produced.

use crate::json::{canonical, hex, ordered_object, Num};
use anyhow::{anyhow, bail, Context, Result};
use md5::{Digest, Md5};
use rusqlite::Connection;
use serde_json::{json, Map, Value};
use std::collections::{BTreeMap, BTreeSet, HashMap, HashSet};
use std::path::Path;

pub const TIER_ORDER: [&str; 5] = ["Bronze", "Silver", "Gold", "Diamond", "Legendary"];
pub const LOCALES: [&str; 2] = ["en", "zh"];

const DURATIONS: [&str; 7] = [
    "ChargeAmount",
    "HasteAmount",
    "SlowAmount",
    "FreezeAmount",
    "FlatCooldownReduction",
    "CooldownMax",
    "EnragedDurationMax",
];

fn attribute_keyword(key: &str) -> Option<&'static str> {
    Some(match key {
        "CritChance" | "CritChanceAmount" => "Crit Chance",
        "FlatCooldownReduction" | "CooldownMax" => "Cooldown",
        "EnragedDurationMax" => "Enraged",
        "ShieldApplyAmount" => "Shield",
        "RegenApplyAmount" => "Regen",
        "PoisonApplyAmount" => "Poison",
        "BurnApplyAmount" => "Burn",
        "Multicast" => "Multicast",
        _ => return None,
    })
}

type Attributes = Map<String, Value>;

fn truthy(v: Option<&Value>) -> bool {
    match v {
        None | Some(Value::Null) => false,
        Some(Value::Bool(b)) => *b,
        Some(Value::Number(n)) => n.as_f64() != Some(0.0),
        Some(Value::String(s)) => !s.is_empty(),
        Some(Value::Array(a)) => !a.is_empty(),
        Some(Value::Object(o)) => !o.is_empty(),
    }
}

fn attribute(attributes: &Attributes, key: Option<&str>, blocked: &HashSet<String>) -> Option<Num> {
    let key = key?;
    if blocked.contains(key) {
        return None;
    }
    attributes.get(key).and_then(Num::from_value)
}

fn static_value(value: &Value, attributes: &Attributes, blocked: &HashSet<String>) -> Option<Num> {
    let kind = value.get("$type").and_then(Value::as_str);
    if kind == Some("TFixedValue") {
        return value.get("Value").and_then(Num::from_value);
    }
    if kind != Some("TReferenceValueCardAttribute") {
        return None;
    }
    let target = &value["Target"];
    let key = value["AttributeType"].as_str()?;
    if target["$type"].as_str() != Some("TTargetCardSelf")
        || truthy(target.get("Conditions"))
        || blocked.contains(key)
    {
        return None;
    }
    let mut number = attributes.get(key).and_then(Num::from_value);
    let modifier = value.get("Modifier");
    if let (Some(n), true) = (number, truthy(modifier)) {
        let modifier = modifier?;
        let factor = static_value(&modifier["Value"], attributes, blocked)?;
        if modifier["ModifyMode"].as_str() != Some("Multiply") {
            return None;
        }
        let product = n.times(factor);
        if modifier["ShouldRound"].as_bool() == Some(true) && !product.is_integral() {
            return None;
        }
        number = Some(product);
    }
    number
}

struct Token {
    number: Option<Num>,
    key: Option<String>,
    unit: &'static str,
}

const NOTHING: Token = Token {
    number: None,
    key: None,
    unit: "number",
};

fn token_value(
    token: &str,
    card: &Value,
    source: &Value,
    from_card: bool,
    tier: &Value,
    attributes: &Attributes,
    blocked: &HashSet<String>,
) -> Token {
    let parts: Vec<&str> = token.split('.').collect();
    if parts.len() == 1 {
        let key = match token {
            "custom_0" => Some("Custom_0"),
            "TempoCost" => Some("TempoCost"),
            _ => None,
        };
        return Token {
            number: attribute(attributes, key, blocked),
            key: key.map(str::to_owned),
            unit: "number",
        };
    }
    let (prefix, effect_id, accessor) = (parts[0], parts[1], &parts[2..]);
    let pool = match prefix {
        "ability" => Some("Abilities"),
        "aura" => Some("Auras"),
        _ => None,
    };
    fn lookup<'v>(owner: &'v Value, pool: Option<&str>, id: &str) -> Option<&'v Value> {
        pool.and_then(|p| owner.get(p))
            .filter(|v| truthy(Some(v)))
            .and_then(|v| v.get(id))
            .filter(|v| truthy(Some(v)))
    }
    let Some(effect) = lookup(source, pool, effect_id).or_else(|| lookup(card, pool, effect_id))
    else {
        return NOTHING;
    };
    if token.contains("??") {
        return NOTHING;
    }
    let ids = if prefix == "ability" {
        "AbilityIds"
    } else {
        "AuraIds"
    };
    let listed = |t: &Value| {
        t[ids]
            .as_array()
            .is_some_and(|a| a.iter().any(|v| v.as_str() == Some(effect_id)))
    };
    // A display-only effect can be absent from every tier's executable effects.
    if from_card
        && !listed(tier)
        && card["Tiers"]
            .as_object()
            .is_some_and(|tiers| tiers.values().any(listed))
    {
        return NOTHING;
    }
    let action = &effect["Action"];
    let kind = action["$type"].as_str().unwrap_or_default();
    let verb = strip_action(kind);
    let mut key = Some(
        match action
            .get("AttributeType")
            .filter(|v| truthy(Some(v)))
            .and_then(Value::as_str)
        {
            Some(k) => k.to_owned(),
            None => format!("{verb}Amount"),
        },
    );
    let mut value = action.get("Value").cloned();
    if accessor == ["mod"] {
        value = value
            .as_ref()
            .and_then(|v| v.get("Modifier"))
            .filter(|v| truthy(Some(v)))
            .and_then(|m| m.get("Value"))
            .cloned();
        key = None;
    } else if accessor == ["target"] || accessor == ["targets"] {
        value = action.get("TargetCount").cloned();
        key = Some(format!("{verb}Targets"));
    } else if !accessor.is_empty() {
        return NOTHING;
    } else if kind == "TActionGameSpawnCards" {
        value = action
            .get("SpawnContext")
            .filter(|v| truthy(Some(v)))
            .and_then(|c| c.get("Limit"))
            .cloned();
        key = None;
    }
    let mut number = if truthy(value.as_ref()) {
        static_value(value.as_ref().unwrap_or(&Value::Null), attributes, blocked)
    } else {
        attribute(attributes, key.as_deref(), blocked)
    };
    let mut unit = "number";
    let operation = action.get("Operation").and_then(Value::as_str);
    if key.as_deref().is_some_and(|k| DURATIONS.contains(&k))
        && !matches!(operation, Some("Multiply" | "Percent"))
    {
        unit = "seconds";
        number = number.map(|n| Num::F(n.as_f64() / 1000.0));
    }
    Token {
        number: number.map(Num::normalized),
        key,
        unit,
    }
}

/// `re.sub("^T(?:Aura)?Action(?:Player|Card)", "", kind)`.
fn strip_action(kind: &str) -> String {
    let Some(rest) = kind.strip_prefix('T') else {
        return kind.to_owned();
    };
    let rest = rest.strip_prefix("Aura").unwrap_or(rest);
    let Some(rest) = rest.strip_prefix("Action") else {
        return kind.to_owned();
    };
    for p in ["Player", "Card"] {
        if let Some(r) = rest.strip_prefix(p) {
            return r.to_owned();
        }
    }
    kind.to_owned()
}

/// Python's `\w` for `str` patterns: `str.isalnum()` or `_`.
fn word(c: char) -> bool {
    c == '_' || c.is_alphanumeric()
}

pub struct Language {
    locale: &'static str,
    translations: HashMap<String, String>,
    /// Keyword entries by word, sorted.
    pub keywords: BTreeMap<String, Value>,
    labels: HashMap<String, String>,
    /// Labels ordered longest-first so prefix matching picks the most specific keyword.
    alternatives: Vec<String>,
}

impl Language {
    pub fn new(
        locale: &'static str,
        keywords: &[(String, Value)],
        translations: HashMap<String, String>,
    ) -> Self {
        let localize = |text: Option<&str>| -> Value {
            match text {
                None => Value::Null,
                Some("") => json!(""),
                Some(t) if locale == "en" => json!(t),
                Some(t) => {
                    let hash = hex(&Md5::digest(t.as_bytes()));
                    translations.get(&hash).map_or(Value::Null, |s| json!(s))
                }
            }
        };
        let mut sorted: Vec<&(String, Value)> = keywords.iter().collect();
        sorted.sort_by(|a, b| a.0.cmp(&b.0));
        let mut out = BTreeMap::new();
        let mut order = Vec::new();
        for (word, entry) in sorted {
            if word == "Invalid" {
                continue;
            }
            out.insert(
                word.clone(),
                json!({
                    "label": localize(Some(word)),
                    "description": localize(entry.get("Keyword").and_then(Value::as_str)),
                    "icon": entry.get("Icon").cloned().unwrap_or(Value::Null),
                }),
            );
            order.push(word.clone());
        }
        let mut labels = HashMap::new();
        let mut label_order = Vec::new();
        for word in &order {
            if let Some(label) = out[word]["label"].as_str().filter(|l| !l.is_empty()) {
                if !labels.contains_key(label) {
                    labels.insert(label.to_owned(), word.clone());
                    label_order.push(label.to_owned());
                }
            }
        }
        // Python's sort by length, reversed, keeps equal lengths in insertion order.
        let mut alternatives = label_order;
        alternatives.sort_by_key(|l| std::cmp::Reverse(l.chars().count()));
        Self {
            locale,
            translations,
            keywords: out,
            labels,
            alternatives,
        }
    }

    fn text(&self, content: &Value) -> Option<String> {
        if self.locale == "en" {
            content["Text"].as_str().map(str::to_owned)
        } else {
            content["Key"]
                .as_str()
                .and_then(|k| self.translations.get(k))
                .cloned()
        }
    }

    fn literals(&self, text: &str, out: &mut Vec<Value>) {
        let english = self.locale == "en";
        let mut cursor = 0;
        let mut i = 0;
        while i < text.len() {
            let before_ok = !english || text[..i].chars().next_back().is_none_or(|c| !word(c));
            let mut matched = None;
            if before_ok {
                for alt in &self.alternatives {
                    if text[i..].starts_with(alt.as_str()) {
                        let end = i + alt.len();
                        if !english || text[end..].chars().next().is_none_or(|c| !word(c)) {
                            matched = Some(alt);
                            break;
                        }
                    }
                }
            }
            if let Some(alt) = matched {
                if cursor < i {
                    out.push(json!({ "text": &text[cursor..i] }));
                }
                out.push(json!({ "keyword": self.labels[alt], "label": alt }));
                i += alt.len();
                cursor = i;
            } else {
                i += text[i..].chars().next().map_or(1, char::len_utf8);
            }
        }
        if cursor < text.len() {
            out.push(json!({ "text": &text[cursor..] }));
        }
    }
}

/// `\{([^{}]+)\}` matches: (start, end, inner).
fn tokens(text: &str) -> Vec<(usize, usize, &str)> {
    let mut out = Vec::new();
    let bytes = text.as_bytes();
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'{' {
            let mut j = i + 1;
            while j < bytes.len() && bytes[j] != b'{' && bytes[j] != b'}' {
                j += 1;
            }
            if j < bytes.len() && bytes[j] == b'}' && j > i + 1 {
                out.push((i, j + 1, &text[i + 1..j]));
                i = j + 1;
                continue;
            }
        }
        i += 1;
    }
    out
}

struct TierContext {
    tier: &'static str,
    attributes: Attributes,
    blocked: HashSet<String>,
}

fn tooltip_line(
    index: usize,
    content: &Value,
    contexts: &[&TierContext],
    card: &Value,
    source: &Value,
    from_card: bool,
    language: &Language,
) -> Value {
    let active: Vec<&str> = contexts.iter().map(|c| c.tier).collect();
    let mut line = Map::new();
    line.insert("id".into(), json!(index));
    line.insert("tiers".into(), json!(active));
    line.insert("available".into(), json!(false));
    line.insert("resolved".into(), json!("none"));
    let english = content["Text"].as_str().unwrap_or_default();
    let text = language.text(content);
    let Some(text) = text.filter(|t| !t.is_empty()) else {
        line.insert("segments".into(), json!([]));
        return Value::Object(line);
    };
    let found = tokens(&text);
    let mut ours: Vec<&str> = found.iter().map(|t| t.2).collect();
    let mut theirs: Vec<&str> = tokens(english).iter().map(|t| t.2).collect();
    ours.sort_unstable();
    theirs.sort_unstable();
    if ours != theirs {
        line.insert("segments".into(), json!([]));
        return Value::Object(line);
    }
    line.insert("available".into(), json!(true));
    let mut segments = Vec::new();
    let (mut known, mut total, mut cursor) = (0usize, 0usize, 0usize);
    for (start, end, inner) in found {
        language.literals(&text[cursor..start], &mut segments);
        let whole = &text[start..end];
        let mut segment = Map::new();
        segment.insert("token".into(), json!(whole));
        let mut values = Map::new();
        for ctx in contexts {
            let token = token_value(
                inner,
                card,
                source,
                from_card,
                &card["Tiers"][ctx.tier],
                &ctx.attributes,
                &ctx.blocked,
            );
            let Some(number) = token.number else { continue };
            values.insert(ctx.tier.to_owned(), number.to_value());
            let percent = english.contains(&format!("{whole}%"));
            segment.insert(
                "unit".into(),
                json!(if percent { "percent" } else { token.unit }),
            );
            let key = token.key.as_deref().unwrap_or_default();
            let keyword = attribute_keyword(key).map_or_else(
                || key.strip_suffix("Amount").unwrap_or(key).to_owned(),
                str::to_owned,
            );
            if language.keywords.contains_key(&keyword) {
                segment.insert("keyword".into(), json!(keyword));
            }
        }
        known += values.len();
        total += active.len();
        if !values.is_empty() {
            segment.insert("tiers".into(), Value::Object(values));
        }
        segments.push(Value::Object(segment));
        cursor = end;
    }
    language.literals(&text[cursor..], &mut segments);
    line.insert(
        "resolved".into(),
        json!(if known == total {
            "full"
        } else if known > 0 {
            "partial"
        } else {
            "none"
        }),
    );
    line.insert("segments".into(), Value::Array(segments));
    Value::Object(line)
}

fn ids_contain(list: &Value, id: &Value) -> bool {
    list.as_array().is_some_and(|a| a.contains(id))
}

pub fn card_definition(card: &Value, language: &Language) -> Result<Value> {
    let card_tiers = card["Tiers"]
        .as_object()
        .ok_or_else(|| anyhow!("card without Tiers"))?;
    let mut attributes = Map::new();
    let mut tiers: Vec<(&'static str, Attributes)> = Vec::new();
    for tier in TIER_ORDER {
        if let Some(t) = card_tiers.get(tier) {
            if let Some(a) = t["Attributes"].as_object() {
                for (k, v) in a {
                    attributes.insert(k.clone(), v.clone());
                }
            }
            tiers.push((tier, attributes.clone()));
        }
    }
    let mut definition = Map::new();
    definition.insert(
        "title".into(),
        language
            .text(&card["Localization"]["Title"])
            .map_or(Value::Null, Value::String),
    );
    for (out, key) in [
        ("type", "Type"),
        ("size", "Size"),
        ("heroes", "Heroes"),
        ("tags", "Tags"),
        ("startingTier", "StartingTier"),
    ] {
        definition.insert(out.into(), card.get(key).cloned().unwrap_or(Value::Null));
    }
    definition.insert(
        "attributes".into(),
        Value::Object(
            tiers
                .iter()
                .map(|(t, a)| ((*t).to_owned(), Value::Object(a.clone())))
                .collect(),
        ),
    );
    let empty = Map::new();
    let auras_of_card = card["Auras"].as_object().unwrap_or(&empty);
    let mut sources: Vec<(Option<&String>, &Value)> = vec![(None, card)];
    if let Some(e) = card.get("Enchantments").and_then(Value::as_object) {
        sources.extend(e.iter().map(|(k, v)| (Some(k), v)));
    }
    let mut enchantments = Map::new();
    for (enchant, source) in sources {
        let mut contexts = Vec::new();
        for (tier, attrs) in &tiers {
            let aura_ids = &card_tiers[*tier]["AuraIds"];
            let mut auras: Vec<&Value> = auras_of_card
                .iter()
                .filter(|(k, _)| ids_contain(aura_ids, &json!(k)))
                .map(|(_, v)| v)
                .collect();
            if enchant.is_some() {
                if let Some(extra) = source["Auras"].as_object() {
                    auras.extend(extra.values());
                }
            }
            let blocked: HashSet<String> = auras
                .iter()
                .filter_map(|aura| {
                    let action = &aura["Action"];
                    let key = action
                        .get("AttributeType")
                        .filter(|v| truthy(Some(v)))?
                        .as_str()?;
                    let target = action.get("Target").filter(|v| truthy(Some(v)));
                    (target.and_then(|t| t["$type"].as_str()) == Some("TTargetCardSelf"))
                        .then(|| key.to_owned())
                })
                .collect();
            let mut merged = attrs.clone();
            if let Some(extra) = source
                .get("Attributes")
                .filter(|v| truthy(Some(v)))
                .and_then(Value::as_object)
            {
                for (k, v) in extra {
                    merged.insert(k.clone(), v.clone());
                }
            }
            contexts.push(TierContext {
                tier,
                attributes: merged,
                blocked,
            });
        }
        let mut lines = Vec::new();
        let tooltips = source
            .get("Localization")
            .filter(|v| truthy(Some(v)))
            .and_then(|l| l.get("Tooltips"))
            .and_then(Value::as_array);
        for (index, tooltip) in tooltips.into_iter().flatten().enumerate() {
            let active: Vec<&TierContext> = contexts
                .iter()
                .filter(|c| {
                    enchant.is_some()
                        || ids_contain(&card_tiers[c.tier]["TooltipIds"], &json!(index))
                })
                .collect();
            let content = &tooltip["Content"];
            if !active.is_empty() && truthy(content.get("Text")) {
                lines.push(tooltip_line(
                    index,
                    content,
                    &active,
                    card,
                    source,
                    enchant.is_none(),
                    language,
                ));
            }
        }
        match enchant {
            Some(name) => {
                enchantments.insert(
                    name.clone(),
                    json!({ "tags": source["Tags"], "lines": lines }),
                );
            }
            None => {
                definition.insert("lines".into(), Value::Array(lines));
            }
        }
    }
    definition.insert("enchantments".into(), Value::Object(enchantments));
    Ok(Value::Object(definition))
}

/// Each distinct enchantment definition is stored once; a card names it by its position.
fn shared_enchantments(cards: &BTreeMap<String, Value>) -> (BTreeMap<String, Value>, Vec<Value>) {
    let mut table = Vec::new();
    let mut positions: HashMap<String, usize> = HashMap::new();
    let mut compact = BTreeMap::new();
    for (key, card) in cards {
        let mut refs = Map::new();
        let mut names: Vec<(&String, &Value)> = card["enchantments"]
            .as_object()
            .map(|o| o.iter().collect())
            .unwrap_or_default();
        names.sort_by(|a, b| a.0.cmp(b.0));
        for (name, effect) in names {
            let identity = canonical(effect);
            let position = *positions.entry(identity).or_insert_with(|| {
                table.push(effect.clone());
                table.len() - 1
            });
            refs.insert(name.clone(), json!(position));
        }
        let mut card = card.clone();
        card["enchantments"] = Value::Object(refs);
        compact.insert(key.clone(), card);
    }
    (compact, table)
}

pub struct GameData<'a> {
    pub db: &'a Connection,
}

fn text_column(row: &rusqlite::Row<'_>, i: usize) -> rusqlite::Result<String> {
    use rusqlite::types::ValueRef;
    Ok(match row.get_ref(i)? {
        ValueRef::Text(t) | ValueRef::Blob(t) => String::from_utf8_lossy(t).into_owned(),
        _ => String::new(),
    })
}

/// A previously written Card Text, to keep cards GameData no longer has.
pub struct Previous<'a> {
    pub dir: &'a Path,
    pub text: &'a Map<String, Value>,
}

/// Write `text-<locale>-<digest>.json` for each locale and return the file names.
pub fn write_card_text(
    db: &Connection,
    translations: &Path,
    previous: Option<&Previous<'_>>,
    output: &Path,
) -> Result<BTreeMap<String, String>> {
    let zh = Connection::open_with_flags(translations, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
        .with_context(|| translations.display().to_string())?;
    let mut table: HashMap<String, String> = HashMap::new();
    {
        let mut stmt = zh.prepare("SELECT hash, text FROM translation")?;
        let mut rows = stmt.query([])?;
        while let Some(row) = rows.next()? {
            table.insert(text_column(row, 0)?, text_column(row, 1)?);
        }
    }
    let tooltips: String = db.query_row("SELECT data FROM tooltips", [], |r| text_column(r, 0))?;
    let keywords = ordered_object(&tooltips)?;
    let mut definitions: Vec<(String, Value)> = Vec::new();
    let mut current: HashSet<String> = HashSet::new();
    {
        let mut stmt = db.prepare("SELECT id, data FROM cards")?;
        let mut rows = stmt.query([])?;
        while let Some(row) = rows.next()? {
            let id = text_column(row, 0)?;
            let data: Value = serde_json::from_str(&text_column(row, 1)?)?;
            if matches!(data["$type"].as_str(), Some("TCardItem" | "TCardSkill")) {
                definitions.push((id.clone(), data));
            }
            current.insert(id);
        }
    }
    let mut files = BTreeMap::new();
    for locale in LOCALES {
        let mut language = Language::new(
            locale,
            &keywords,
            if locale == "en" {
                HashMap::new()
            } else {
                table.clone()
            },
        );
        let mut cards = BTreeMap::new();
        for (key, card) in &definitions {
            cards.insert(
                key.clone(),
                card_definition(card, &language).with_context(|| key.clone())?,
            );
        }
        let mut tags = BTreeSet::new();
        for card in cards.values() {
            let sources = std::iter::once(card).chain(
                card["enchantments"]
                    .as_object()
                    .into_iter()
                    .flat_map(|o| o.values()),
            );
            for source in sources {
                for tag in source["tags"].as_array().into_iter().flatten() {
                    if let Some(t) = tag.as_str() {
                        tags.insert(t.to_owned());
                    }
                }
            }
        }
        // Source order: a later keyword with the same Tag replaces an earlier one.
        let mut tag_labels = Map::new();
        for (word, entry) in &keywords {
            let Some(tag) = entry["Tag"].as_str() else {
                continue;
            };
            if !tags.contains(tag) {
                continue;
            }
            let label = language
                .keywords
                .get(word)
                .ok_or_else(|| anyhow!("keyword {word} has a used Tag but no entry"))?["label"]
                .clone();
            if truthy(Some(&label)) {
                tag_labels.insert(tag.to_owned(), label);
            }
        }
        if let Some(previous) = previous {
            if let Some(filename) = previous.text.get(locale).and_then(Value::as_str) {
                merge_previous(
                    previous.dir,
                    filename,
                    locale,
                    &current,
                    &tags,
                    &mut cards,
                    &mut language,
                    &mut tag_labels,
                )?;
            }
        }
        let (cards, enchantments) = shared_enchantments(&cards);
        let payload = json!({
            "version": 2,
            "locale": locale,
            "cards": cards,
            "enchantments": enchantments,
            "keywords": language.keywords,
            "tagLabels": tag_labels,
        });
        let data = canonical(&payload);
        let digest = hex(&sha2::Sha256::digest(data.as_bytes()));
        let filename = format!("text-{locale}-{}.json", &digest[..20]);
        std::fs::write(output.join(&filename), data.as_bytes())?;
        files.insert(locale.to_owned(), filename);
    }
    Ok(files)
}

#[expect(
    clippy::too_many_arguments,
    reason = "the merge touches every part of one locale's payload"
)]
fn merge_previous(
    dir: &Path,
    filename: &str,
    locale: &str,
    current: &HashSet<String>,
    tags: &BTreeSet<String>,
    cards: &mut BTreeMap<String, Value>,
    language: &mut Language,
    tag_labels: &mut Map<String, Value>,
) -> Result<()> {
    let digest = filename
        .strip_prefix(&format!("text-{locale}-"))
        .and_then(|r| r.strip_suffix(".json"))
        .filter(|d| {
            d.len() == 20
                && d.bytes()
                    .all(|b| b.is_ascii_hexdigit() && !b.is_ascii_uppercase())
        })
        .ok_or_else(|| anyhow!("unexpected previous Card Text file: {filename}"))?;
    let data = std::fs::read(dir.join(filename))?;
    if !hex(&sha2::Sha256::digest(&data)).starts_with(digest) {
        bail!("previous Card Text digest mismatch: {filename}");
    }
    let prior: Value = serde_json::from_slice(&data)?;
    let shared = prior["enchantments"]
        .as_array()
        .cloned()
        .unwrap_or_default();
    for (key, card) in prior["cards"].as_object().into_iter().flatten() {
        if current.contains(key) {
            continue;
        }
        let mut card = card.clone();
        let mut inline = Map::new();
        for (name, effect) in card["enchantments"].as_object().into_iter().flatten() {
            let resolved = match effect.as_u64() {
                Some(i) => shared
                    .get(i as usize)
                    .cloned()
                    .ok_or_else(|| anyhow!("bad enchantment position"))?,
                None => effect.clone(),
            };
            inline.insert(name.clone(), resolved);
        }
        card["enchantments"] = Value::Object(inline);
        cards.insert(key.clone(), card);
    }
    for (key, entry) in prior["keywords"].as_object().into_iter().flatten() {
        language
            .keywords
            .entry(key.clone())
            .or_insert_with(|| entry.clone());
    }
    for (tag, label) in prior["tagLabels"].as_object().into_iter().flatten() {
        if !tags.contains(tag) {
            tag_labels.insert(tag.clone(), label.clone());
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    struct Fixture {
        raw: Value,
        keywords: Vec<(String, Value)>,
        zh: HashMap<String, String>,
    }

    fn fixture() -> Fixture {
        let text = include_str!("../tests/fixtures/card-text.json");
        let raw: Value = serde_json::from_str(text).unwrap();
        let keywords = raw["keywords"]
            .as_object()
            .unwrap()
            .iter()
            .map(|(k, v)| (k.clone(), v.clone()))
            .collect();
        let zh = raw["zh"]
            .as_object()
            .unwrap()
            .iter()
            .map(|(k, v)| (k.clone(), v.as_str().unwrap_or_default().to_owned()))
            .collect();
        Fixture { raw, keywords, zh }
    }

    fn by_title<'a>(f: &'a Fixture, title: &str) -> &'a Value {
        f.raw["cards"]
            .as_object()
            .unwrap()
            .values()
            .find(|c| c["Localization"]["Title"]["Text"] == title)
            .unwrap()
    }

    fn english(f: &Fixture, title: &str) -> Value {
        card_definition(
            by_title(f, title),
            &Language::new("en", &f.keywords, HashMap::new()),
        )
        .unwrap()
    }

    fn values(line: &Value) -> &Value {
        &line["segments"]
            .as_array()
            .unwrap()
            .iter()
            .find(|s| s.get("tiers").is_some())
            .unwrap()["tiers"]
    }

    #[test]
    fn radar_module_values_units_and_added_tag() {
        let f = fixture();
        let card = english(&f, "Radar Module");
        assert_eq!(card["startingTier"], "Silver");
        assert_eq!(card["tags"], json!(["Tech"]));
        assert_eq!(
            card["lines"],
            json!([{
                "id": 0, "available": true, "resolved": "full", "tiers": ["Silver", "Gold", "Diamond"],
                "segments": [
                    { "text": "When an enemy uses an item, items adjacent to this gain " },
                    { "token": "{ability.0}", "unit": "percent", "keyword": "Crit Chance", "tiers": { "Silver": 4, "Gold": 8, "Diamond": 12 } },
                    { "text": "% " },
                    { "keyword": "Crit Chance", "label": "Crit Chance" },
                ],
            }])
        );
        let icy = &card["enchantments"]["Icy"]["lines"][0];
        let tiers: Map<String, Value> = icy["tiers"]
            .as_array()
            .unwrap()
            .iter()
            .map(|t| (t.as_str().unwrap().to_owned(), json!(0.5)))
            .collect();
        assert!(icy["segments"].as_array().unwrap().contains(&json!({ "token": "{ability.e1}", "unit": "seconds", "keyword": "Freeze", "tiers": tiers })));
        assert!(card["enchantments"]["Obsidian"]["tags"]
            .as_array()
            .unwrap()
            .contains(&json!("Weapon")));
    }

    #[test]
    fn damage_ammo_and_multicast() {
        let f = fixture();
        let pistol = english(&f, "Pistol Sword");
        for line in pistol["lines"].as_array().unwrap() {
            assert_eq!(values(line), &json!({ "Gold": 15, "Diamond": 30 }));
        }
        let ammo: Vec<&Value> = pistol["attributes"]
            .as_object()
            .unwrap()
            .values()
            .map(|a| &a["AmmoMax"])
            .collect();
        assert_eq!(ammo, [&json!(3), &json!(3)]);
        let sword = english(&f, "Sword of Swords");
        for (line, amount) in [
            (&sword["lines"][0], 150),
            (&sword["enchantments"]["Shiny"]["lines"][0], 1),
        ] {
            assert_eq!(
                values(line),
                &json!({ "Diamond": amount, "Legendary": amount })
            );
        }
    }

    #[test]
    fn dynamic_values_preserve_the_token() {
        let f = fixture();
        for (name, token, status) in [
            ("Healthy Heart", "{ability.0}", "none"),
            ("Shortened Rage", "{aura.9}", "partial"),
        ] {
            let line = &english(&f, name)["lines"][0];
            assert_eq!(line["resolved"], status);
            assert!(line["segments"]
                .as_array()
                .unwrap()
                .contains(&json!({ "token": token })));
        }
    }

    #[test]
    fn locale_availability_and_keywords() {
        let f = fixture();
        let raw = by_title(&f, "Radar Module");
        let chinese = |translations: HashMap<String, String>| {
            card_definition(raw, &Language::new("zh", &f.keywords, translations)).unwrap()["lines"]
                [0]
            .clone()
        };
        let line = chinese(f.zh.clone());
        assert_eq!(line["resolved"], "full");
        let label = &f.zh["b62e02b57d80e3be252fe8484d70c8ea"];
        assert!(line["segments"]
            .as_array()
            .unwrap()
            .iter()
            .any(|s| s["keyword"] == "Crit Chance"
                && s["label"].as_str().is_some_and(|l| label.contains(l))));
        let key = raw["Localization"]["Tooltips"][0]["Content"]["Key"]
            .as_str()
            .unwrap()
            .to_owned();
        for translation in [Some("缺少占位符"), None] {
            let mut translations = f.zh.clone();
            match translation {
                Some(t) => translations.insert(key.clone(), t.to_owned()),
                None => translations.remove(&key),
            };
            let line = chinese(translations);
            assert_eq!(line["available"], false);
            assert_eq!(
                (&line["resolved"], &line["segments"]),
                (&json!("none"), &json!([]))
            );
        }
    }

    #[test]
    fn all_enchantment_names_follow_the_same_definition_shape() {
        let f = fixture();
        let mut raw = by_title(&f, "Radar Module").clone();
        let names = "Deadly Ethereal Fiery Golden Heavy Icy Mystical Obsidian Radiant Restorative Shielded Shiny Tiny Toxic Turbo Mossy";
        let icy = raw["Enchantments"]["Icy"].clone();
        raw["Enchantments"] = Value::Object(
            names
                .split(' ')
                .map(|n| (n.to_owned(), icy.clone()))
                .collect(),
        );
        let definition =
            card_definition(&raw, &Language::new("en", &f.keywords, HashMap::new())).unwrap();
        let mut got: Vec<&String> = definition["enchantments"]
            .as_object()
            .unwrap()
            .keys()
            .collect();
        let mut want: Vec<String> = names.split(' ').map(str::to_owned).collect();
        got.sort();
        want.sort();
        assert_eq!(got, want.iter().collect::<Vec<_>>());
    }

    #[test]
    fn digest_delivery_preserves_retired_definitions_in_each_locale() {
        let f = fixture();
        let out = tempfile::tempdir().unwrap();
        let zh_path = out.path().join("zh-CN.bytes");
        {
            let zh = Connection::open(&zh_path).unwrap();
            zh.execute_batch("CREATE TABLE translation (hash, text)")
                .unwrap();
            for (k, v) in &f.zh {
                zh.execute("INSERT INTO translation VALUES (?1, ?2)", (k, v))
                    .unwrap();
            }
        }
        let db = Connection::open_in_memory().unwrap();
        db.execute_batch("CREATE TABLE cards (id, data); CREATE TABLE tooltips (data)")
            .unwrap();
        let cards = f.raw["cards"].as_object().unwrap();
        for (k, c) in cards {
            db.execute("INSERT INTO cards VALUES (?1, ?2)", (k, c.to_string()))
                .unwrap();
        }
        db.execute(
            "INSERT INTO tooltips VALUES (?1)",
            [f.raw["keywords"].to_string()],
        )
        .unwrap();
        let write = |text: Option<&Map<String, Value>>| {
            let previous = text.map(|text| Previous {
                dir: out.path(),
                text,
            });
            write_card_text(&db, &zh_path, previous.as_ref(), out.path()).unwrap()
        };
        let files = write(None);
        let as_map = |files: &BTreeMap<String, String>| {
            files
                .iter()
                .map(|(k, v)| (k.clone(), json!(v)))
                .collect::<Map<_, _>>()
        };
        db.execute(
            "DELETE FROM cards WHERE id = ?1",
            [cards.keys().next().unwrap()],
        )
        .unwrap();
        assert_eq!(files, write(Some(&as_map(&files))));
        // A file whose enchantments are inline still merges.
        let mut inline = BTreeMap::new();
        for (locale, name) in &files {
            let mut payload: Value =
                serde_json::from_slice(&std::fs::read(out.path().join(name)).unwrap()).unwrap();
            let shared = payload
                .as_object_mut()
                .unwrap()
                .remove("enchantments")
                .unwrap();
            for card in payload["cards"].as_object_mut().unwrap().values_mut() {
                let refs = card["enchantments"].as_object().unwrap().clone();
                card["enchantments"] = Value::Object(
                    refs.into_iter()
                        .map(|(n, i)| (n, shared[i.as_u64().unwrap() as usize].clone()))
                        .collect(),
                );
            }
            let data = payload.to_string();
            let file = format!(
                "text-{locale}-{}.json",
                &hex(&sha2::Sha256::digest(data.as_bytes()))[..20]
            );
            std::fs::write(out.path().join(&file), data).unwrap();
            inline.insert(locale.clone(), file);
        }
        assert_eq!(files, write(Some(&as_map(&inline))));
        for (locale, name) in &files {
            let payload: Value =
                serde_json::from_slice(&std::fs::read(out.path().join(name)).unwrap()).unwrap();
            assert_eq!(
                (&payload["version"], &payload["locale"]),
                (&json!(2), &json!(locale))
            );
            let shared = payload["enchantments"].as_array().unwrap();
            assert_eq!(
                shared.len(),
                shared.iter().map(canonical).collect::<HashSet<_>>().len()
            );
            for card in payload["cards"].as_object().unwrap().values() {
                assert!(card["enchantments"]
                    .as_object()
                    .unwrap()
                    .values()
                    .all(|i| (i.as_u64().unwrap() as usize) < shared.len()));
            }
            let expected = if locale == "en" {
                json!({ "Weapon": "Weapon", "Relic": "Relic", "Tech": "Tech" })
            } else {
                json!({ "Weapon": "武器", "Tech": "科技" })
            };
            assert_eq!(payload["tagLabels"], expected);
            let ids: BTreeSet<&String> = payload["cards"].as_object().unwrap().keys().collect();
            assert_eq!(ids, cards.keys().collect());
        }
    }
}
