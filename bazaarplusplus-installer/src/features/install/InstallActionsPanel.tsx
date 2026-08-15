import { Box, CloudDownload, Layers3, Loader2, Trash2 } from 'lucide-react';
import type { ReactNode } from 'react';
import { ActionTile } from '../../components/ui/ActionTile';
import { useI18n } from '../../i18n/LocaleProvider';
import { ResetDataFailureDetails } from './ResetDataFailureDetails';
import { installFailurePaths } from './installProblems';
import type {
  InstallPageSnapshot,
  InstallWorkflowIntents
} from './installWorkflow';

export function InstallActionsPanel({
  snapshot,
  intents,
  updateChecking,
  onCheckUpdate
}: {
  snapshot: Extract<InstallPageSnapshot, { phase: 'ready' }>;
  intents: InstallWorkflowIntents;
  updateChecking: boolean;
  onCheckUpdate: () => void;
}) {
  const { t } = useI18n();
  const state = snapshot.data;
  const failurePaths =
    snapshot.confirmation?.phase === 'failed'
      ? installFailurePaths(snapshot.confirmation.problem)
      : snapshot.actionProblem
        ? installFailurePaths(snapshot.actionProblem)
        : [];

  return (
    <section>
      <h3 className="bpp-install-section-title">
        {t('maintenanceToolsHeading')}
      </h3>
      <div className="bpp-install-maintenance-grid">
        <MaintenanceAction
          danger
          disabled={!snapshot.actions.requestResetData}
          busy={snapshot.operation === 'resetData'}
          icon={<Layers3 size={22} />}
          title={
            state.game.path_valid && !state.has_resettable_data
              ? t('actionNoResettableData')
              : t('actionResetData')
          }
          detail={
            state.game.path_valid && !state.has_resettable_data
              ? t('maintenanceNoResettableDataDescription')
              : t('maintenanceResetDataDescription')
          }
          onClick={() => intents.requestResetData()}
        />
        <MaintenanceAction
          disabled={!snapshot.actions.requestResetBepinex}
          busy={snapshot.operation === 'resetBepinex'}
          icon={<Box size={22} />}
          title={t('actionResetBepinex')}
          detail={t('maintenanceResetBepinexDescription')}
          onClick={() => intents.requestResetBepinex()}
        />
        <MaintenanceAction
          disabled={updateChecking}
          busy={updateChecking}
          icon={<CloudDownload size={22} />}
          title={t('headerCheckUpdate')}
          detail={t('checkUpdateDescription')}
          onClick={onCheckUpdate}
        />
        <MaintenanceAction
          danger
          disabled={!snapshot.actions.requestUninstall}
          busy={snapshot.operation === 'uninstall'}
          icon={<Trash2 size={22} />}
          title={t('actionUninstall')}
          detail={t('maintenanceUninstallDescription')}
          onClick={() => intents.requestUninstall()}
        />
      </div>

      {failurePaths.length > 0 && !snapshot.confirmation && (
        <ResetDataFailureDetails paths={failurePaths} />
      )}
    </section>
  );
}

function MaintenanceAction({
  icon,
  title,
  detail,
  onClick,
  disabled,
  busy,
  danger = false
}: {
  icon: ReactNode;
  title: string;
  detail: string;
  onClick: () => void;
  disabled: boolean;
  busy: boolean;
  danger?: boolean;
}) {
  return (
    <ActionTile
      disabled={disabled}
      busy={busy}
      onClick={onClick}
      danger={danger}
      icon={busy ? <Loader2 size={22} className="animate-spin" /> : icon}
      title={title}
      description={detail}
    />
  );
}
