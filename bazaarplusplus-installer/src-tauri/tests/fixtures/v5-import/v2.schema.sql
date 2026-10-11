PRAGMA user_version=2;
CREATE TABLE battle_snapshots (
    battle_id TEXT PRIMARY KEY,
    player_hand_json TEXT NOT NULL,
    player_skills_json TEXT NOT NULL,
    opponent_hand_json TEXT NOT NULL,
    opponent_skills_json TEXT NOT NULL,
    FOREIGN KEY (battle_id) REFERENCES battles(battle_id) ON DELETE CASCADE
);
CREATE TABLE battles (
    battle_id TEXT PRIMARY KEY,
    remote_battle_id TEXT NULL,
    uploader_account_id TEXT NULL,
    source TEXT NOT NULL,
    run_id TEXT NULL,
    local_player_account_id TEXT NULL,
    recorded_at_utc TEXT NOT NULL,
    day INTEGER NULL,
    hour INTEGER NULL,
    encounter_id TEXT NULL,
    combat_kind TEXT NOT NULL,
    player_name TEXT NULL,
    player_account_id TEXT NULL,
    player_hero TEXT NULL,
    player_rank TEXT NULL,
    player_rating INTEGER NULL,
    player_level INTEGER NULL,
    player_prestige INTEGER NULL,
    player_income INTEGER NULL,
    player_gold INTEGER NULL,
    player_victories INTEGER NULL,
    player_hand_item_count INTEGER NULL,
    player_skill_count INTEGER NULL,
    opponent_name TEXT NULL,
    opponent_account_id TEXT NULL,
    opponent_hero TEXT NULL,
    opponent_rank TEXT NULL,
    opponent_rating INTEGER NULL,
    opponent_level INTEGER NULL,
    opponent_prestige INTEGER NULL,
    opponent_victories INTEGER NULL,
    opponent_hand_item_count INTEGER NULL,
    opponent_skill_count INTEGER NULL,
    result TEXT NULL,
    winner_combatant_id TEXT NULL,
    loser_combatant_id TEXT NULL,
    is_final_battle INTEGER NOT NULL DEFAULT 0,
    has_local_payload INTEGER NOT NULL DEFAULT 0,
    bundle_id TEXT NULL,
    download_url TEXT NULL,
    download_url_expires_at_ms INTEGER NULL,
    ghost_replay_state TEXT NULL,
    ghost_replay_unavailable_reason TEXT NULL,
    deleted_at_utc TEXT NULL,
    local_payload_state TEXT NULL,
    local_payload_maintenance_at_utc TEXT NULL,
    FOREIGN KEY (run_id) REFERENCES runs(run_id) ON DELETE CASCADE,
    CHECK (
        (source = 'LOCAL' AND remote_battle_id IS NULL AND uploader_account_id IS NULL)
        OR
        (source = 'GHOST' AND run_id IS NULL AND remote_battle_id IS NOT NULL AND uploader_account_id IS NOT NULL)
    ),
    CHECK (
        ghost_replay_state IS NULL OR ghost_replay_state IN (
            'remote_available', 'local_ready', 'unavailable_payload', 'expired'
        )
    ),
    CHECK (
        (source = 'LOCAL' AND local_payload_state IS NOT NULL AND (
            (local_payload_state = 'ready' AND has_local_payload = 1)
            OR
            (local_payload_state IN ('delete_pending', 'evicted', 'missing')
             AND has_local_payload = 0)
        ))
        OR
        (source <> 'LOCAL' AND local_payload_state IS NULL)
    )
);
CREATE TABLE bundle_outbox (
    bundle_id TEXT PRIMARY KEY,
    run_id TEXT NOT NULL,
    file_name TEXT NOT NULL UNIQUE,
    content_sha256_hex TEXT NOT NULL,
    content_digest TEXT NOT NULL,
    total_bytes INTEGER NOT NULL,
    has_screenshot INTEGER NOT NULL,
    sealed_at_utc TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    attempts INTEGER NOT NULL DEFAULT 0,
    last_attempt_at_utc TEXT NULL,
    next_attempt_at_utc TEXT NULL,
    failed_at_utc TEXT NULL,
    last_error_code TEXT NULL,
    last_error_detail TEXT NULL,
    server_request_id TEXT NULL,
    server_outcome TEXT NULL,
    uploaded_at_utc TEXT NULL,
    CHECK (status IN ('pending', 'uploaded', 'permanent_failure'))
);
CREATE TABLE bundle_seal_jobs (
    run_id TEXT PRIMARY KEY,
    state TEXT NOT NULL DEFAULT 'waiting',
    player_account_id TEXT NULL,
    screenshot_requested INTEGER NOT NULL,
    screenshot_state TEXT NOT NULL DEFAULT 'waiting',
    input_deadline_at_utc TEXT NOT NULL,
    bundle_id TEXT NULL UNIQUE,
    created_at_ms INTEGER NULL,
    attempts INTEGER NOT NULL DEFAULT 0,
    last_attempt_at_utc TEXT NULL,
    last_error_code TEXT NULL,
    last_error_detail TEXT NULL,
    FOREIGN KEY (run_id) REFERENCES runs(run_id) ON DELETE CASCADE,
    CHECK (state IN ('waiting', 'sealing', 'terminal_failure')),
    CHECK (screenshot_state IN ('not_requested', 'waiting', 'available', 'unavailable', 'timed_out'))
);
CREATE TABLE combat_replay_videos (
    video_id TEXT PRIMARY KEY,
    battle_id TEXT NOT NULL,
    source TEXT NOT NULL,
    video_relative_path TEXT NOT NULL,
    width INTEGER NOT NULL,
    height INTEGER NOT NULL,
    fps INTEGER NOT NULL,
    codec TEXT NOT NULL,
    crf INTEGER NULL,
    preset TEXT NULL,
    started_at_utc TEXT NOT NULL,
    ended_at_utc TEXT NULL,
    duration_ms INTEGER NULL,
    captured_frames INTEGER NOT NULL DEFAULT 0,
    dropped_frames INTEGER NOT NULL DEFAULT 0,
    file_size_bytes INTEGER NULL,
    status TEXT NOT NULL,
    error TEXT NULL,
    attachment_state TEXT NOT NULL DEFAULT 'attached',
    file_state TEXT NOT NULL DEFAULT 'pending',
    detached_at_utc TEXT NULL,
    missing_at_utc TEXT NULL,
    last_reconciled_at_utc TEXT NULL,
    CHECK (attachment_state IN ('attached', 'detached')),
    CHECK (file_state IN ('pending', 'present', 'missing', 'deleted'))
);
CREATE TABLE run_events (
    run_id TEXT NOT NULL,
    seq INTEGER NOT NULL,
    ts_utc TEXT NOT NULL,
    kind TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    PRIMARY KEY (run_id, seq),
    FOREIGN KEY (run_id) REFERENCES runs(run_id) ON DELETE CASCADE
);
CREATE TABLE run_screenshots (
    screenshot_id TEXT PRIMARY KEY,
    run_id TEXT NULL,
    hero_name TEXT NULL,
    battle_id TEXT NULL,
    capture_source TEXT NOT NULL,
    is_primary INTEGER NOT NULL DEFAULT 0,
    image_relative_path TEXT NOT NULL,
    captured_at_local TEXT NOT NULL,
    captured_at_utc TEXT NOT NULL,
    day INTEGER NULL,
    player_rank TEXT NULL,
    player_rating INTEGER NULL,
    player_position INTEGER NULL,
    victories_at_capture INTEGER NULL,
    build_channel TEXT NULL
);
CREATE TABLE runs (
    run_id TEXT PRIMARY KEY,
    started_at_utc TEXT NOT NULL,
    last_seen_at_utc TEXT NOT NULL,
    status TEXT NOT NULL,
    completed INTEGER NOT NULL DEFAULT 0,
    hero TEXT NOT NULL,
    game_mode TEXT NOT NULL,
    seed INTEGER NULL,
    player_rank TEXT NULL,
    player_rating INTEGER NULL,
    day INTEGER NULL,
    hour INTEGER NULL,
    max_health INTEGER NULL,
    prestige INTEGER NULL,
    level INTEGER NULL,
    income INTEGER NULL,
    gold INTEGER NULL,
    last_seq INTEGER NOT NULL DEFAULT 0,
    ended_at_utc TEXT NULL,
    final_day INTEGER NULL,
    final_hour INTEGER NULL,
    victories INTEGER NULL,
    losses INTEGER NULL,
    final_player_rank TEXT NULL,
    final_player_rating INTEGER NULL,
    final_player_rating_delta INTEGER NULL,
    reason TEXT NULL,
    build_channel TEXT NULL,
    player_account_id TEXT NULL,
    bundle_screenshot_requested INTEGER NOT NULL DEFAULT 0,
    mod_version TEXT NULL
);
CREATE INDEX idx_battles_ghost_discovery
    ON battles(local_player_account_id, source, recorded_at_utc DESC);
