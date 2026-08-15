// LEAF MODULE: value-mirrors of generated types, consumed as BOTH useState seeds
// (features/**) and preview command results. MUST NOT import from command clients
// or features/** — that would recreate the runtime import cycle this file avoids.
import type {
  AppBootstrap,
  HistoryRunList,
  InstallState,
  RunDataCleanupPreview,
  RunDataCleanupResult,
  ScreenshotCleanupPreview,
  ScreenshotCleanupResult,
  StreamOverlayCropSettingsPayload,
  StreamServiceStatus
} from '../types/backend';
import bootstrapResource from '../../src-tauri/resources/app-bootstrap.json';

export const emptyInstallState: InstallState = {
  // These falsy fields gate preview-unreachable install/reset commands.
  selected_game_path: null,
  steam_path: null,
  game: {
    found: false,
    path_valid: false,
    display_version: null
  },
  mod_state: {
    installed: false,
    installed_version: null,
    bundled_version: null,
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
  has_resettable_data: false,
  has_bepinex_files: false,
  warnings: [{ code: 'game_missing', params: {} }]
};

export const idleStreamStatus: StreamServiceStatus = {
  // The loopback host and zero active-window offset are deliberate defaults.
  running: false,
  host: '127.0.0.1',
  port: null,
  base_url: null,
  overlay_url: null,
  settings_url: null,
  last_error: null,
  started_at: null,
  active_from: null,
  active_window_offset: 0,
  db: {
    found: false,
    path: null
  },
  window: {
    total_records: 0,
    existing_before_start: 0,
    captured_since_start: 0,
    current_hero: null,
    current_start_label: null
  }
};

export const defaultCropSettings: StreamOverlayCropSettingsPayload = {
  // This is the real preview crop rectangle, not a normalized zero-value.
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
    last_run_at_utc: null,
    win_rate: null
  },
  runs: []
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
