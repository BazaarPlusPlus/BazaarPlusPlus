import type { EnvironmentInfo } from '../types.ts';
import type { ActionBusy, PageState } from './state.ts';
import type { UpdaterSnapshot } from '../updater.ts';
import {
  type LocalizedText,
  type PendingSteamAction,
  type TranslateText
} from './selectors/types.ts';
import { selectInstallGates } from './selectors/install-gates.ts';
import { selectModeLabels } from './selectors/mode-labels.ts';
import { selectSteamModal } from './selectors/steam-modal.ts';
import { selectUpdaterButton } from './selectors/updater-button.ts';

export type {
  PendingSteamAction,
  LocalizedText,
  TranslateText
} from './selectors/types.ts';

export interface InstallPageModelInput {
  env: EnvironmentInfo | null;
  bazaarFound: boolean;
  customGamePath: string;
  cachedDetectedGamePath: string;
  actionBusy: ActionBusy;
  showStreamMode: boolean;
  locale: string;
  isDebugInstallPreview: boolean;
  updaterSnapshot: UpdaterSnapshot;
  hasPendingUpdate: boolean;
  pendingSteamAction: PendingSteamAction;
  localized: LocalizedText;
  t: TranslateText;
}

export interface InstallPageModel {
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
  modeTitle: string;
  modeToggleLabel: string;
  dotnetDownloadUrl: string;
  localeBadge: string;
  localeButtonLabel: string;
  updaterProgressLabel: string | null;
  updaterButtonLabel: string;
  updaterButtonTitle: string;
  updaterButtonDisabled: boolean;
  updaterButtonHighlighted: boolean;
  steamModalTitle: string;
  steamModalBody: string;
  steamModalCancelText: string;
}

export function createInstallDebugEnvironment(): EnvironmentInfo {
  return {
    steam_path: 'C:\\Program Files (x86)\\Steam',
    steam_launch_options_supported: true,
    game_path: 'C:\\Games\\The Bazaar',
    game_path_valid: true,
    dotnet_version: '9.0.0',
    dotnet_ok: true,
    bepinex_installed: false,
    bpp_version: null,
    bundled_bpp_version: 'debug-preview'
  };
}

export function formatByteLabel(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }

  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let unitIndex = 0;

  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unitIndex]}`;
}

export function createInstallPageModel(
  input: InstallPageModelInput
): InstallPageModel {
  const installGates = selectInstallGates({
    env: input.env,
    bazaarFound: input.bazaarFound,
    customGamePath: input.customGamePath,
    cachedDetectedGamePath: input.cachedDetectedGamePath,
    actionBusy: input.actionBusy,
    isDebugInstallPreview: input.isDebugInstallPreview
  });
  const updaterButton = selectUpdaterButton({
    snapshot: input.updaterSnapshot,
    hasPendingUpdate: input.hasPendingUpdate,
    t: input.t
  });
  const steamModal = selectSteamModal({
    pendingSteamAction: input.pendingSteamAction,
    t: input.t
  });
  const modeLabels = selectModeLabels({
    showStreamMode: input.showStreamMode,
    locale: input.locale,
    localized: input.localized,
    t: input.t
  });

  return {
    selectedPath: installGates.selectedPath,
    modInstalled: installGates.modInstalled,
    bundledBppVersion: installGates.bundledBppVersion,
    installedBppVersion: installGates.installedBppVersion,
    pageState: installGates.pageState,
    hasPath: installGates.hasPath,
    isBusy: installGates.isBusy,
    canInstall: installGates.canInstall,
    canLaunchGame: installGates.canLaunchGame,
    versionMismatch: installGates.versionMismatch,
    modeTitle: modeLabels.modeTitle,
    modeToggleLabel: modeLabels.modeToggleLabel,
    dotnetDownloadUrl: modeLabels.dotnetDownloadUrl,
    localeBadge: modeLabels.localeBadge,
    localeButtonLabel: modeLabels.localeButtonLabel,
    updaterProgressLabel: updaterButton.progressLabel,
    updaterButtonLabel: updaterButton.label,
    updaterButtonTitle: updaterButton.title,
    updaterButtonDisabled: updaterButton.disabled,
    updaterButtonHighlighted: updaterButton.highlighted,
    steamModalTitle: steamModal.title,
    steamModalBody: steamModal.body,
    steamModalCancelText: steamModal.cancelText
  };
}
