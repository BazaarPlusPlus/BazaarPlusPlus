//! The local history database shape the installer reads, for tests that need a
//! real database: the history service tests and the IPC and HTTP goldens share
//! this one copy. #159 replaces its body with the mod's schema golden.

pub(crate) fn create_history_schema(conn: &rusqlite::Connection) {
    conn.execute_batch(
        "
        pragma foreign_keys = on;
        pragma user_version = 1;
        create table runs (
            run_id text primary key,
            started_at_utc text not null,
            last_seen_at_utc text not null,
            status text not null,
            completed integer not null default 0,
            hero text not null,
            game_mode text not null,
            ended_at_utc text null,
            final_day integer null,
            final_hour integer null,
            victories integer null,
            losses integer null,
            final_player_rank text null,
            final_player_rating integer null,
            final_player_rating_delta integer null,
            build_channel text null,
            player_account_id text null,
            bundle_screenshot_requested integer not null default 0,
            mod_version text null
        );
        create table run_events (
            run_id text not null,
            seq integer not null,
            ts_utc text not null,
            kind text not null,
            payload_json text not null,
            primary key (run_id, seq),
            foreign key (run_id) references runs(run_id) on delete cascade
        );
        create table battles (
            battle_id text primary key,
            source text not null,
            run_id text null,
            recorded_at_utc text not null,
            combat_kind text not null default 'PVP',
            day integer null,
            hour integer null,
            player_name text null,
            player_hero text null,
            opponent_hero text null,
            opponent_name text null,
            opponent_rank text null,
            opponent_rating integer null,
            result text null,
            deleted_at_utc text null,
            foreign key (run_id) references runs(run_id) on delete cascade
        );
        create table battle_snapshots (
            battle_id text primary key,
            player_hand_json text not null,
            player_skills_json text not null,
            opponent_hand_json text not null,
            opponent_skills_json text not null,
            foreign key (battle_id) references battles(battle_id) on delete cascade
        );
        create table run_screenshots (
            screenshot_id text primary key,
            run_id text null,
            hero_name text null,
            battle_id text null,
            capture_source text not null,
            is_primary integer not null default 0,
            image_relative_path text not null,
            captured_at_utc text not null,
            captured_at_local text not null,
            day integer null,
            player_rank text null,
            player_rating integer null,
            player_position integer null,
            victories_at_capture integer null,
            build_channel text null
        );
        create table combat_replay_videos (
            video_id text primary key,
            battle_id text not null,
            source text not null default 'GAME_CAPTURE',
            video_relative_path text not null,
            width integer not null default 1920,
            height integer not null default 1080,
            fps integer not null default 60,
            codec text not null default 'h264',
            started_at_utc text not null,
            duration_ms integer null,
            file_size_bytes integer null,
            status text not null
        );
        create table bundle_seal_jobs (
            run_id text primary key,
            state text not null default 'waiting',
            player_account_id text null,
            screenshot_requested integer not null,
            screenshot_state text not null default 'waiting',
            input_deadline_at_utc text not null,
            bundle_id text null unique,
            created_at_ms integer null,
            attempts integer not null default 0,
            last_attempt_at_utc text null,
            last_error_code text null,
            last_error_detail text null,
            foreign key (run_id) references runs(run_id) on delete cascade,
            check (state in ('waiting', 'sealing', 'terminal_failure')),
            check (screenshot_state in ('not_requested', 'waiting', 'available', 'unavailable', 'timed_out'))
        );
        create table bundle_outbox (
            bundle_id text primary key,
            run_id text not null,
            file_name text not null unique,
            content_sha256_hex text not null,
            content_digest text not null,
            total_bytes integer not null,
            has_screenshot integer not null,
            sealed_at_utc text not null,
            status text not null default 'pending',
            attempts integer not null default 0,
            last_attempt_at_utc text null,
            next_attempt_at_utc text null,
            failed_at_utc text null,
            last_error_code text null,
            last_error_detail text null,
            server_request_id text null,
            server_outcome text null,
            uploaded_at_utc text null,
            check (status in ('pending', 'uploaded', 'permanent_failure'))
        );
        ",
    )
    .unwrap();
}
