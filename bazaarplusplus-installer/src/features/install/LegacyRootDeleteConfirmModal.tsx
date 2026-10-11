import { CircleCheck, FolderX, ShieldCheck } from 'lucide-react';
import {
  ConfirmDialog,
  ConfirmNote,
  ConfirmTarget
} from '../../components/ui/ConfirmDialog';
import { useI18n } from '../../i18n/LocaleProvider';
import { InstallProblemBanner } from './InstallProblemBanner';
import { ResetDataFailureDetails } from './ResetDataFailureDetails';
import type { InstallProblem } from './installProblems';
import type { LegacyImportSummary } from './legacyDataWorkflow';

/** Deletes one Legacy Root. After a V5 import (`afterImport`) it is the offer
 * to delete `BazaarPlusPlusV5`, which the user may decline and keep. */
export function LegacyRootDeleteConfirmModal({
  busy,
  acknowledged,
  targetPath,
  name,
  problem,
  failurePaths,
  afterImport,
  onAcknowledgedChange,
  onClose,
  onConfirm
}: {
  busy: boolean;
  acknowledged: boolean;
  targetPath: string;
  name: string;
  problem: InstallProblem | null;
  failurePaths: string[];
  afterImport?: LegacyImportSummary;
  onAcknowledgedChange: (acknowledged: boolean) => void;
  onClose: () => void;
  onConfirm: () => void | Promise<void>;
}) {
  const { t } = useI18n();

  return (
    <ConfirmDialog
      titleId="legacy-delete-modal-title"
      title={t('legacyDeleteConfirmTitle')}
      tone="danger"
      acknowledge={{
        label: t('legacyDeleteConfirmAcknowledge', { name }),
        checked: acknowledged,
        onChange: onAcknowledgedChange
      }}
      confirmLabel={problem ? t('retry') : t('legacyDeleteConfirmAction')}
      busyLabel={t('legacyDeleteRunning')}
      busy={busy}
      activeDismissalPolicy={{ kind: 'blocked' }}
      dismissLabel={
        problem ? t('close') : afterImport ? t('legacyDeleteKeep') : undefined
      }
      onConfirm={onConfirm}
      onClose={onClose}
    >
      {afterImport && (
        <ConfirmNote tone="neutral" icon={<CircleCheck size={15} />}>
          <p>
            {t('legacyDeleteAfterImport', {
              imported: afterImport.imported_run_count
            })}
          </p>
          {afterImport.skipped_run_count > 0 && (
            <p>
              {t('legacyDeleteAfterImportSkipped', {
                count: afterImport.skipped_run_count
              })}
            </p>
          )}
          {afterImport.unavailable_file_count > 0 && (
            <p>
              {t('legacyDeleteAfterImportUnavailableFiles', {
                count: afterImport.unavailable_file_count
              })}
            </p>
          )}
          <p>{t('legacyDeleteAfterImportOffer')}</p>
        </ConfirmNote>
      )}
      <ConfirmTarget>
        {t('legacyDeleteTarget', { path: targetPath, name })}
      </ConfirmTarget>
      <ConfirmNote tone="danger" icon={<FolderX size={15} />}>
        <p>{t('legacyDeleteConfirmBody', { name })}</p>
        <p>{t('legacyDeleteConfirmSpace')}</p>
      </ConfirmNote>
      <ConfirmNote tone="neutral" icon={<ShieldCheck size={15} />}>
        <p>{t('legacyDeleteConfirmKeepsCurrent')}</p>
        <p>{t('resetDataConfirmGameClosed')}</p>
      </ConfirmNote>
      {problem && <InstallProblemBanner problem={problem} />}
      {failurePaths.length > 0 && (
        <ResetDataFailureDetails paths={failurePaths} />
      )}
    </ConfirmDialog>
  );
}
