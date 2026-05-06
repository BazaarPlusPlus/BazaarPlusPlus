import type { EnvironmentInfo } from '../../types.ts';
import {
  createPageState,
  selectCustomGamePath,
  type ActionBusy,
  type PageState
} from '../state.ts';

export interface InstallGatesSelection {
  selectedPath: string | null;
  modInstalled: boolean;
  bundledBppVersion: string | null;
  installedBppVersion: string | null;
  pageState: PageState;
  hasPath: boolean;
  isBusy: boolean;
  canInstall: boolean;
  canLaunchGame: boolean;
  versionMismatch: boolean;
}

export function selectInstallGates(input: {
  env: EnvironmentInfo | null;
  bazaarFound: boolean;
  customGamePath: string;
  cachedDetectedGamePath: string;
  actionBusy: ActionBusy;
  isDebugInstallPreview: boolean;
}): InstallGatesSelection {
  const selectedPath = selectCustomGamePath(input.customGamePath);
  const cachedDetectedPath = input.cachedDetectedGamePath.trim() || null;
  const modInstalled = Boolean(input.env?.bpp_version);
  const bundledBppVersion = input.env?.bundled_bpp_version ?? null;
  const installedBppVersion = input.env?.bpp_version ?? null;
  const pageState = createPageState({
    actionBusy: input.actionBusy,
    bazaarFound: input.bazaarFound,
    selectedGamePath: selectedPath,
    detectedGamePath: input.env?.game_path ?? cachedDetectedPath,
    isDebugInstallPreview: input.isDebugInstallPreview,
    bundledBppVersion,
    installedBppVersion
  });

  return {
    selectedPath,
    modInstalled,
    bundledBppVersion,
    installedBppVersion,
    pageState,
    hasPath: pageState.hasPath,
    isBusy: pageState.isBusy,
    canInstall: pageState.canInstall,
    canLaunchGame: pageState.canLaunchGame,
    versionMismatch: pageState.versionMismatch
  };
}
