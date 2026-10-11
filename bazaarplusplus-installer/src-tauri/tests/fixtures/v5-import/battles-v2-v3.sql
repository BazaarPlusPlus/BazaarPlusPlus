-- Battles, battle snapshots, and replay videos for the user_version 2 and 3 shapes (both
-- v3 fixtures), which carry the lifecycle columns. Load after rows-common.sql.
-- battles-v1.sql holds the same rows without the lifecycle columns; keep the two in step.
-- Reference clock for the 30-day replay retention window: 2026-10-10T00:00:00Z.

INSERT INTO battles (
    battle_id, remote_battle_id, uploader_account_id, source, run_id, local_player_account_id,
    recorded_at_utc, day, hour, encounter_id, combat_kind, player_name, player_account_id,
    player_hero, opponent_name, opponent_account_id, opponent_hero, result,
    winner_combatant_id, loser_combatant_id, is_final_battle, has_local_payload, bundle_id,
    download_url, download_url_expires_at_ms, ghost_replay_state,
    ghost_replay_unavailable_reason, deleted_at_utc, local_payload_state,
    local_payload_maintenance_at_utc
) VALUES
    -- Local replays inside the retention window (payload files in CombatReplays/).
    ('battle-alpha-1', NULL, NULL, 'LOCAL', 'run-alpha', NULL,
     '2026-10-01T12:10:00.0000000+00:00', 9, 4, 'encounter-alpha-1', 'PVPCombat', 'Player',
     'account-001', 'Vanessa', 'Rival A', 'account-101', 'Pygmalien', 'Win',
     'player', 'opponent', 1, 1, NULL, NULL, NULL, NULL, NULL, NULL, 'ready', NULL),
    ('battle-bravo-1', NULL, NULL, 'LOCAL', 'run-bravo', NULL,
     '2026-10-02T13:10:00.0000000+00:00', 8, 2, 'encounter-bravo-1', 'PVPCombat', 'Player',
     'account-001', 'Dooley', 'Rival B', 'account-102', 'Vanessa', 'Loss',
     'opponent', 'player', 1, 1, NULL, NULL, NULL, NULL, NULL, NULL, 'ready', NULL),
    ('battle-echo-1', NULL, NULL, 'LOCAL', 'run-echo', NULL,
     '2026-10-05T13:10:00.0000000+00:00', 9, 5, 'encounter-echo-1', 'PVPCombat', 'Player',
     'account-001', 'Stelle', 'Rival E', 'account-105', 'Jules', 'Win',
     'player', 'opponent', 1, 1, NULL, NULL, NULL, NULL, NULL, NULL, 'ready', NULL),
    ('battle-foxtrot-1', NULL, NULL, 'LOCAL', 'run-foxtrot', NULL,
     '2026-10-06T13:05:00.0000000+00:00', 3, 1, 'encounter-foxtrot-1', 'PVPCombat', 'Player',
     'account-001', 'Jules', 'Rival F', 'account-106', 'Mak', 'Win',
     'player', 'opponent', 0, 1, NULL, NULL, NULL, NULL, NULL, NULL, 'ready', NULL),
    -- Local replay outside the retention window (its payload file is still on disk).
    ('battle-golf-1', NULL, NULL, 'LOCAL', 'run-golf', NULL,
     '2026-08-01T13:10:00.0000000+00:00', 11, 2, 'encounter-golf-1', 'PVPCombat', 'Player',
     'account-001', 'hero8', 'Rival G', 'account-107', 'Dooley', 'Win',
     'player', 'opponent', 1, 1, NULL, NULL, NULL, NULL, NULL, NULL, 'ready', NULL),
    -- Local battles whose payload is gone.
    ('battle-charlie-1', NULL, NULL, 'LOCAL', 'run-charlie', NULL,
     '2026-10-03T13:10:00.0000000+00:00', 10, 1, 'encounter-charlie-1', 'PVPCombat', 'Player',
     'account-001', 'Pygmalien', 'Rival C', 'account-103', 'Stelle', 'Win',
     'player', 'opponent', 1, 0, NULL, NULL, NULL, NULL, NULL, NULL, 'evicted',
     '2026-10-04T00:00:00.0000000+00:00'),
    ('battle-delta-1', NULL, NULL, 'LOCAL', 'run-delta', NULL,
     '2026-10-04T13:10:00.0000000+00:00', 7, 3, 'encounter-delta-1', 'PVPCombat', 'Player',
     'account-001', 'Mak', 'Rival D', 'account-104', 'Vanessa', 'Loss',
     'opponent', 'player', 1, 0, NULL, NULL, NULL, NULL, NULL, NULL, 'missing', NULL),
    -- Ghost battles, which the import does not bring over: one downloaded (payload in
    -- GhostBattlePayloads/), one only listed remotely.
    ('ghost-local-ready', 'remote-901', 'account-777', 'GHOST', NULL, 'account-001',
     '2026-10-02T09:00:00.0000000+00:00', 12, 0, 'encounter-ghost-1', 'PVPCombat', 'Ghost Owner',
     'account-777', 'Mak', 'Player', 'account-001', 'Vanessa', 'Win',
     'player', 'opponent', 0, 0, 'bundle-remote-901', NULL, NULL, 'local_ready', NULL, NULL,
     NULL, NULL),
    ('ghost-remote-only', 'remote-902', 'account-778', 'GHOST', NULL, 'account-001',
     '2026-10-03T09:00:00.0000000+00:00', 10, 1, 'encounter-ghost-2', 'PVPCombat',
     'Other Owner', 'account-778', 'Jules', 'Player', 'account-001', 'Dooley', 'Loss',
     'opponent', 'player', 0, 0, 'bundle-remote-902',
     'https://example.invalid/ghost/remote-902', 1791763200000, 'remote_available', NULL, NULL,
     NULL, NULL);

