import { ProblemBanner } from '../../components/ui/ProblemBanner';
import { useI18n } from '../../i18n/LocaleProvider';
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
      problem={problem}
      onRetry={onRetry}
    />
  );
}
