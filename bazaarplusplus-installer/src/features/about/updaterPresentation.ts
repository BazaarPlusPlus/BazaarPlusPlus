import type { MessageKey } from '../../i18n/messages';
import type {
  ModalDismissalPolicy,
  ModalPriority
} from '../shared/modalCoordinator';
import type { UpdaterSnapshot } from './updater';

export type UpdaterHeaderIcon =
  'download' | 'checking' | 'current' | 'preview' | 'restart' | 'error';

export type UpdaterModalAction =
  'install' | 'restart' | 'retry-install' | 'retry-restart';

export type UpdaterUiContract = {
  header: {
    labelKey: MessageKey;
    icon: UpdaterHeaderIcon;
    busy: boolean;
    disabled: boolean;
    tone: 'default' | 'error';
  };
  modal: {
    titleKey: MessageKey;
    action: UpdaterModalAction | null;
    actionLabelKey: MessageKey | null;
    priority: ModalPriority;
    dismissalPolicy: ModalDismissalPolicy;
  } | null;
};

export function getUpdaterUiContract(
  snapshot: UpdaterSnapshot
): UpdaterUiContract {
  const modal = (
    titleKey: MessageKey,
    action: UpdaterModalAction | null,
    actionLabelKey: MessageKey | null,
    blocked = false
  ): UpdaterUiContract['modal'] => ({
    titleKey,
    action,
    actionLabelKey,
    priority: blocked ? 'critical' : 'system',
    dismissalPolicy: blocked ? 'blocked' : 'dismissible'
  });

  switch (snapshot.phase) {
    case 'idle':
      return {
        header: header('headerCheckUpdate', 'download'),
        modal: null
      };
    case 'checking':
      return {
        header: header('headerCheckingUpdate', 'checking', true),
        modal: null
      };
    case 'current':
      return {
        header: header('updaterCurrent', 'current'),
        modal: null
      };
    case 'preview':
      return {
        header: header('updaterPreview', 'preview'),
        modal: null
      };
    case 'available':
      return {
        header: header('updateHeaderAvailable', 'download', false, true),
        modal: modal('updateModalTitle', 'install', 'updateInstall')
      };
    case 'downloading':
      return {
        header: header('updateDownloading', 'checking', true),
        modal: modal('updateDownloading', null, null, true)
      };
    case 'installing':
      return {
        header: header('updateInstalling', 'checking', true),
        modal: modal('updateInstalling', null, null, true)
      };
    case 'ready-to-restart':
      return {
        header: header('updateReady', 'restart', false, true),
        modal: modal('updateReady', 'restart', 'updateRestartNow')
      };
    case 'restarting':
      return {
        header: header('updateRestarting', 'checking', true),
        modal: modal('updateRestarting', null, null, true)
      };
    case 'failed': {
      if (snapshot.problem.code === 'updater_check_failed') {
        return {
          header: header('headerCheckFailed', 'error', false, false, 'error'),
          modal: null
        };
      }
      const restartFailure = snapshot.problem.code === 'updater_restart_failed';
      return {
        header: header('updateError', 'error', false, true, 'error'),
        modal: modal(
          restartFailure ? 'updateRestartFailedTitle' : 'updateError',
          restartFailure ? 'retry-restart' : 'retry-install',
          restartFailure ? 'updateRetryRestart' : 'updateRetry'
        )
      };
    }
  }
}

function header(
  labelKey: MessageKey,
  icon: UpdaterHeaderIcon,
  busy = false,
  disabled = false,
  tone: 'default' | 'error' = 'default'
): UpdaterUiContract['header'] {
  return { labelKey, icon, busy, disabled, tone };
}
