import { ChevronDown, HardDrive } from 'lucide-react';
import { useState } from 'react';
import { Button } from '../../components/ui/Button';
import { ConfirmDialog } from '../../components/ui/ConfirmDialog';
import { ModalSource } from '../../components/ui/ModalCoordinator';
import { ProblemBanner } from '../../components/ui/ProblemBanner';
import { useI18n } from '../../i18n/LocaleProvider';
import type { HistorySummary, StorageCleanupPreset } from '../../types/backend';
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

export function HistoryOverview({
  summary,
  onCompleted
}: {
  summary: HistorySummary;
  onCompleted: () => Promise<void> | void;
}) {
  const { locale, t } = useI18n();
  const cleanup = useStorageCleanup(onCompleted);
  const [expanded, setExpanded] = useState(false);
  const numberFormat = new Intl.NumberFormat(locale);

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
      <section
        className={`bpp-history-overview ${expanded ? 'is-open' : ''}`}
        aria-label={t('historyOverviewLabel')}
      >
        <div className="bpp-history-overview-bar">
          <dl className="bpp-history-stats">
            <OverviewMetric
              label={t('historySummaryRuns')}
              value={numberFormat.format(summary.runs)}
            />
            <OverviewMetric
              label={t('historySummaryVideos')}
              value={numberFormat.format(summary.videos)}
            />
            <OverviewMetric
              label={t('historySummaryWinRate')}
              value={
                summary.win_rate === null
                  ? '—'
                  : `${Math.round(summary.win_rate * 100)}%`
              }
              detail={t(
                summary.win_rate === null
                  ? 'historySummaryWinRateUnavailable'
                  : 'historySummaryWinRateDescription'
              )}
              accent
            />
          </dl>
          <button
            type="button"
            className="bpp-history-cleanup-toggle"
            aria-expanded={expanded}
            aria-controls="history-storage-cleanup-content"
            onClick={() => setExpanded((open) => !open)}
          >
            <HardDrive size={15} aria-hidden="true" />
            <span>{t('storageCleanupTitle')}</span>
            <ChevronDown
              size={14}
              className="bpp-history-cleanup-chevron"
              aria-hidden="true"
            />
          </button>
        </div>

        <div
          id="history-storage-cleanup-content"
          className="bpp-history-cleanup-reveal"
          role="region"
          aria-label={t('storageCleanupTitle')}
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

function OverviewMetric({
  label,
  value,
  detail,
  accent = false
}: {
  label: string;
  value: string;
  detail?: string;
  accent?: boolean;
}) {
  return (
    <div className={`bpp-history-stat ${accent ? 'is-accent' : ''}`}>
      <dt className="bpp-history-stat-label">{label}</dt>
      <dd className="bpp-history-stat-reading">
        <span className="bpp-history-stat-value">{value}</span>
        {detail && <span className="bpp-history-stat-detail">{detail}</span>}
      </dd>
    </div>
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

  const [preset, setPreset] =
    useState<StorageCleanupPreset>('before_this_month');

  return (
    <div className="bpp-history-cleanup-row">
      <div className="bpp-history-cleanup-row-copy">
        <span className="bpp-history-cleanup-row-label">{label}</span>
        <span className="bpp-history-cleanup-row-description">
          {description}
        </span>
      </div>
      <div className="bpp-history-cleanup-actions">
        <div className="bpp-history-cleanup-range">
          <select
            aria-label={t('storageCleanupRangeLabel', { scope: label })}
            value={preset}
            disabled={busy}
            onChange={(event) =>
              setPreset(event.target.value as StorageCleanupPreset)
            }
          >
            {PRESETS.map(({ preset, labelKey }) => (
              <option key={preset} value={preset}>
                {t(labelKey)}
              </option>
            ))}
          </select>
          <ChevronDown size={14} aria-hidden="true" />
        </div>
        <Button
          type="button"
          size="small"
          disabled={busy}
          onClick={() => void onSelect(scope, preset)}
          className="bpp-history-cleanup-action"
          aria-label={t('storageCleanupPreviewLabel', { scope: label })}
        >
          {t('storageCleanupPreviewAction')}
        </Button>
      </div>
    </div>
  );
}
