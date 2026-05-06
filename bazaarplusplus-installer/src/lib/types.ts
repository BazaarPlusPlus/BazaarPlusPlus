export type {
  DotnetInfo,
  EnvironmentInfo,
  GameRunningInfo,
  InstallerContextPayload,
  LaunchOptionsPatchResult,
  LegacyRecordDirectoryInfo,
  SteamRunningInfo,
  StreamDbPathInfo,
  StreamOverlayCropSettings,
  StreamOverlayCropSettingsPayload,
  StreamOverlayDisplayMode,
  StreamRecordSummary,
  StreamServiceStatus
} from './generated/commands';

export interface StreamRecordWindowSummary {
  total: number;
  existing_before_start: number;
  captured_since_start: number;
}
