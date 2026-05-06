import { invoke } from '@tauri-apps/api/core';
import type { Locale } from '../i18n.ts';
import type {
  EnvironmentInfo,
  GameRunningInfo,
  InstallerContextPayload,
  LegacyRecordDirectoryInfo,
  LaunchOptionsPatchResult,
  SteamRunningInfo,
  StreamDbPathInfo,
  StreamOverlayCropSettings,
  StreamOverlayCropSettingsPayload,
  StreamOverlayDisplayMode,
  StreamRecordSummary,
  StreamServiceStatus
} from '../types.ts';

export interface TauriCommandMap {
  verify_game_path: { input: { path: string }; output: boolean };
  initialize_installer_context: {
    input: undefined;
    output: InstallerContextPayload;
  };
  detect_environment: {
    input: { gamePath: string } | undefined;
    output: EnvironmentInfo;
  };
  detect_steam_running: { input: undefined; output: SteamRunningInfo };
  close_steam: { input: undefined; output: void };
  detect_bazaar_running: { input: undefined; output: GameRunningInfo };
  install_bepinex: {
    input: { steamPath: string; gamePath: string; skipSteamShutdown: boolean };
    output: void;
  };
  uninstall_bpp: {
    input: { steamPath: string; gamePath: string };
    output: void;
  };
  repair_bpp: { input: { gamePath: string }; output: void };
  get_legacy_record_directory_info: {
    input: { gamePath: string };
    output: LegacyRecordDirectoryInfo;
  };
  patch_launch_options: {
    input: { steamPath: string; gamePath: string; skipSteamShutdown: boolean };
    output: LaunchOptionsPatchResult;
  };
  get_stream_service_status: {
    input: undefined;
    output: StreamServiceStatus;
  };
  start_stream_service: {
    input: { gamePath?: string };
    output: StreamServiceStatus;
  };
  stop_stream_service: { input: undefined; output: StreamServiceStatus };
  set_tray_locale: { input: { locale: Locale }; output: void };
  set_stream_overlay_window_offset: {
    input: { gamePath?: string; offset: number };
    output: StreamServiceStatus;
  };
  list_stream_overlay_records: {
    input: { gamePath?: string; limit?: number };
    output: StreamRecordSummary[];
  };
  reveal_stream_record_image: {
    input: { gamePath?: string; recordId: string };
    output: void;
  };
  delete_stream_record: {
    input: { gamePath?: string; recordId: string };
    output: void;
  };
  load_stream_record_strip_preview: {
    input: { gamePath?: string; recordId: string };
    output: string | null;
  };
  load_stream_record_strip_previews: {
    input: { gamePath?: string; recordIds: string[] };
    output: Record<string, string>;
  };
  get_stream_overlay_crop_settings: {
    input: undefined;
    output: StreamOverlayCropSettingsPayload;
  };
  save_stream_overlay_crop_settings: {
    input: { crop: StreamOverlayCropSettings };
    output: StreamOverlayCropSettingsPayload;
  };
  import_stream_overlay_crop_code: {
    input: { code: string };
    output: StreamOverlayCropSettingsPayload;
  };
  save_stream_overlay_display_mode: {
    input: { displayMode: StreamOverlayDisplayMode };
    output: StreamOverlayCropSettingsPayload;
  };
  detect_stream_db_path: {
    input: { gamePath?: string };
    output: StreamDbPathInfo;
  };
}

type CommandName = keyof TauriCommandMap;
type CommandInput<K extends CommandName> = TauriCommandMap[K]['input'];
type CommandOutput<K extends CommandName> = TauriCommandMap[K]['output'];
type CommandArgs<K extends CommandName> =
  undefined extends CommandInput<K>
    ? [payload?: Exclude<CommandInput<K>, undefined>]
    : [payload: CommandInput<K>];

export async function call<K extends CommandName>(
  name: K,
  ...args: CommandArgs<K>
): Promise<CommandOutput<K>> {
  const payload = args[0];

  if (payload === undefined) {
    return invoke<CommandOutput<K>>(name);
  }

  return invoke<CommandOutput<K>>(name, payload as Record<string, unknown>);
}