CREATE UNIQUE INDEX idx_battles_ghost_identity
    ON battles(uploader_account_id, remote_battle_id)
    WHERE source = 'GHOST';
CREATE INDEX idx_battles_history_ghost ON battles(local_player_account_id, recorded_at_utc DESC, battle_id DESC) WHERE source = 'GHOST' AND deleted_at_utc IS NULL;
CREATE INDEX idx_battles_history_ghost_day ON battles(local_player_account_id, recorded_at_utc DESC, battle_id DESC) WHERE source = 'GHOST' AND deleted_at_utc IS NULL AND day >= 10;
CREATE INDEX idx_battles_history_ghost_day_outcome ON battles(local_player_account_id, CASE lower(trim(winner_combatant_id,' 	
                 　')) WHEN 'player' THEN 1 WHEN 'opponent' THEN -1 ELSE CASE lower(trim(result,' 	
                 　')) WHEN 'win' THEN 1 WHEN 'won' THEN 1 WHEN 'loss' THEN -1 WHEN 'lost' THEN -1 ELSE 0 END END, recorded_at_utc DESC, battle_id DESC) WHERE source = 'GHOST' AND deleted_at_utc IS NULL AND day >= 10;
CREATE INDEX idx_battles_history_ghost_outcome ON battles(local_player_account_id, CASE lower(trim(winner_combatant_id,' 	
                 　')) WHEN 'player' THEN 1 WHEN 'opponent' THEN -1 ELSE CASE lower(trim(result,' 	
                 　')) WHEN 'win' THEN 1 WHEN 'won' THEN 1 WHEN 'loss' THEN -1 WHEN 'lost' THEN -1 ELSE 0 END END, recorded_at_utc DESC, battle_id DESC) WHERE source = 'GHOST' AND deleted_at_utc IS NULL;
CREATE INDEX idx_battles_history_local ON battles(run_id, recorded_at_utc DESC, battle_id DESC) WHERE source = 'LOCAL';
CREATE INDEX idx_battles_local_player_recent
    ON battles(local_player_account_id, recorded_at_utc DESC);
CREATE INDEX idx_battles_run_id_recorded
    ON battles(run_id, recorded_at_utc DESC);
CREATE INDEX idx_battles_source_recorded
    ON battles(source, recorded_at_utc DESC);
CREATE UNIQUE INDEX idx_bundle_outbox_active_run
    ON bundle_outbox(run_id)
    WHERE status = 'pending';
CREATE INDEX idx_bundle_outbox_due
    ON bundle_outbox(status, next_attempt_at_utc, attempts, sealed_at_utc);
CREATE INDEX idx_bundle_seal_jobs_state
    ON bundle_seal_jobs(state, input_deadline_at_utc, run_id);
CREATE INDEX idx_combat_replay_videos_attachment
    ON combat_replay_videos(
        attachment_state, started_at_utc DESC, video_id
    );
CREATE INDEX idx_combat_replay_videos_battle
    ON combat_replay_videos(battle_id, started_at_utc DESC);
CREATE INDEX idx_run_events_ts_utc
    ON run_events(ts_utc);
CREATE UNIQUE INDEX idx_run_screenshots_primary_run
    ON run_screenshots(run_id)
    WHERE is_primary = 1 AND run_id IS NOT NULL;
CREATE INDEX idx_run_screenshots_run_id_captured_at_utc
    ON run_screenshots(run_id, captured_at_utc DESC);
CREATE INDEX idx_run_screenshots_source_captured
    ON run_screenshots(capture_source, captured_at_utc ASC, screenshot_id ASC);
CREATE INDEX idx_runs_completed_last_seen
    ON runs(completed, last_seen_at_utc DESC);
CREATE INDEX idx_runs_history_hero ON runs(CASE lower(trim(hero,' 	
                 　')) WHEN 'hero8' THEN 'thedragons' ELSE lower(trim(hero,' 	
                 　')) END, COALESCE(ended_at_utc,last_seen_at_utc,started_at_utc) DESC, run_id DESC);
CREATE INDEX idx_runs_history_recent ON runs(COALESCE(ended_at_utc,last_seen_at_utc,started_at_utc) DESC, run_id DESC);
CREATE INDEX idx_runs_started_at_utc
    ON runs(started_at_utc DESC);
CREATE TRIGGER trg_battles_local_payload_insert
BEFORE INSERT ON battles
WHEN NOT (
    (NEW.source = 'LOCAL' AND NEW.local_payload_state IS NOT NULL AND (
        (NEW.local_payload_state = 'ready' AND NEW.has_local_payload = 1)
        OR
        (NEW.local_payload_state IN ('delete_pending', 'evicted', 'missing')
         AND NEW.has_local_payload = 0)
    ))
    OR
    (NEW.source <> 'LOCAL' AND NEW.local_payload_state IS NULL)
)
BEGIN
    SELECT RAISE(ABORT, 'invalid local payload lifecycle state');
END;
CREATE TRIGGER trg_battles_local_payload_update
BEFORE UPDATE OF source, has_local_payload, local_payload_state ON battles
WHEN NOT (
    (NEW.source = 'LOCAL' AND NEW.local_payload_state IS NOT NULL AND (
        (NEW.local_payload_state = 'ready' AND NEW.has_local_payload = 1)
        OR
        (NEW.local_payload_state IN ('delete_pending', 'evicted', 'missing')
         AND NEW.has_local_payload = 0)
    ))
    OR
    (NEW.source <> 'LOCAL' AND NEW.local_payload_state IS NULL)
)
BEGIN
    SELECT RAISE(ABORT, 'invalid local payload lifecycle state');
END;
