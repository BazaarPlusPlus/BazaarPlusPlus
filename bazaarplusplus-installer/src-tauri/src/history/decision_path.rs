use super::dto::{CombatEvent, DecisionPathSection, LocalStreamFrame};
use anyhow::ensure;
use base64::{engine::general_purpose::STANDARD, Engine};
use rusqlite::Connection;

pub(super) fn read(conn: &Connection, run_id: &str) -> DecisionPathSection {
    load(conn, run_id).unwrap_or(DecisionPathSection::Unreadable)
}

fn load(conn: &Connection, run_id: &str) -> anyhow::Result<DecisionPathSection> {
    let mut stmt = conn.prepare(
        "SELECT seq, ts_at_ms, kind, payload_json,
                payload_json ->> 'message', payload_json ->> 'command',
                json_type(payload_json, '$.command_args'), payload_json -> 'command_args',
                payload_json ->> 'responds_to_seq', json_type(payload_json, '$.message'), json_type(payload_json, '$.command')
         FROM run_events WHERE run_id = ?1 AND kind IN
         ('stream_message', 'stream_command', 'pvp_combat_recorded', 'pve_combat_recorded')
         ORDER BY seq",
    )?;
    let mut rows = stmt.query([run_id])?;
    let mut scratch = Vec::new();
    let mut frames = Vec::new();
    let mut events = Vec::new();
    while let Some(row) = rows.next()? {
        let kind: String = row.get(2)?;
        let seq = row.get(0)?;
        let timestamp_ms = row.get(1)?;
        if kind.ends_with("_combat_recorded") {
            events.push(CombatEvent {
                seq,
                timestamp_ms,
                kind,
                body: row.get(3)?,
            });
            continue;
        }
        let message_base64: Option<String> = row.get(4)?;
        let command: Option<String> = row.get(5)?;
        let args_type: Option<String> = row.get(6)?;
        let responds_to_seq: Option<i64> = row.get(8)?;
        if kind == "stream_message" {
            ensure!(row.get::<_, Option<String>>(9)?.as_deref() == Some("text"));
            ensure!(command.is_none() && args_type.is_none());
            scratch.clear();
            STANDARD.decode_vec(
                message_base64
                    .as_ref()
                    .ok_or_else(|| anyhow::anyhow!("Missing message"))?,
                &mut scratch,
            )?;
        } else {
            ensure!(row.get::<_, Option<String>>(10)?.as_deref() == Some("text"));
            ensure!(
                command.as_ref().is_some_and(|s| !s.is_empty())
                    && message_base64.is_none()
                    && responds_to_seq.is_none()
            );
            ensure!(matches!(args_type.as_deref(), None | Some("object")));
        }
        frames.push(LocalStreamFrame {
            seq,
            timestamp_ms,
            kind,
            message_base64,
            command,
            command_args_json: row.get(7)?,
            responds_to_seq,
        });
    }
    Ok(if frames.is_empty() {
        DecisionPathSection::Absent
    } else {
        DecisionPathSection::Available { frames, events }
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn preserves_command_arguments_and_sequence_gaps() {
        let db = Connection::open_in_memory().unwrap();
        db.execute_batch(r#"CREATE TABLE run_events(run_id, seq, ts_at_ms, kind, payload_json);
          INSERT INTO run_events VALUES ('r',5,11,'stream_message','{"message":"AQ==","responds_to_seq":2}');"#).unwrap();
        db.execute("INSERT INTO run_events VALUES ('r',2,10,'stream_command',?1)", [include_str!("../../../../bazaarplusplus-mod/tests/BundlePipeline.Tests/fixtures/local-stream-command.json").trim_end()]).unwrap();
        let DecisionPathSection::Available { frames, .. } = read(&db, "r") else {
            panic!("missing stream")
        };
        assert_eq!(
            frames[0].command_args_json.as_deref(),
            Some(r#"{"z":{"id":9223372036854775807},"a":1}"#)
        );
        assert_eq!((frames[1].seq, frames[1].responds_to_seq), (5, Some(2)));
        db.execute(
            "UPDATE run_events SET kind='stream_command' WHERE seq=5",
            [],
        )
        .unwrap();
        assert_eq!(read(&db, "r"), DecisionPathSection::Unreadable);
    }
}
