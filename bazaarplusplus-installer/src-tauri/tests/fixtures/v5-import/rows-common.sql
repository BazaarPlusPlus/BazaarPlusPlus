-- Seed rows whose columns are the same in every V5 shape (user_version 1, 2, and 3).
-- Load after a *.schema.sql and before the battles-*.sql that matches its shape.
-- {{V5_ROOT}} and {{OUTSIDE_ROOT}} are replaced with absolute directories before loading;
-- see README.md. Reference clock for the replay retention window: 2026-10-10T00:00:00Z.

INSERT INTO runs (
    run_id, started_at_utc, last_seen_at_utc, status, completed, hero, game_mode,
    player_rank, player_rating, day, hour, last_seq, ended_at_utc, final_day, final_hour,
    victories, losses, final_player_rank, final_player_rating, final_player_rating_delta,
    reason, build_channel, player_account_id, bundle_screenshot_requested, mod_version
) VALUES
    -- Shares its end-of-run screenshot file and a replay video file with run-bravo.
    -- Its Bundle was uploaded.
    ('run-alpha', '2026-10-01T12:00:00.0000000+00:00', '2026-10-01T12:15:04.0000000+00:00',
     'completed', 1, 'Vanessa', 'Ranked', 'Gold 2', 1420, 9, 4, 3,
     '2026-10-01T12:15:04.0000000+00:00', 9, 4, 10, 3, 'Gold 1', 1436, 16,
     'run_state_exit', 'Online', 'account-001', 1, '5.6.0'),
    -- Points at run-alpha's screenshot and video files. Its Bundle is pending in BundleOutbox/.
    ('run-bravo', '2026-10-02T13:00:00.0000000+00:00', '2026-10-02T13:20:00.0000000+00:00',
     'completed', 1, 'Dooley', 'Ranked', 'Gold 1', 1436, 8, 2, 2,
     '2026-10-02T13:20:00.0000000+00:00', 8, 2, 7, 3, 'Gold 1', 1430, -6,
     'run_state_exit', 'Online', 'account-001', 1, '5.6.0'),
    -- Absolute screenshot and video paths inside the V5 Data Root.
    ('run-charlie', '2026-10-03T13:00:00.0000000+00:00', '2026-10-03T13:21:00.0000000+00:00',
     'completed', 1, 'Pygmalien', 'Ranked', 'Gold 1', 1430, 10, 1, 2,
     '2026-10-03T13:21:00.0000000+00:00', 10, 1, 10, 2, 'Gold 1', 1452, 22,
     'run_state_exit', 'Online', 'account-001', 1, '5.6.0'),
    -- Absolute screenshot and video paths outside the V5 Data Root.
    ('run-delta', '2026-10-04T13:00:00.0000000+00:00', '2026-10-04T13:19:00.0000000+00:00',
     'completed', 1, 'Mak', 'Ranked', 'Gold 1', 1452, 7, 3, 2,
     '2026-10-04T13:19:00.0000000+00:00', 7, 3, 4, 3, 'Gold 1', 1440, -12,
     'run_state_exit', 'Online', 'account-001', 1, '5.6.0'),
    -- run-echo and its collision id (RunLogRunIdentity.CreateCollisionId appends
    -- ':bpp:' and 32 hex digits when a game run id is reused).
    ('run-echo', '2026-10-05T13:00:00.0000000+00:00', '2026-10-05T13:18:00.0000000+00:00',
     'completed', 1, 'Stelle', 'Ranked', 'Gold 1', 1440, 9, 5, 2,
     '2026-10-05T13:18:00.0000000+00:00', 9, 5, 8, 3, 'Gold 1', 1446, 6,
     'run_state_exit', 'Online', 'account-001', 1, '5.7.0'),
    ('run-echo:bpp:0f1e2d3c4b5a69788796a5b4c3d2e1f0', '2026-10-05T14:00:00.0000000+00:00',
     '2026-10-05T14:16:00.0000000+00:00', 'abandoned', 1, 'Stelle', 'Ranked', 'Gold 1', 1446,
     4, 2, 1, '2026-10-05T14:16:00.0000000+00:00', 4, 2, NULL, NULL, NULL, NULL, NULL,
     'session_mismatch', 'Online', 'account-001', 1, '5.7.0'),
    -- Not finished: completed = 0, never ended.
    ('run-foxtrot', '2026-10-06T13:00:00.0000000+00:00', '2026-10-06T13:09:00.0000000+00:00',
     'active', 0, 'Jules', 'Ranked', 'Gold 1', 1446, 3, 1, 1,
     NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
     NULL, 'Online', 'account-001', 0, '5.7.0'),
    -- The internal hero id 'hero8' (History shows it as The Dragons); its replay is older than
    -- the 30-day V6 replay retention window.
    ('run-golf', '2026-08-01T13:00:00.0000000+00:00', '2026-08-01T13:25:00.0000000+00:00',
     'completed', 1, 'hero8', 'Ranked', 'Silver 1', 1210, 11, 2, 3,
     '2026-08-01T13:25:00.0000000+00:00', 11, 2, 10, 1, 'Silver 1', 1236, 26,
     'run_state_exit', 'Online', 'account-001', 1, '5.5.0');

