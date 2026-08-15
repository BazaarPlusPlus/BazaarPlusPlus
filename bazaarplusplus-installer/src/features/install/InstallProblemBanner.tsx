import { Button } from '../../components/ui/Button';
import { ProblemBanner } from '../../components/ui/ProblemBanner';
import { useI18n } from '../../i18n/LocaleProvider';
import { formatProblemDiagnostic } from '../shared/problems';
import { presentInstallProblem, type InstallProblem } from './installProblems';

export function InstallProblemBanner({
  problem,
  onRetry
}: {
  problem: InstallProblem;
  onRetry?: () => void;
}) {
  const { t } = useI18n();
  return (
    <ProblemBanner
      message={presentInstallProblem(problem, t)}
      diagnostic={problem.diagnostic ? formatProblemDiagnostic(problem) : null}
      diagnosticLabel={t('problemDiagnostics')}
      actions={
        onRetry ? (
          <Button type="button" size="small" variant="ghost" onClick={onRetry}>
            {t('retry')}
          </Button>
        ) : undefined
      }
    />
  );
}
