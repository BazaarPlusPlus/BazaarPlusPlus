//! The History List filter: what the frontend selects, and the query it
//! resolves to at one local instant.

use std::collections::BTreeMap;

use chrono::{DateTime, Duration, TimeZone};
use rusqlite::types::Value;

use crate::history::final_board::FINAL_BOARD_ITEMS;
use crate::history::hero::canonical_hero_id;
use crate::history::instant::local_day_start;

/// The instant the History List orders and filters Runs by: the Run's last activity.
pub(crate) const RUN_LIST_TIME: &str = "coalesce(ended_at_ms, last_seen_at_ms, started_at_ms)";

/// Wire strings are a stable contract with the frontend filter bar.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, serde::Deserialize, specta::Type)]
pub enum HistoryPeriod {
    #[default]
    #[serde(rename = "all")]
    All,
    #[serde(rename = "today")]
    Today,
    #[serde(rename = "last_7_days")]
    Last7Days,
    #[serde(rename = "last_30_days")]
    Last30Days,
}

/// The finished results a Run can be filtered by. `Win` and `Loss` are the
/// rows `derive_run_result` (`history/mapper.rs`) names `win` and `loss`;
/// abandoned and in-progress Runs show only under `All`.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, serde::Deserialize, specta::Type)]
pub enum HistoryResultFilter {
    #[default]
    #[serde(rename = "all")]
    All,
    #[serde(rename = "win")]
    Win,
    #[serde(rename = "loss")]
    Loss,
}

impl HistoryResultFilter {
    fn predicate(self) -> Option<&'static str> {
        match self {
            Self::All => None,
            Self::Win => Some("status = 'completed' and coalesce(victories, 0) >= 10"),
            Self::Loss => Some("status = 'completed' and coalesce(victories, 0) < 10"),
        }
    }
}

/// The History List filter the frontend sends.
#[derive(Clone, Debug, Default, PartialEq, Eq, serde::Deserialize, specta::Type)]
pub struct HistoryRunFilter {
    /// A canonical hero id from `HistoryRunList::heroes`.
    pub hero: Option<String>,
    pub period: HistoryPeriod,
    pub result: HistoryResultFilter,
    /// A card template id from `HistoryRunList::key_cards`: Runs whose Final
    /// Board holds that item.
    pub key_card: Option<String>,
}

impl HistoryRunFilter {
    /// Resolves the period against `now`, the user's local time.
    pub(crate) fn query_at<Tz: TimeZone>(&self, now: DateTime<Tz>) -> RunQuery {
        let since = match self.period {
            HistoryPeriod::All => None,
            HistoryPeriod::Today => {
                Some(local_day_start(&now, now.date_naive()).unwrap_or_else(|| now.clone()))
            }
            HistoryPeriod::Last7Days => Some(now.clone() - Duration::days(7)),
            HistoryPeriod::Last30Days => Some(now.clone() - Duration::days(30)),
        };
        RunQuery {
            hero: self.hero.clone(),
            since_ms: since.map(|instant| instant.timestamp_millis()),
            result: self.result,
            key_card: self.key_card.clone(),
        }
    }
}

/// A History List filter with its period resolved to an instant. The default
/// selects every Run.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub(crate) struct RunQuery {
    pub(crate) hero: Option<String>,
    pub(crate) since_ms: Option<i64>,
    pub(crate) result: HistoryResultFilter,
    pub(crate) key_card: Option<String>,
}

impl RunQuery {
    pub(crate) fn is_unfiltered(&self) -> bool {
        self.hero.is_none()
            && self.since_ms.is_none()
            && self.result == HistoryResultFilter::All
            && self.key_card.is_none()
    }

    /// The same query with every key card: the Runs whose Final Boards the
    /// key card options come from.
    pub(crate) fn any_key_card(&self) -> RunQuery {
        RunQuery {
            key_card: None,
            ..self.clone()
        }
    }

    /// The `where` condition on `runs` and its parameters, in order.
    /// `spellings` maps each canonical hero id to the stored spellings it covers.
    pub(crate) fn condition(&self, spellings: &HeroSpellings) -> (String, Vec<Value>) {
        let mut clauses = Vec::new();
        let mut params = Vec::new();
        if let Some(hero) = &self.hero {
            match spellings.0.get(hero.as_str()) {
                Some(raw) => {
                    clauses.push(format!(
                        "hero in ({})",
                        std::iter::repeat_n("?", raw.len())
                            .collect::<Vec<_>>()
                            .join(", ")
                    ));
                    params.extend(raw.iter().cloned().map(Value::Text));
                }
                None => clauses.push("0".to_string()),
            }
        }
        if let Some(since) = self.since_ms {
            clauses.push(format!("{RUN_LIST_TIME} >= ?"));
            params.push(Value::Integer(since));
        }
        if let Some(predicate) = self.result.predicate() {
            clauses.push(format!("({predicate})"));
        }
        if let Some(key_card) = &self.key_card {
            clauses.push(format!(
                "run_id in (select run_id from ({FINAL_BOARD_ITEMS}) where template_id = ?)"
            ));
            params.push(Value::Text(key_card.clone()));
        }
        let condition = if clauses.is_empty() {
            "1".to_string()
        } else {
            clauses.join(" and ")
        };
        (condition, params)
    }
}

/// The stored hero spellings of every Run, grouped by canonical hero id.
#[derive(Debug, Default)]
pub(crate) struct HeroSpellings(BTreeMap<String, Vec<String>>);

impl HeroSpellings {
    pub(crate) fn from_stored(stored: impl IntoIterator<Item = String>) -> Self {
        let mut spellings = BTreeMap::<String, Vec<String>>::new();
        for raw in stored {
            let canonical = canonical_hero_id(&raw);
            if !canonical.is_empty() {
                spellings.entry(canonical).or_default().push(raw);
            }
        }
        Self(spellings)
    }

    /// Canonical hero ids, sorted.
    pub(crate) fn heroes(&self) -> Vec<String> {
        self.0.keys().cloned().collect()
    }
}
