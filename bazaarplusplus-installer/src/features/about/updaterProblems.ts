import type { Translate } from '../../i18n/LocaleProvider';
import type { MessageKey } from '../../i18n/messages';
import { isWindowsPlatform } from '../shared/platform';
import {
  createUiProblem,
  problemFromError,
  type UiProblem
} from '../shared/problems';

export type UpdaterProblemOperation =
  'check' | 'download' | 'install' | 'restart';

export type UpdaterProblemCode =
  | 'updater_check_failed'
  | 'updater_download_failed'
  | 'updater_install_failed'
  | 'updater_restart_failed';

export type UpdaterProblem = UiProblem<UpdaterProblemCode>;

const CODE_BY_OPERATION: Record<UpdaterProblemOperation, UpdaterProblemCode> = {
  check: 'updater_check_failed',
  download: 'updater_download_failed',
  install: 'updater_install_failed',
  restart: 'updater_restart_failed'
};

export function updaterProblemFromError(
  error: unknown,
  operation: UpdaterProblemOperation,
  version: string | null = null
): UpdaterProblem {
  const code = CODE_BY_OPERATION[operation];
  const captured = problemFromError(error, code);
  return createUiProblem(code, {
    params: {
      operation,
      ...(version ? { version } : {})
    },
    diagnostic: captured.diagnostic
  });
}

export function presentUpdaterProblem(
  problem: UpdaterProblem,
  t: Translate
): string {
  return t(updaterProblemMessageKey(problem), problem.params);
}

function updaterProblemMessageKey(problem: UpdaterProblem): MessageKey {
  switch (problem.code) {
    case 'updater_check_failed':
      return 'updaterProblemCheckFailed';
    case 'updater_download_failed':
      return 'updaterProblemDownloadFailed';
    case 'updater_install_failed':
      return 'updaterProblemInstallFailed';
    case 'updater_restart_failed':
      // The recovery step names a real OS surface, so it has to match the host.
      return isWindowsPlatform()
        ? 'updaterProblemRestartFailedWindows'
        : 'updaterProblemRestartFailedMac';
  }
}
