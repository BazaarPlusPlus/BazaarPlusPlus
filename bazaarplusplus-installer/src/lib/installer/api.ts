import { invoke } from '@tauri-apps/api/core';
import type {
  DotnetInfo,
  EnvironmentInfo,
  GameRunningInfo,
  LegacyRecordDirectoryInfo,
  LaunchOptionsPatchResult,
  SteamRunningInfo,
  SupportersResponse
} from '$lib/types';

export async function verifyGamePath(path: string) {
  return invoke<boolean>('verify_game_path', { path });
}

export async function detectDotnetRuntime() {
  return invoke<DotnetInfo>('detect_dotnet_runtime');
}

export async function detectEnvironment(gamePath?: string) {
  return gamePath
    ? invoke<EnvironmentInfo>('detect_environment', { gamePath })
    : invoke<EnvironmentInfo>('detect_environment');
}

export async function detectSteamRunning() {
  return invoke<SteamRunningInfo>('detect_steam_running');
}

export async function detectBazaarRunning() {
  return invoke<GameRunningInfo>('detect_bazaar_running');
}

export async function installBepinex(steamPath: string, gamePath: string) {
  return invoke('install_bepinex', { steamPath, gamePath });
}

export async function uninstallBpp(steamPath: string, gamePath: string) {
  return invoke('uninstall_bpp', { steamPath, gamePath });
}

export async function repairBpp(gamePath: string) {
  return invoke('repair_bpp', { gamePath });
}

export async function getLegacyRecordDirectoryInfo(gamePath: string) {
  return invoke<LegacyRecordDirectoryInfo>('get_legacy_record_directory_info', { gamePath });
}

export async function patchLaunchOptions(steamPath: string, gamePath: string) {
  return invoke<LaunchOptionsPatchResult>('patch_launch_options', {
    steamPath,
    gamePath
  });
}

export async function loadSupporters() {
  return invoke<SupportersResponse>('load_supporters');
}
