use super::{
    card_names::GameCardNames,
    dto::{HistoryBattleCard, HistoryBattleRow},
};
use serde::Deserialize;
use std::collections::BTreeSet;

#[derive(Deserialize)]
struct CardSet {
    status: String,
    items: Vec<SnapshotCard>,
}

#[derive(Deserialize)]
struct SnapshotCard {
    instance_id: String,
    template_id: String,
    #[serde(rename = "type")]
    kind: String,
    size: Option<String>,
    socket: Option<String>,
    tier: Option<String>,
    enchant: Option<String>,
    name: Option<String>,
}

pub(super) fn read_set(json: Option<String>, kind: &str) -> Option<Vec<HistoryBattleCard>> {
    let set: CardSet = serde_json::from_str(&json?).ok()?;
    match set.status.as_str() {
        "CapturedEmpty" => Some(Vec::new()),
        "Captured" => Some(
            set.items
                .into_iter()
                .filter(|card| card.kind == kind)
                .map(|card| HistoryBattleCard {
                    instance_id: card.instance_id,
                    name: card
                        .name
                        .filter(|name| !name.trim().is_empty())
                        .unwrap_or_else(|| card.template_id.clone()),
                    name_zh: None,
                    template_id: card.template_id,
                    size: match card.size.as_deref() {
                        Some("Medium") => 2,
                        Some("Large") => 3,
                        _ => 1,
                    },
                    slot: card
                        .socket
                        .as_deref()
                        .and_then(|socket| socket.strip_prefix("Socket_")?.parse::<u8>().ok())
                        .filter(|slot| *slot < 10),
                    tier: card.tier,
                    enchant: card
                        .enchant
                        .filter(|value| !value.trim().is_empty() && value.trim() != "None"),
                })
                .collect(),
        ),
        _ => None,
    }
}

pub(super) fn resolve_titles(battles: &mut [HistoryBattleRow], names: &GameCardNames) {
    let mut cards = battles
        .iter_mut()
        .flat_map(|battle| {
            [
                &mut battle.player_board,
                &mut battle.player_skills,
                &mut battle.opponent_board,
                &mut battle.opponent_skills,
            ]
            .into_iter()
            .flatten()
            .flatten()
        })
        .collect::<Vec<_>>();
    let ids = cards
        .iter()
        .map(|card| card.template_id.clone())
        .collect::<BTreeSet<_>>();
    let titles = names.titles(&ids.into_iter().collect::<Vec<_>>());
    for card in &mut cards {
        if let Some(title) = titles.get(&card.template_id) {
            card.name_zh = title.zh.clone().or_else(|| Some(card.name.clone()));
            if let Some(en) = &title.en {
                card.name.clone_from(en);
            }
        }
    }
}
