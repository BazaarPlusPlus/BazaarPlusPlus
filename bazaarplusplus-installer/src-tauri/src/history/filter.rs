use std::collections::{BTreeMap, HashMap, HashSet};

use chrono::{DateTime, Duration, TimeZone};
use rusqlite::types::Value;

use crate::history::hero::canonical_hero_id;
use crate::history::instant::local_day_start;

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

    /// Key Card options deliberately ignore the selected card so the user can switch directly to another.
    pub(crate) fn any_key_card(&self) -> RunQuery {
        RunQuery {
            key_card: None,
            ..self.clone()
        }
    }

    pub(crate) fn condition(
        &self,
        spellings: &HeroSpellings,
        boards: &FinalBoards,
    ) -> RunCondition {
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
            clauses.push("run_id in (select value from json_each(?))".to_string());
            params.push(Value::Text(
                serde_json::Value::from(boards.runs_holding(key_card)).to_string(),
            ));
        }
        RunCondition {
            sql: if clauses.is_empty() {
                "1".to_string()
            } else {
                clauses.join(" and ")
            },
            params,
            unfiltered: self.is_unfiltered(),
        }
    }
}

pub(crate) struct RunCondition {
    pub(crate) sql: String,
    pub(crate) params: Vec<Value>,
    pub(crate) unfiltered: bool,
}

#[derive(Clone, Debug)]
pub(crate) struct BoardItem {
    pub(crate) template_id: String,
    pub(crate) name: Option<String>,
}

#[derive(Debug, Default)]
pub(crate) struct FinalBoards(HashMap<String, Vec<BoardItem>>);

impl FinalBoards {
    pub(crate) fn from_items(items: impl IntoIterator<Item = (String, BoardItem)>) -> Self {
        let mut boards = HashMap::<String, Vec<BoardItem>>::new();
        for (run_id, item) in items {
            boards.entry(run_id).or_default().push(item);
        }
        Self(boards)
    }

    fn runs_holding(&self, template_id: &str) -> Vec<String> {
        let mut runs = self
            .0
            .iter()
            .filter(|(_, items)| items.iter().any(|item| item.template_id == template_id))
            .map(|(run_id, _)| run_id.clone())
            .collect::<Vec<_>>();
        runs.sort();
        runs
    }

    pub(crate) fn key_card_options(
        &self,
        runs: &HashSet<String>,
        selected: Option<&str>,
    ) -> Vec<BoardItem> {
        let mut held = HashMap::<&str, (usize, &BoardItem)>::new();
        for (run_id, items) in &self.0 {
            if !runs.contains(run_id) {
                continue;
            }
            let mut seen = HashSet::new();
            for item in items {
                if seen.insert(item.template_id.as_str()) {
                    held.entry(item.template_id.as_str()).or_insert((0, item)).0 += 1;
                }
            }
        }
        let mut options = held.into_values().collect::<Vec<_>>();
        options.sort_by(|(a_runs, a), (b_runs, b)| {
            b_runs
                .cmp(a_runs)
                .then_with(|| a.name.cmp(&b.name))
                .then_with(|| a.template_id.cmp(&b.template_id))
        });
        let mut options = options
            .into_iter()
            .map(|(_, item)| item.clone())
            .collect::<Vec<_>>();
        if let Some(selected) = selected {
            if !options.iter().any(|item| item.template_id == selected) {
                options.push(
                    self.0
                        .values()
                        .flatten()
                        .find(|item| item.template_id == selected)
                        .cloned()
                        .unwrap_or_else(|| BoardItem {
                            template_id: selected.to_string(),
                            name: None,
                        }),
                );
            }
        }
        options
    }
}

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

    pub(crate) fn heroes(&self) -> Vec<String> {
        self.0.keys().cloned().collect()
    }
}
