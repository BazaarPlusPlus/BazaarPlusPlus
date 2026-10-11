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
  | 'install_partial_failure'
  | GameRunningProblemCode
  | 'install_unexpected';

/** One code per operation that refuses while The Bazaar runs. */
type GameRunningProblemCode =
  | 'install_blocked_by_game'
  | 'reset_blocked_by_game'
  | 'bepinex_reset_blocked_by_game'
  | 'import_blocked_by_game'
  | 'legacy_delete_blocked_by_game';

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
    case 'install_partial_failure':
    case 'install_blocked_by_game':
    case 'reset_blocked_by_game':
    case 'bepinex_reset_blocked_by_game':
    case 'import_blocked_by_game':
    case 'legacy_delete_blocked_by_game':
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
    case 'install_blocked_by_game':
      return 'installBlockedByGame';
    case 'reset_blocked_by_game':
      return 'resetDataBlockedByGame';
    case 'bepinex_reset_blocked_by_game':
      return 'resetBepinexBlockedByGame';
    case 'import_blocked_by_game':
      return 'importBlockedByGame';
    case 'legacy_delete_blocked_by_game':
      return 'legacyDeleteBlockedByGame';
    case 'install_partial_failure':
      switch (problem.params.operation) {
        case 'reset_bepinex':
          return 'resetBepinexPartialFailure';
        case 'delete_legacy_root':
          return 'legacyDeletePartialFailure';
        default:
          return 'resetDataPartialFailure';
      }
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
        case 'get_legacy_data_state':
          return 'installProblemLegacyDataFailed';
        case 'delete_legacy_root':
          return 'installProblemLegacyDeleteFailed';
        default:
          return 'installProblemUnexpected';
      }
    case 'install_unexpected':
      return 'installProblemUnexpected';
  }
}
