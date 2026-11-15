//! Card titles from the game's own data cache, read-only (installer ADR-0010).
//! The game keeps its card data and translations under
//! `Application.persistentDataPath/prod/cache` (`DataManifestType.GetCachePath`
//! in the game's `TheBazaarRuntime.dll`); BPP never writes there.

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::time::Duration;

use rusqlite::{params_from_iter, Connection, OpenFlags};

/// A card's titles: English from the game's card data, Simplified Chinese
/// from its zh-CN translations.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub(crate) struct CardTitles {
    pub(crate) en: Option<String>,
    pub(crate) zh: Option<String>,
}

/// Where card titles come from: the game's data cache, or nowhere.
#[derive(Clone, Debug)]
pub(crate) struct GameCardNames {
    cache: Option<PathBuf>,
}

impl GameCardNames {
    /// The cache of the game installed for this user.
    pub(crate) fn installed() -> Self {
        installed_cache_dir().map_or_else(Self::none, Self::at)
    }

    pub(crate) fn at(cache: PathBuf) -> Self {
        Self { cache: Some(cache) }
    }

    /// No titles; cards keep the names their snapshots carry.
    pub(crate) fn none() -> Self {
        Self { cache: None }
    }

    /// Titles of the cards `template_ids` names that the cache knows. A cache
    /// that is missing or unreadable yields fewer titles, never an error: the
    /// History List falls back to the names the snapshots carry.
    pub(crate) fn titles(&self, template_ids: &[String]) -> HashMap<String, CardTitles> {
        let Some(cache) = &self.cache else {
            return HashMap::new();
        };
        if template_ids.is_empty() {
            return HashMap::new();
        }
        match read_titles(cache, template_ids) {
            Ok(titles) => titles,
            Err(error) => {
                eprintln!(
                    "cannot read card titles from the game cache {}: {error}",
                    cache.display()
                );
                HashMap::new()
            }
        }
    }
}

fn installed_cache_dir() -> Option<PathBuf> {
    let home = dirs::home_dir()?;
    if cfg!(target_os = "macos") {
        Some(home.join("Library/Application Support/com.TempoStorm.TheBazaar/prod/cache"))
    } else if cfg!(target_os = "windows") {
        // Unity's persistentDataPath: LocalLow\<company>\<product>.
        Some(home.join("AppData/LocalLow/Tempo Storm/The Bazaar/prod/cache"))
    } else {
        None
    }
}

/// Opens one of the game's files for reading only, and only for this call:
/// the game replaces these files when it updates its data.
fn open_read_only(path: &Path) -> rusqlite::Result<Connection> {
    let conn = Connection::open_with_flags(
        path,
        OpenFlags::SQLITE_OPEN_READ_ONLY | OpenFlags::SQLITE_OPEN_NO_MUTEX,
    )?;
    conn.busy_timeout(Duration::from_millis(500))?;
    Ok(conn)
}

fn placeholders(count: usize) -> String {
    std::iter::repeat_n("?", count)
        .collect::<Vec<_>>()
        .join(", ")
}

fn read_titles(
    cache: &Path,
    template_ids: &[String],
) -> anyhow::Result<HashMap<String, CardTitles>> {
    let game_data = cache.join("GameData.db");
    if !game_data.is_file() {
        return Ok(HashMap::new());
    }
    // `Data` holds JSON text in a BLOB column; read it as text, not as JSONB.
    let mut keyed = Vec::new();
    {
        let conn = open_read_only(&game_data)?;
        let mut stmt = conn.prepare(&format!(
            "
            select
              Id,
              case when json_valid(cast(Data as text))
                then json_extract(cast(Data as text), '$.Localization.Title.Key') end,
              case when json_valid(cast(Data as text))
                then json_extract(cast(Data as text), '$.Localization.Title.Text') end
            from cards
            where Id in ({})
            ",
            placeholders(template_ids.len())
        ))?;
        let rows = stmt.query_map(params_from_iter(template_ids), |row| {
            Ok((
                row.get::<_, String>(0)?,
                row.get::<_, Option<String>>(1)?,
                row.get::<_, Option<String>>(2)?,
            ))
        })?;
        for row in rows {
            keyed.push(row?);
        }
    }

    let keys = keyed
        .iter()
        .filter_map(|(_, key, _)| key.clone())
        .collect::<Vec<_>>();
    let mut zh = HashMap::new();
    let translations = cache.join("translations/zh-CN.bytes");
    if !keys.is_empty() && translations.is_file() {
        let conn = open_read_only(&translations)?;
        let mut stmt = conn.prepare(&format!(
            "select hash, text from translation where hash in ({})",
            placeholders(keys.len())
        ))?;
        let rows = stmt.query_map(params_from_iter(&keys), |row| {
            Ok((row.get::<_, String>(0)?, row.get::<_, String>(1)?))
        })?;
        for row in rows {
            let (hash, text) = row?;
            zh.insert(hash, text);
        }
    }

    Ok(keyed
        .into_iter()
        .map(|(id, key, en)| {
            let zh = key.and_then(|key| zh.get(&key).cloned());
            (id, CardTitles { en, zh })
        })
        .collect())
}