INSERT INTO run_events (run_id, seq, ts_utc, kind, payload_json) VALUES
    ('run-alpha', 1, '2026-10-01T12:00:00.0000000+00:00', 'run_started',
     '{"schema_version":3,"run_id":"run-alpha","seq":1,"ts":"2026-10-01T12:00:00.000+00:00","kind":"run_started","day":1,"hour":0,"hero":"Vanessa","game_mode":"Ranked"}'),
    ('run-alpha', 2, '2026-10-01T12:10:00.0000000+00:00', 'pvp_combat_recorded',
     '{"schema_version":3,"run_id":"run-alpha","seq":2,"ts":"2026-10-01T12:10:00.000+00:00","kind":"pvp_combat_recorded","day":9,"hour":4,"combat_kind":"PVPCombat","battle_id":"battle-alpha-1"}'),
    ('run-alpha', 3, '2026-10-01T12:15:04.0000000+00:00', 'run_completed',
     '{"schema_version":3,"run_id":"run-alpha","seq":3,"ts":"2026-10-01T12:15:04.000+00:00","kind":"run_completed","victories":10,"losses":3}'),
    ('run-foxtrot', 1, '2026-10-06T13:00:00.0000000+00:00', 'run_started',
     '{"schema_version":3,"run_id":"run-foxtrot","seq":1,"ts":"2026-10-06T13:00:00.000+00:00","kind":"run_started","day":1,"hour":0,"hero":"Jules","game_mode":"Ranked"}'),
    ('run-golf', 1, '2026-08-01T13:00:00.0000000+00:00', 'run_started',
     '{"schema_version":3,"run_id":"run-golf","seq":1,"ts":"2026-08-01T13:00:00.000+00:00","kind":"run_started","day":1,"hour":0,"hero":"hero8","game_mode":"Ranked"}');

