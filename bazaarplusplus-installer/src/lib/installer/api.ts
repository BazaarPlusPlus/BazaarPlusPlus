import { call } from '../bridge/commands.ts';
import type {
  EnvironmentInfo,
  GameRunningInfo,
  InstallerContextPayload,
  LegacyRecordDirectoryInfo,
  LaunchOptionsPatchResult,
  SteamRunningInfo
} from '$lib/types';

export async function verifyGamePath(path: string) {
  return call('verify_game_path', { path });
}

export async function initializeInstallerContext() {
  return call('initialize_installer_context');
}

export async function detectEnvironment(gamePath?: string) {
  return gamePath
    ? call('detect_environment', { gamePath })
    : call('detect_environment');
}

export async function detectSteamRunning() {
  return call('detect_steam_running');
}

export async function closeSteam() {
  return call('close_steam');
}

export async function detectBazaarRunning() {
  return call('detect_bazaar_running');
}

export async function installBepinex(
  steamPath: string,
  gamePath: string,
  skipSteamShutdown = false
) {
  return call('install_bepinex', { steamPath, gamePath, skipSteamShutdown });
}

export async function uninstallBpp(steamPath: string, gamePath: string) {
  return call('uninstall_bpp', { steamPath, gamePath });
}

export async function repairBpp(gamePath: string) {
  return call('repair_bpp', { gamePath });
}

export async function getLegacyRecordDirectoryInfo(gamePath: string) {
  return call('get_legacy_record_directory_info', {
    gamePath
  });
}

export async function patchLaunchOptions(
  steamPath: string,
  gamePath: string,
  skipSteamShutdown = false
) {
  return call('patch_launch_options', {
    steamPath,
    gamePath,
    skipSteamShutdown
  });
}
