import type { Translate } from '../../i18n/LocaleProvider';
import {
  createUiProblem,
  problemFromError,
  type UiProblem
} from '../shared/problems';

export type AboutBootstrapProblem = UiProblem<'about_bootstrap_failed'>;

export function aboutBootstrapProblemFromError(
  error: unknown
): AboutBootstrapProblem {
  const captured = problemFromError(error, 'about_bootstrap_failed');
  return createUiProblem('about_bootstrap_failed', {
    params: { operation: 'load_bootstrap' },
    diagnostic: captured.diagnostic
  });
}

export function presentAboutProblem(
  problem: AboutBootstrapProblem,
  t: Translate
): string {
  return t('aboutProblemBootstrapFailed', problem.params);
}