INSERT INTO run_screenshots (
    screenshot_id, run_id, hero_name, battle_id, capture_source, is_primary,
    image_relative_path, captured_at_local, captured_at_utc, day, player_rank, player_rating,
    player_position, victories_at_capture, build_channel
) VALUES
    ('shot-alpha', 'run-alpha', 'Vanessa', NULL, 'end_of_run_auto', 1,
     '2026-10-01/2026-10-01_20-15-04-120_final_run-run-alpha.png',
     '2026-10-01T20:15:04.1200000+08:00', '2026-10-01T12:15:04.1200000+00:00',
     9, 'Gold 1', 1436, 1, 10, 'Online'),
    -- Same file as shot-alpha.
    ('shot-bravo', 'run-bravo', 'Dooley', NULL, 'end_of_run_auto', 1,
     '2026-10-01/2026-10-01_20-15-04-120_final_run-run-alpha.png',
     '2026-10-02T21:20:00.0000000+08:00', '2026-10-02T13:20:00.0000000+00:00',
     8, 'Gold 1', 1430, 2, 7, 'Online'),
    ('shot-charlie', 'run-charlie', 'Pygmalien', NULL, 'end_of_run_auto', 1,
     '{{V5_ROOT}}/Screenshots/2026-10-03/2026-10-03_21-21-00-000_final_run-run-charlie.png',
     '2026-10-03T21:21:00.0000000+08:00', '2026-10-03T13:21:00.0000000+00:00',
     10, 'Gold 1', 1452, 1, 10, 'Online'),
    ('shot-delta', 'run-delta', 'Mak', NULL, 'end_of_run_auto', 1,
     '{{OUTSIDE_ROOT}}/Pictures/2026-10-04_21-19-00-000_final_run-run-delta.png',
     '2026-10-04T21:19:00.0000000+08:00', '2026-10-04T13:19:00.0000000+00:00',
     7, 'Gold 1', 1440, 3, 4, 'Online'),
    -- A Windows relative path, as Path.Combine writes it there.
    ('shot-echo', 'run-echo', 'Stelle', NULL, 'end_of_run_auto', 1,
     '2026-10-05\2026-10-05_21-18-00-000_final_run-run-echo.png',
     '2026-10-05T21:18:00.0000000+08:00', '2026-10-05T13:18:00.0000000+00:00',
     9, 'Gold 1', 1446, 1, 8, 'Online');

-- input_deadline_at_utc: run-echo carries the ISO "o" text FailOutboxAndScheduleReseal used to
-- write (NormalizeSealJobDeadlines rewrites it on the mod's next open); the collision run carries
-- the SQLite datetime() text written today.
INSERT INTO bundle_seal_jobs (
    run_id, state, player_account_id, screenshot_requested, screenshot_state,
    input_deadline_at_utc, bundle_id, created_at_ms, attempts, last_attempt_at_utc,
    last_error_code, last_error_detail
) VALUES
    ('run-echo', 'waiting', 'account-001', 1, 'available',
     '2026-10-05T13:20:00.0000000+00:00', NULL, NULL, 1, '2026-10-05T13:19:00.0000000+00:00',
     'bundle_file_invalid', 'outbox file failed validation'),
    ('run-echo:bpp:0f1e2d3c4b5a69788796a5b4c3d2e1f0', 'terminal_failure', 'account-001', 1,
     'timed_out', '2026-10-05 14:18:00', NULL, NULL, 3, '2026-10-05T14:20:00.0000000+00:00',
     'bundle_build_failed', 'run has no recorded battles');

INSERT INTO bundle_outbox (
    bundle_id, run_id, file_name, content_sha256_hex, content_digest, total_bytes,
    has_screenshot, sealed_at_utc, status, attempts, last_attempt_at_utc, next_attempt_at_utc,
    failed_at_utc, last_error_code, last_error_detail, server_request_id, server_outcome,
    uploaded_at_utc
) VALUES
    ('bundle-alpha', 'run-alpha', 'bundle-alpha.bundle',
     'a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1',
     'sha256:a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1', 4096, 1,
     '2026-10-01T12:16:00.0000000+00:00', 'uploaded', 1, '2026-10-01T12:16:30.0000000+00:00',
     NULL, NULL, NULL, NULL, 'req-alpha', 'accepted', '2026-10-01T12:16:31.0000000+00:00'),
    ('bundle-bravo', 'run-bravo', 'bundle-bravo.bundle',
     'b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2',
     'sha256:b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2', 4096, 1,
     '2026-10-02T13:21:00.0000000+00:00', 'pending', 2, '2026-10-02T13:30:00.0000000+00:00',
     '2026-10-02T14:30:00.0000000+00:00', NULL, 'network_error', 'connection reset', NULL,
     NULL, NULL);
