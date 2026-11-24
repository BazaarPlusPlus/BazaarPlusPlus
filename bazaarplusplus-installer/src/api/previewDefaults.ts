// Keep imports type-only to avoid a runtime cycle between command adapters and feature state.
import type {
  AppBootstrap,
  HistoryRunList,
  InstallState,
  LegacyDataPresence,
  LegacyDataState,
  RunDataCleanupPreview,
  RunDataCleanupResult,
  ScreenshotCleanupPreview,
  ScreenshotCleanupResult,
  StreamOverlayCropSettingsPayload,
  StreamServiceStatus
} from '../types/backend';
import bootstrapResource from '../../src-tauri/resources/app-bootstrap.json';

export const emptyInstallState: InstallState = {
  selected_game_path: null,
  game: {
    path_valid: false
  },
  mod_state: {
    installed: false,
    ready: false
  },
  actions: {
    can_install: false,
    can_reinstall: false,
    can_reset_data: false,
    can_reset_bepinex: false,
    can_uninstall: false,
    can_launch: false
  },
  data_root_path: null,
  has_resettable_data: false,
  has_bepinex_files: false,
  warnings: [{ code: 'game_missing', params: {} }]
};

export const emptyLegacyDataState: LegacyDataState = {
  game_path: null,
  roots: [],
  installed_mod_data_root: 'reinstall_required'
};

export const emptyLegacyDataPresence: LegacyDataPresence = {
  game_path: null,
  roots: [],
  installed_mod_data_root: 'reinstall_required'
};

export const idleStreamStatus: StreamServiceStatus = {
  running: false,
  host: '127.0.0.1',
  port: null,
  overlay_url: null,
  settings_url: null,
  last_error: null,
  started_at: null,
  active_from: null,
  active_window_offset: 0,
  db: {
    found: false
  }
};

export const defaultCropSettings: StreamOverlayCropSettingsPayload = {
  crop: {
    left: 0.342,
    top: 0.313,
    width: 0.58,
    height: 0.22
  },
  code: '',
  display_mode: 'current'
};

export const emptyHistoryRunList: HistoryRunList = {
  summary: {
    runs: 0,
    videos: 0,
    win_rate: null
  },
  runs: [],
  heroes: [],
  key_cards: []
};

export const emptyScreenshotCleanupPreview: ScreenshotCleanupPreview = {
  screenshots: 0,
  orphan_files: 0,
  estimated_bytes: 0,
  skipped_pending_uploads: 0
};

export const emptyRunDataCleanupPreview: RunDataCleanupPreview = {
  runs: 0,
  battles: 0,
  videos: 0,
  estimated_bytes: 0,
  skipped_pending_uploads: 0
};

export const emptyScreenshotCleanupResult: ScreenshotCleanupResult = {
  deleted_rows: 0,
  deleted_files: 0,
  freed_bytes: 0,
  skipped_pending_uploads: 0
};

export const emptyRunDataCleanupResult: RunDataCleanupResult = {
  deleted_runs: 0,
  deleted_files: 0,
  freed_bytes: 0,
  skipped_pending_uploads: 0
};

export const fallbackBootstrap: AppBootstrap = {
  ...(bootstrapResource as Pick<
    AppBootstrap,
    'links' | 'credits' | 'licenses'
  >),
  app_version: __FRONTEND_VERSION__,
  bundled_bpp_version: null
};
