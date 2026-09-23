import { ChevronDown } from 'lucide-react';
import type { ReactNode } from 'react';
import { useI18n } from '../../i18n/LocaleProvider';
import { Button } from '../../components/ui/Button';
import { ResetDataFailureDetails } from './ResetDataFailureDetails';
import { installFailurePaths } from './installProblems';
import type {
  InstallPageSnapshot,
  InstallWorkflowIntents
} from './installWorkflow';

/**
 * Uninstall and reset tools, folded away by default so destructive actions
 * are not on screen during everyday use. The section opens itself while one
 * of its operations runs or has left failure details to read.
 */
export function InstallActionsPanel({
  snapshot,
  intents
}: {
  snapshot: Extract<InstallPageSnapshot, { phase: 'ready' }>;
  intents: InstallWorkflowIntents;
}) {
  const { t } = useI18n();
  const state = snapshot.data;
  const noResettableData = state.game.path_valid && !state.has_resettable_data;
  const failurePaths =
    snapshot.confirmation?.phase === 'failed'
      ? installFailurePaths(snapshot.confirmation.problem)
      : snapshot.actionProblem
        ? installFailurePaths(snapshot.actionProblem)
        : [];
  const forceOpen =
    snapshot.operation === 'uninstall' ||
    snapshot.operation === 'resetBepinex' ||
    snapshot.operation === 'resetData' ||
    (failurePaths.length > 0 && !snapshot.confirmation);

  return (
    <details className="bpp-panel bpp-disclosure" open={forceOpen || undefined}>
      <summary className="bpp-disclosure-summary">
        <span className="bpp-row-copy">
          <span className="bpp-row-title">{t('maintenanceToolsHeading')}</span>
          <span className="bpp-row-description">
            {t('maintenanceToolsHint')}
          </span>
        </span>
        <ChevronDown size={16} aria-hidden="true" />
      </summary>
      <MaintenanceRow
        title={t('actionUninstall')}
        detail={t('maintenanceUninstallDescription')}
        action={
          <Button
            size="sm"
            disabled={!snapshot.actions.requestUninstall}
            busy={snapshot.operation === 'uninstall'}
            onClick={() => intents.requestUninstall()}
          >
            {t('actionUninstall')}
          </Button>
        }
      />
      <MaintenanceRow
        title={t('actionResetBepinex')}
        detail={t('maintenanceResetBepinexDescription')}
        action={
          <Button
            variant="danger"
            size="sm"
            disabled={!snapshot.actions.requestResetBepinex}
            busy={snapshot.operation === 'resetBepinex'}
            onClick={() => intents.requestResetBepinex()}
            aria-label={t('actionResetBepinex')}
          >
            {t('actionDelete')}
          </Button>
        }
      />
      <MaintenanceRow
        title={
          noResettableData ? t('actionNoResettableData') : t('actionResetData')
        }
        detail={
          noResettableData
            ? t('maintenanceNoResettableDataDescription')
            : t('maintenanceResetDataDescription')
        }
        action={
          <Button
            variant="danger"
            size="sm"
            disabled={!snapshot.actions.requestResetData}
            busy={snapshot.operation === 'resetData'}
            onClick={() => intents.requestResetData()}
            aria-label={t('actionResetData')}
          >
            {t('actionDelete')}
          </Button>
        }
      />
      {failurePaths.length > 0 && !snapshot.confirmation && (
        <div className="px-4 pb-4">
          <ResetDataFailureDetails paths={failurePaths} />
        </div>
      )}
    </details>
  );
}

function MaintenanceRow({
  title,
  detail,
  action
}: {
  title: string;
  detail: string;
  action: ReactNode;
}) {
  return (
    <div className="bpp-row">
      <div className="bpp-row-copy">
        <span className="bpp-row-title">{title}</span>
        <span className="bpp-row-description">{detail}</span>
      </div>
      {action}
    </div>
  );
}
