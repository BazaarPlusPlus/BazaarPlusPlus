import type { Translate } from '../../i18n/LocaleProvider';
import type { MessageKey } from '../../i18n/messages';
import {
  createUiProblem,
  problemFromError,
  type UiProblem
} from '../shared/problems';

export type InstallProblemCode =
  | 'install_detection_failed'
  | 'install_action_failed'
  | 'install_game_running'
  | 'install_partial_failure'
  | 'install_unexpected';

export type InstallProblem = UiProblem<InstallProblemCode>;

type InstallWarning = {
  code: string;
  params: Record<string, string>;
};

export function installProblemFromError(error: unknown): InstallProblem {
  const problem: UiProblem = problemFromError(error, 'install_unexpected');
  switch (problem.code) {
    case 'install_detection_failed':
    case 'install_action_failed':
    case 'install_game_running':
    case 'install_partial_failure':
    case 'install_unexpected':
      return problem as InstallProblem;
    default:
      return createUiProblem('install_unexpected', {
        params: problem.params,
        diagnostic: problem.diagnostic
      });
  }
}

export function presentInstallWarning(
  warning: InstallWarning,
  t: Translate
): string {
  return t(installWarningMessageKey(warning.code), warning.params);
}

export function presentInstallProblem(
  problem: InstallProblem,
  t: Translate
): string {
  return t(installProblemMessageKey(problem), problem.params);
}

export function installFailurePaths(problem: InstallProblem): string[] {
  if (problem.code !== 'install_partial_failure') return [];
  return (problem.params.paths ?? '')
    .split('\u001f')
    .map((path) => path.trim())
    .filter(Boolean);
}

export type InstallNoticePresentationCode =
  | 'install_done'
  | 'uninstall_done'
  | 'reset_data_done'
  | 'reset_data_nothing_to_delete'
  | 'reset_bepinex_done'
  | 'reset_bepinex_nothing_to_delete';

export function presentInstallNotice(
  code: InstallNoticePresentationCode,
  t: Translate
): string {
  switch (code) {
    case 'install_done':
      return t('installDone');
    case 'uninstall_done':
      return t('uninstallDone');
    case 'reset_data_done':
      return t('resetDataDone');
    case 'reset_data_nothing_to_delete':
      return t('resetDataNothingToDelete');
    case 'reset_bepinex_done':
      return t('resetBepinexDone');
    case 'reset_bepinex_nothing_to_delete':
      return t('resetBepinexNothingToDelete');
  }
}

function installWarningMessageKey(code: string): MessageKey {
  switch (code) {
    case 'game_missing':
      return 'installWarningGameMissing';
    case 'steam_config_unavailable':
      return 'installWarningSteamConfigUnavailable';
    case 'launch_options_not_empty':
      return 'installWarningLaunchOptionsNotEmpty';
    case 'trampoline_not_ready':
      return 'installWarningTrampolineNotReady';
    case 'obsolete_macos_artifacts':
      return 'installWarningObsoleteMacosArtifacts';
    default:
      return 'installWarningUnexpected';
  }
}

function installProblemMessageKey(problem: InstallProblem): MessageKey {
  switch (problem.code) {
    case 'install_detection_failed':
      return 'installProblemDetectionFailed';
    case 'install_game_running':
      return problem.params.operation === 'reset_bepinex'
        ? 'resetBepinexBlockedByGame'
        : 'resetDataBlockedByGame';
    case 'install_partial_failure':
      return problem.params.operation === 'reset_bepinex'
        ? 'resetBepinexPartialFailure'
        : 'resetDataPartialFailure';
    case 'install_action_failed':
      switch (problem.params.operation) {
        case 'choose_directory':
          return 'installProblemChooseDirectoryFailed';
        case 'install':
          return 'installProblemInstallFailed';
        case 'reset_bpp_data':
          return 'installProblemResetDataFailed';
        case 'reset_bepinex':
          return 'installProblemResetBepinexFailed';
        case 'uninstall':
          return 'installProblemUninstallFailed';
        case 'launch':
          return 'installProblemLaunchFailed';
        default:
          return 'installProblemUnexpected';
      }
    case 'install_unexpected':
      return 'installProblemUnexpected';
  }
}