INSERT INTO battle_snapshots (
    battle_id, player_hand_json, player_skills_json, opponent_hand_json, opponent_skills_json
) VALUES
    ('battle-alpha-1', '[{"template_id":"item-1","tier":"Gold"}]', '[]',
     '[{"template_id":"item-2","tier":"Silver"}]', '[]');

INSERT INTO combat_replay_videos (
    video_id, battle_id, source, video_relative_path, width, height, fps, codec, crf, preset,
    started_at_utc, ended_at_utc, duration_ms, captured_frames, dropped_frames,
    file_size_bytes, status, error, attachment_state, file_state, detached_at_utc,
    missing_at_utc, last_reconciled_at_utc
) VALUES
    ('video-alpha-1', 'battle-alpha-1', 'LocalSaved',
     'battle-alpha-1.20261001-201000.rec0001.mp4', 1920, 1080, 60, 'h264', 23, 'p4',
     '2026-10-01T12:10:00.0000000+00:00', '2026-10-01T12:11:30.0000000+00:00', 90000, 5400, 0,
     36, 'COMPLETED', NULL, 'attached', 'present', NULL, NULL,
     '2026-10-01T12:20:00.0000000+00:00'),
    -- Same file as video-alpha-1.
    ('video-bravo-1', 'battle-bravo-1', 'LocalSaved',
     'battle-alpha-1.20261001-201000.rec0001.mp4', 1920, 1080, 60, 'h264', 23, 'p4',
     '2026-10-02T13:10:00.0000000+00:00', '2026-10-02T13:11:30.0000000+00:00', 90000, 5400, 0,
     36, 'COMPLETED', NULL, 'attached', 'present', NULL, NULL,
     '2026-10-02T13:20:00.0000000+00:00'),
    ('video-charlie-1', 'battle-charlie-1', 'LocalSaved',
     '{{V5_ROOT}}/CombatReplayVideos/battle-charlie-1.20261003-211000.rec0003.mp4',
     1920, 1080, 60, 'h264', 23, 'p4',
     '2026-10-03T13:10:00.0000000+00:00', '2026-10-03T13:11:30.0000000+00:00', 90000, 5400, 0,
     36, 'COMPLETED', NULL, 'attached', 'present', NULL, NULL,
     '2026-10-03T13:20:00.0000000+00:00'),
    ('video-delta-1', 'battle-delta-1', 'LocalSaved',
     '{{OUTSIDE_ROOT}}/Videos/battle-delta-1.20261004-211000.rec0004.mp4',
     1920, 1080, 60, 'h264', 23, 'p4',
     '2026-10-04T13:10:00.0000000+00:00', '2026-10-04T13:11:30.0000000+00:00', 90000, 5400, 0,
     36, 'COMPLETED', NULL, 'attached', 'present', NULL, NULL,
     '2026-10-04T13:20:00.0000000+00:00');
