// Seed data for the shell snapshot anchor (src/shell.snapshot.test.tsx).
// Every value is fixed: no clock reads, no build version, no host paths.
import {
  emptyHistoryRunList,
  emptyInstallState,
  fallbackBootstrap,
  idleStreamStatus
} from '../../api/previewDefaults';
import type {
  AppBootstrap,
  HistoryRunDetail,
  HistoryRunList,
  HistoryRunRow,
  InstallState,
  LegacyDataState,
  SemanticProblem,
  StreamServiceStatus
} from '../../types/backend';

/** Replaces `__FRONTEND_VERSION__` so a version bump does not churn snapshots. */
export const bootstrap: AppBootstrap = {
  ...fallbackBootstrap,
  app_version: '1.2.3',
  bundled_bpp_version: '1.2.3'
};

export const gameMissing: InstallState = emptyInstallState;

export const installable: InstallState = {
  selected_game_path: '/Games/The Bazaar',
  steam_path: '/Games/Steam',
  game: { path_valid: true },
  mod_state: { installed: false, ready: false },
  actions: {
    can_install: true,
    can_reinstall: false,
    can_reset_data: false,
    can_reset_bepinex: false,
    can_uninstall: false,
    can_launch: true
  },
  has_resettable_data: false,
  has_bepinex_files: false,
  warnings: []
};

export const installed: InstallState = {
  ...installable,
  mod_state: { installed: true, ready: true },
  actions: {
    can_install: false,
    can_reinstall: true,
    can_reset_data: true,
    can_reset_bepinex: true,
    can_uninstall: true,
    can_launch: true
  },
  has_resettable_data: true,
  has_bepinex_files: true
};

/** An upgraded 5.x user who has not reinstalled yet: three Legacy Roots, one
 * sharing files by hard link, and a mod that still writes an old root. */
export const legacyRootsNeedingReinstall: LegacyDataState = {
  game_path: '/Games/The Bazaar',
  roots: [
    {
      name: 'BazaarPlusPlus',
      size_bytes: 524_288,
      file_count: 3,
      hard_linked_file_count: 0,
      unreadable_entry_count: 0
    },
    {
      name: 'BazaarPlusPlusV4',
      size_bytes: 12_582_912,
      file_count: 40,
      hard_linked_file_count: 0,
      unreadable_entry_count: 0
    },
    {
      name: 'BazaarPlusPlusV5',
      size_bytes: 1_610_612_736,
      file_count: 1200,
      hard_linked_file_count: 12,
      unreadable_entry_count: 1
    }
  ],
  installed_mod_data_root: 'reinstall_required',
  v5_import_eligible: false
};

export const installDetectionFailed: SemanticProblem = {
  code: 'install_detection_failed',
  params: { operation: 'detect_state' },
  diagnostic: 'fixture: Steam library unreadable'
};

const heroes = ['Vanessa', 'Pygmalien', 'Dooley', 'Mak', 'Stelle', 'Jules'];

function run(index: number): HistoryRunRow {
  const won = index % 3 !== 0;
  return {
    run_id: `run-${index}`,
    hero: heroes[index % heroes.length],
    game_mode: index % 4 === 0 ? 'Normal' : 'Ranked',
    started_at_utc: '2026-09-12T10:00:00Z',
    ended_at_utc: '2026-09-12T10:42:30Z',
    result: won ? 'win' : 'loss',
    victories: won ? 10 : 4,
    losses: won ? 2 : 5,
    final_day: won ? 12 : 8,
    final_player_rank: index % 2 === 0 ? 'Gold' : null,
    final_player_rating: index % 2 === 0 ? 1500 + index : null,
    screenshot_id: null,
    thumbnail_url: null
  };
}

/** 120 runs: the first page of a three-page list. */
export const historyFirstPage: HistoryRunList = {
  summary: { runs: 120, videos: 7, win_rate: 0.67 },
  runs: Array.from({ length: 50 }, (_, index) => run(index + 1))
};

export const historyEmpty: HistoryRunList = emptyHistoryRunList;

export const historyReadFailed: SemanticProblem = {
  code: 'history_read_failed',
  params: { operation: 'list_runs' },
  diagnostic: 'fixture: database is locked'
};

export const runDetail: HistoryRunDetail = {
  run: {
    run_id: 'run-1',
    hero: 'Vanessa',
    game_mode: 'Ranked',
    started_at_utc: '2026-09-12T10:00:00Z',
    ended_at_utc: '2026-09-12T10:42:30Z',
    status: 'completed',
    result: 'win',
    victories: 10,
    losses: 2,
    final_day: 12,
    final_player_rank: 'Gold',
    final_player_rating: 1520,
    screenshot_id: null,
    player_name: 'Fixture Player'
  },
  battles: [
    {
      battle_id: 'battle-1',
      day: 1,
      hour: 2,
      result: 'win',
      opponent_hero: 'Pygmalien',
      opponent_name: 'Opponent A',
      opponent_rank: 'Silver',
      opponent_rating: 1400,
      video: {
        video_id: 'video-1',
        status: 'completed',
        file_size_bytes: 52_428_800,
        duration_ms: 95_000
      }
    },
    {
      battle_id: 'battle-2',
      day: 2,
      hour: 4,
      result: 'loss',
      opponent_hero: 'Dooley',
      opponent_name: null,
      opponent_rank: null,
      opponent_rating: null,
      video: null
    }
  ]
};

export const runDetailReadFailed: SemanticProblem = {
  code: 'history_read_failed',
  params: { operation: 'get_run_detail' },
  diagnostic: 'fixture: database is locked'
};

export const streamIdle: StreamServiceStatus = idleStreamStatus;

export const streamRunning: StreamServiceStatus = {
  ...idleStreamStatus,
  running: true,
  port: 17654,
  base_url: 'http://127.0.0.1:17654',
  overlay_url: 'http://127.0.0.1:17654/overlay',
  settings_url: 'http://127.0.0.1:17654/settings',
  started_at: '2026-09-12T10:00:00Z',
  active_from: '2026-09-12T10:00:00Z',
  db: { found: true }
};

export const streamError: StreamServiceStatus = {
  ...idleStreamStatus,
  last_error: 'fixture: address already in use'
};

export const availableUpdate = {
  version: '9.9.0',
  body: 'Fixture release notes.',
  rawJson: {}
};
