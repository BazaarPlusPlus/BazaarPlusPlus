use reqwest::blocking::Client;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::fs;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering as AtomicOrdering};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use crate::commands::{debug_error, debug_log};

const BUNDLED_SUPPORTERS_JSON: &str = include_str!("../../../static/support/supporter-list.json");
const SUPPORTERS_CACHE_FILE_NAME: &str = "supporters-cache.json";
const SUPPORTERS_CACHE_DIR_NAME: &str = "BazaarPlusPlusInstaller";
const SUPPORTERS_REMOTE_URL: &str = "https://example.com/supporter-list.json";
static SUPPORTERS_REFRESH_IN_FLIGHT: AtomicBool = AtomicBool::new(false);

pub const SUPPORTER_REFRESH_INTERVAL_SECS: u64 = 12 * 60 * 60;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SupporterEntry {
    pub name: String,
    pub tier: u8,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SupportersPayload {
    pub entries: Vec<SupporterEntry>,
    #[serde(rename = "fetchedAt")]
    pub fetched_at: Option<u64>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum SupportersSource {
    Bundled,
    Cache,
    Remote,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SupportersResponse {
    pub entries: Vec<SupporterEntry>,
    pub source: SupportersSource,
    pub fetched_at: Option<u64>,
    pub stale: bool,
}

#[derive(Debug, Clone, PartialEq, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct SupportersCacheDocument {
    entries: Vec<SupporterEntry>,
    fetched_at: u64,
}

#[tauri::command]
pub async fn load_supporters() -> Result<SupportersResponse, String> {
    tauri::async_runtime::spawn_blocking(load_supporters_sync)
        .await
        .map_err(|err| format!("failed to join supporters loader: {err}"))?
}

pub fn normalize_supporter_entries(raw: &str) -> Option<Vec<SupporterEntry>> {
    let payload = serde_json::from_str::<Value>(raw).ok()?;
    let entries = payload.as_array()?;

    let mut normalized_entries = Vec::with_capacity(entries.len());
    for entry in entries {
        let name = entry.get("name")?.as_str()?.trim();
        let tier = entry.get("tier")?.as_u64()?;

        if name.is_empty() || !(1..=4).contains(&tier) {
            return None;
        }

        normalized_entries.push(SupporterEntry {
            name: name.to_string(),
            tier: tier as u8,
        });
    }

    sort_supporters(&mut normalized_entries);

    Some(normalized_entries)
}

pub fn is_cache_stale(fetched_at: Option<u64>, now: u64) -> bool {
    fetched_at
        .map(|timestamp| now.saturating_sub(timestamp) >= SUPPORTER_REFRESH_INTERVAL_SECS)
        .unwrap_or(true)
}

pub fn select_local_payload(
    bundled_entries: Vec<SupporterEntry>,
    cache_entries: Option<Vec<SupporterEntry>>,
    cache_fetched_at: Option<u64>,
) -> SupportersPayload {
    if let Some(entries) = cache_entries {
        return SupportersPayload {
            entries,
            fetched_at: cache_fetched_at,
        };
    }

    SupportersPayload {
        entries: bundled_entries,
        fetched_at: None,
    }
}

fn load_supporters_sync() -> Result<SupportersResponse, String> {
    let bundled_entries = load_bundled_entries()?;
    let cached_payload = read_cached_payload();
    let local_payload = select_local_payload(
        bundled_entries,
        cached_payload.as_ref().map(|cache| cache.entries.clone()),
        cached_payload.as_ref().map(|cache| cache.fetched_at),
    );
    let local_source = if local_payload.fetched_at.is_some() {
        SupportersSource::Cache
    } else {
        SupportersSource::Bundled
    };
    let now = current_unix_timestamp_secs()?;
    let stale = is_cache_stale(local_payload.fetched_at, now);

    Ok(build_local_supporters_response(
        local_payload,
        local_source,
        stale,
        || schedule_supporters_refresh(now),
    ))
}

fn load_bundled_entries() -> Result<Vec<SupporterEntry>, String> {
    normalize_supporter_entries(BUNDLED_SUPPORTERS_JSON)
        .ok_or_else(|| "bundled supporter list is invalid".to_string())
}

fn read_cached_payload() -> Option<SupportersCacheDocument> {
    let cache_path = cache_file_path()?;
    let raw = fs::read_to_string(cache_path).ok()?;
    let document = serde_json::from_str::<SupportersCacheDocument>(&raw).ok()?;

    let entries_json = serde_json::to_string(&document.entries).ok()?;
    let entries = normalize_supporter_entries(&entries_json)?;

    Some(SupportersCacheDocument {
        entries,
        fetched_at: document.fetched_at,
    })
}

fn write_cached_payload(entries: &[SupporterEntry], fetched_at: u64) -> Result<(), String> {
    let Some(cache_dir) = cache_directory() else {
        return Err("cache directory is unavailable".to_string());
    };

    fs::create_dir_all(&cache_dir)
        .map_err(|err| format!("failed to create supporters cache directory: {err}"))?;

    let payload = SupportersCacheDocument {
        entries: entries.to_vec(),
        fetched_at,
    };
    let raw = serde_json::to_string_pretty(&payload)
        .map_err(|err| format!("failed to serialize supporters cache payload: {err}"))?;

    fs::write(cache_dir.join(SUPPORTERS_CACHE_FILE_NAME), raw)
        .map_err(|err| format!("failed to write supporters cache payload: {err}"))
}

fn fetch_remote_entries(remote_url: &str) -> Result<Vec<SupporterEntry>, String> {
    let client = Client::builder()
        .timeout(Duration::from_secs(5))
        .build()
        .map_err(|err| format!("failed to build supporters client: {err}"))?;
    let response = client
        .get(remote_url)
        .send()
        .map_err(|err| format!("failed to fetch supporters payload: {err}"))?;

    if !response.status().is_success() {
        return Err(format!(
            "supporters payload request returned status {}",
            response.status()
        ));
    }

    let raw = response
        .text()
        .map_err(|err| format!("failed to read supporters payload body: {err}"))?;

    normalize_supporter_entries(&raw)
        .ok_or_else(|| "remote supporters payload is invalid".to_string())
}

fn build_local_supporters_response<Schedule>(
    local_payload: SupportersPayload,
    local_source: SupportersSource,
    stale: bool,
    schedule_refresh: Schedule,
) -> SupportersResponse
where
    Schedule: FnOnce() -> Result<(), String>,
{
    if stale {
        if let Err(_error) = schedule_refresh() {
            debug_error!("failed to schedule supporters refresh: {_error}");
        }
    }

    SupportersResponse {
        entries: local_payload.entries,
        source: local_source,
        fetched_at: local_payload.fetched_at,
        stale,
    }
}

fn schedule_supporters_refresh(fetched_at: u64) -> Result<(), String> {
    if SUPPORTERS_REFRESH_IN_FLIGHT
        .compare_exchange(false, true, AtomicOrdering::AcqRel, AtomicOrdering::Acquire)
        .is_err()
    {
        return Ok(());
    }

    std::thread::spawn(move || {
        if let Err(_error) = refresh_supporters_cache(SUPPORTERS_REMOTE_URL, fetched_at) {
            debug_error!("failed to refresh supporters: {_error}");
        }

        SUPPORTERS_REFRESH_IN_FLIGHT.store(false, AtomicOrdering::Release);
    });

    Ok(())
}

fn refresh_supporters_cache(
    remote_url: &str,
    fetched_at: u64,
) -> Result<Vec<SupporterEntry>, String> {
    refresh_supporters_cache_with(
        remote_url,
        fetched_at,
        fetch_remote_entries,
        write_cached_payload,
    )
}

fn refresh_supporters_cache_with<Fetch, Write>(
    remote_url: &str,
    fetched_at: u64,
    fetch_entries: Fetch,
    write_cache: Write,
) -> Result<Vec<SupporterEntry>, String>
where
    Fetch: FnOnce(&str) -> Result<Vec<SupporterEntry>, String>,
    Write: FnOnce(&[SupporterEntry], u64) -> Result<(), String>,
{
    let entries = fetch_entries(remote_url)?;
    write_cache(&entries, fetched_at)?;
    Ok(entries)
}

fn cache_directory() -> Option<PathBuf> {
    dirs::cache_dir().map(|path| path.join(SUPPORTERS_CACHE_DIR_NAME))
}

fn cache_file_path() -> Option<PathBuf> {
    cache_directory().map(|dir| dir.join(SUPPORTERS_CACHE_FILE_NAME))
}

fn current_unix_timestamp_secs() -> Result<u64, String> {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|duration| duration.as_secs())
        .map_err(|err| format!("system time before unix epoch: {err}"))
}

fn sort_supporters(entries: &mut [SupporterEntry]) {
    entries.sort_by(|left, right| {
        right
            .tier
            .cmp(&left.tier)
            .then_with(|| left.name.cmp(&right.name))
    });
    debug_log!("loaded {} supporters", entries.len());
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    use std::cell::RefCell;

    #[test]
    fn test_normalize_supporter_entries_accepts_entries_without_amount() {
        let raw = r#"[{"name":"Alice","tier":4},{"name":"Bob","tier":2}]"#;

        let entries = normalize_supporter_entries(raw).expect("expected entries");

        assert_eq!(
            serde_json::to_value(entries).expect("expected serializable entries"),
            json!([
                {"name": "Alice", "tier": 4},
                {"name": "Bob", "tier": 2}
            ])
        );
    }

    #[test]
    fn test_normalize_supporter_entries_ignores_legacy_amount_field() {
        let raw =
            r#"[{"name":"Alice","tier":4,"amount":1.5},{"name":"Bob","tier":2,"amount":0.1}]"#;

        let entries = normalize_supporter_entries(raw).expect("expected entries");

        assert_eq!(
            serde_json::to_value(entries).expect("expected serializable entries"),
            json!([
                {"name": "Alice", "tier": 4},
                {"name": "Bob", "tier": 2}
            ])
        );
    }

    #[test]
    fn test_normalize_supporter_entries_rejects_invalid_entries() {
        let raw = r#"[{"name":" ","tier":4},{"name":"Bob","tier":8}]"#;

        let entries = normalize_supporter_entries(raw);

        assert_eq!(entries, None);
    }

    #[test]
    fn test_normalize_supporter_entries_sorts_by_tier_then_name() {
        let raw = r#"[{"name":"Zed","tier":3,"amount":0.3},{"name":"Amy","tier":4,"amount":0.2},{"name":"Bob","tier":4,"amount":0.9},{"name":"Cara","tier":4,"amount":0.5}]"#;

        let entries = normalize_supporter_entries(raw).expect("expected entries");

        assert_eq!(
            serde_json::to_value(entries).expect("expected serializable entries"),
            json!([
                {"name": "Amy", "tier": 4},
                {"name": "Bob", "tier": 4},
                {"name": "Cara", "tier": 4},
                {"name": "Zed", "tier": 3}
            ])
        );
    }

    #[test]
    fn test_is_cache_stale_returns_false_within_refresh_window() {
        let now = 200_000;

        assert_eq!(
            is_cache_stale(Some(now - SUPPORTER_REFRESH_INTERVAL_SECS + 1), now),
            false
        );
    }

    #[test]
    fn test_is_cache_stale_returns_true_at_refresh_boundary() {
        let now = 200_000;

        assert_eq!(
            is_cache_stale(Some(now - SUPPORTER_REFRESH_INTERVAL_SECS), now),
            true
        );
    }

    #[test]
    fn test_select_local_payload_prefers_valid_cache_over_bundled_entries() {
        let bundled_entries = vec![SupporterEntry {
            name: "Bundled".to_string(),
            tier: 1,
        }];
        let cache_entries = vec![SupporterEntry {
            name: "Cached".to_string(),
            tier: 4,
        }];

        let payload = select_local_payload(bundled_entries, Some(cache_entries.clone()), Some(123));

        assert_eq!(
            payload,
            SupportersPayload {
                entries: cache_entries,
                fetched_at: Some(123),
            }
        );
    }

    #[test]
    fn test_select_local_payload_falls_back_to_bundled_entries_when_cache_missing() {
        let bundled_entries = vec![SupporterEntry {
            name: "Bundled".to_string(),
            tier: 1,
        }];

        let payload = select_local_payload(bundled_entries.clone(), None, None);

        assert_eq!(
            payload,
            SupportersPayload {
                entries: bundled_entries,
                fetched_at: None,
            }
        );
    }

    #[test]
    fn test_stale_local_response_returns_immediately_and_requests_background_refresh() {
        let local_payload = SupportersPayload {
            entries: vec![SupporterEntry {
                name: "Bundled".to_string(),
                tier: 1,
            }],
            fetched_at: None,
        };
        let refresh_requested = RefCell::new(false);

        let response = build_local_supporters_response(
            local_payload.clone(),
            SupportersSource::Bundled,
            true,
            || {
                *refresh_requested.borrow_mut() = true;
                Ok(())
            },
        );

        assert_eq!(response.entries, local_payload.entries);
        assert_eq!(response.source, SupportersSource::Bundled);
        assert_eq!(response.fetched_at, None);
        assert_eq!(response.stale, true);
        assert_eq!(*refresh_requested.borrow(), true);
    }

    #[test]
    fn test_refresh_supporters_cache_with_writes_latest_remote_entries() {
        let remote_entries = vec![SupporterEntry {
            name: "Remote".to_string(),
            tier: 4,
        }];
        let written_entries = RefCell::new(None);

        let refreshed = refresh_supporters_cache_with(
            "https://example.com/supporter-list.json",
            456,
            |_| Ok(remote_entries.clone()),
            |entries, fetched_at| {
                written_entries.replace(Some((entries.to_vec(), fetched_at)));
                Ok(())
            },
        )
        .expect("expected refresh result");

        assert_eq!(refreshed, remote_entries);
        assert_eq!(written_entries.into_inner(), Some((remote_entries, 456)));
    }
}
