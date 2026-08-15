import { ChevronDown, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { Button } from '../../components/ui/Button';
import { ConfirmDialog } from '../../components/ui/ConfirmDialog';
import { ModalSource } from '../../components/ui/ModalCoordinator';
import { ProblemBanner } from '../../components/ui/ProblemBanner';
import { useI18n } from '../../i18n/LocaleProvider';
import type { StorageCleanupPreset } from '../../types/backend';
import { formatBytes } from './format';
import { formatProblemDiagnostic } from '../shared/problems';
import {
  presentStorageCleanupProblem,
  type StorageCleanupProblem
} from './storageCleanupProblems';
import {
  useStorageCleanup,
  type CleanupOutcome,
  type CleanupScope,
  type PendingCleanup
} from './useStorageCleanup';

const PRESETS: Array<{
  preset: StorageCleanupPreset;
  labelKey:
    | 'storageCleanupPresetBeforeThisMonth'
    | 'storageCleanupPresetOlderThan7Days'
    | 'storageCleanupPresetAll';
}> = [
  {
    preset: 'before_this_month',
    labelKey: 'storageCleanupPresetBeforeThisMonth'
  },
  {
    preset: 'older_than_7_days',
    labelKey: 'storageCleanupPresetOlderThan7Days'
  },
  { preset: 'all', labelKey: 'storageCleanupPresetAll' }
];

function pendingItemCount(pending: PendingCleanup): number {
  if (pending.scope === 'screenshots') {
    return pending.preview.screenshots + pending.preview.orphan_files;
  }
  return pending.preview.runs;
}

export function StorageCleanupCard({
  onCompleted
}: {
  onCompleted: () => Promise<void> | void;
}) {
  const { locale, t } = useI18n();
  const cleanup = useStorageCleanup(onCompleted);
  const [expanded, setExpanded] = useState(false);

  const pendingBody = (pending: PendingCleanup): string => {
    if (pendingItemCount(pending) === 0) {
      return t('storageCleanupNothingToClean');
    }
    if (pending.scope === 'screenshots') {
      return t('storageCleanupScreenshotsConfirmBody', {
        count: pending.preview.screenshots + pending.preview.orphan_files,
        size: formatBytes(pending.preview.estimated_bytes, locale)
      });
    }
    return t('storageCleanupRunDataConfirmBody', {
      runs: pending.preview.runs,
      battles: pending.preview.battles,
      videos: pending.preview.videos,
      size: formatBytes(pending.preview.estimated_bytes, locale)
    });
  };

  const outcomeText = (outcome: CleanupOutcome): string => {
    if (outcome.scope === 'screenshots') {
      return t('storageCleanupScreenshotsDone', {
        files: outcome.result.deleted_files,
        size: formatBytes(outcome.result.freed_bytes, locale)
      });
    }
    return t('storageCleanupRunDataDone', {
      runs: outcome.result.deleted_runs,
      files: outcome.result.deleted_files,
      size: formatBytes(outcome.result.freed_bytes, locale)
    });
  };

  return (
    <>
      <section className={`bpp-history-cleanup ${expanded ? 'is-open' : ''}`}>
        <button
          type="button"
          className="bpp-history-cleanup-toggle"
          aria-expanded={expanded}
          aria-controls="history-storage-cleanup-content"
          onClick={() => setExpanded((open) => !open)}
        >
          <ChevronDown size={16} className="bpp-history-cleanup-chevron" />
          <Trash2 size={19} className="bpp-history-cleanup-trash" />
          <span className="bpp-history-cleanup-title">
            {t('storageCleanupTitle')}
          </span>
        </button>

        <div
          id="history-storage-cleanup-content"
          className="bpp-history-cleanup-reveal"
          aria-hidden={!expanded}
          inert={!expanded}
        >
          <div className="bpp-history-cleanup-content">
            {cleanup.previewProblem && (
              <StorageCleanupProblemBanner problem={cleanup.previewProblem} />
            )}

            <CleanupRow
              label={t('storageCleanupScreenshotsLabel')}
              description={t('storageCleanupScreenshotsDescription')}
              scope="screenshots"
              busy={cleanup.busy}
              onSelect={cleanup.requestCleanup}
            />
            <CleanupRow
              label={t('storageCleanupRunDataLabel')}
              description={t('storageCleanupRunDataDescription')}
              scope="run_data"
              busy={cleanup.busy}
              onSelect={cleanup.requestCleanup}
            />

            {cleanup.outcome && (
              <p className="bpp-history-cleanup-outcome">
                {outcomeText(cleanup.outcome)}
              </p>
            )}
          </div>
        </div>
      </section>

      <ModalSource
        id="route:storage-cleanup"
        open={cleanup.pending !== null}
        priority={
          cleanup.operation?.phase === 'running' ? 'critical' : 'confirmation'
        }
        dismissalPolicy={
          cleanup.operation?.phase === 'running' ? 'blocked' : 'dismissible'
        }
      >
        {cleanup.pending && (
          <ConfirmDialog
            titleId="cleanup-confirm-modal-title"
            title={t('storageCleanupConfirmTitle')}
            tone="danger"
            confirmLabel={
              cleanup.problem ? t('retry') : t('storageCleanupConfirmAction')
            }
            busyLabel={
              cleanup.pending.scope === 'screenshots'
                ? t('storageCleanupRunningScreenshots')
                : t('storageCleanupRunningRunData')
            }
            busy={cleanup.busy}
            activeDismissalPolicy={{ kind: 'blocked' }}
            dismissLabel={
              cleanup.operation?.phase === 'failed' ? t('close') : undefined
            }
            confirmDisabled={pendingItemCount(cleanup.pending) === 0}
            onConfirm={cleanup.confirm}
            onClose={cleanup.cancel}
          >
            <p className="m-0 text-[12px] leading-relaxed text-[rgba(232,200,122,0.86)] fira-code selectable">
              {t('storageCleanupTarget', {
                scope:
                  cleanup.pending.scope === 'screenshots'
                    ? t('storageCleanupScreenshotsLabel')
                    : t('storageCleanupRunDataLabel'),
                preset: t(
                  PRESETS.find(
                    ({ preset }) => preset === cleanup.pending?.preset
                  )?.labelKey ?? 'storageCleanupPresetAll'
                )
              })}
            </p>
            <p className="m-0 text-[13px] leading-relaxed text-[rgba(245,220,220,0.86)]">
              {pendingBody(cleanup.pending)}
            </p>
            {cleanup.pending.preview.skipped_pending_uploads > 0 && (
              <p className="m-0 text-[12px] leading-relaxed text-[rgba(200,170,120,0.8)]">
                {t('storageCleanupSkippedPending', {
                  count: cleanup.pending.preview.skipped_pending_uploads
                })}
              </p>
            )}
            {cleanup.problem && (
              <StorageCleanupProblemBanner problem={cleanup.problem} />
            )}
          </ConfirmDialog>
        )}
      </ModalSource>
    </>
  );
}

function StorageCleanupProblemBanner({
  problem
}: {
  problem: StorageCleanupProblem;
}) {
  const { t } = useI18n();
  return (
    <ProblemBanner
      message={presentStorageCleanupProblem(problem, t)}
      diagnostic={problem.diagnostic ? formatProblemDiagnostic(problem) : null}
      diagnosticLabel={t('problemDiagnostics')}
    />
  );
}

function CleanupRow({
  label,
  description,
  scope,
  busy,
  onSelect
}: {
  label: string;
  description: string;
  scope: CleanupScope;
  busy: boolean;
  onSelect: (
    scope: CleanupScope,
    preset: StorageCleanupPreset
  ) => Promise<boolean> | void;
}) {
  const { t } = useI18n();

  return (
    <div className="bpp-history-cleanup-row">
      <div className="bpp-history-cleanup-row-copy">
        <span className="bpp-history-cleanup-row-label">{label}</span>
        <span className="bpp-history-cleanup-row-description">
          {description}
        </span>
      </div>
      <div className="bpp-history-cleanup-actions">
        {PRESETS.map(({ preset, labelKey }) => {
          // The visible label is the range alone so the three buttons read as
          // one axis; the full action stays available to assistive tech.
          const actionLabel = t('storageCleanupActionLabel', {
            scope: label,
            preset: t(labelKey)
          });
          return (
            <Button
              key={preset}
              type="button"
              size="small"
              disabled={busy}
              onClick={() => void onSelect(scope, preset)}
              className="bpp-history-cleanup-action"
              title={actionLabel}
              aria-label={actionLabel}
            >
              {t(labelKey)}
            </Button>
          );
        })}
      </div>
    </div>
  );
}
