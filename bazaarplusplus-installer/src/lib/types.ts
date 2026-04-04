export interface EnvironmentInfo {
  steam_path: string | null;
  steam_launch_options_supported: boolean;
  game_path: string | null;
  dotnet_version: string | null;
  dotnet_ok: boolean;
  bepinex_installed: boolean;
  bpp_version: string | null;
  bundled_bpp_version: string | null;
}

export interface DotnetInfo {
  dotnet_version: string | null;
  dotnet_ok: boolean;
}

export interface LaunchOptionsPatchResult {
  verified: boolean;
}

export interface SteamRunningInfo {
  running: boolean;
}

export interface GameRunningInfo {
  running: boolean;
}

export interface LegacyRecordDirectoryInfo {
  total_bytes: number;
}

export type SupporterTierId = 1 | 2 | 3 | 4;

export interface SupporterEntry {
  name: string;
  tier: SupporterTierId;
}

export type SupportersSource = 'bundled' | 'cache' | 'remote';

export interface SupportersResponse {
  entries: SupporterEntry[];
  source: SupportersSource;
  fetchedAt: number | null;
  stale: boolean;
}
