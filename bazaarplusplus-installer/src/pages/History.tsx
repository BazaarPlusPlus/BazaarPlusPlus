import { useState } from 'react';
import {
  ChevronRight,
  History as HistoryIcon,
  Image as ImageIcon,
  RefreshCw
} from 'lucide-react';
import { Link } from 'react-router-dom';
import { Button } from '../components/ui/Button';
import { EmptyState } from '../components/ui/EmptyState';
import { LoadingPanel } from '../components/ui/LoadingPanel';
import { PageShell } from '../components/ui/PageShell';
import { ProblemBanner } from '../components/ui/ProblemBanner';
import {
  formatDateTime,
  formatRunResultLabel,
  toneColorClass
} from '../features/history/format';
import { StorageCleanupCard } from '../features/history/StorageCleanupCard';
import {
  presentHistoryProblem,
  type HistoryPageProblem
} from '../features/history/historyProblems';
import {
  useHistoryPage,
  type EndGameProcessOutcome
} from '../features/history/useHistoryPage';
import { isWindowsPlatform } from '../features/shared/platform';
import { formatProblemDiagnostic } from '../features/shared/problems';
import { useToast } from '../components/ui/Toast';
import { useI18n } from '../i18n/LocaleProvider';
import type { MessageKey } from '../i18n/messages';
import type { HistoryRunRow } from '../types/backend';

export default function History() {
  const page = useHistoryPage();
  const { t } = useI18n();

  return (
    <PageShell
      title={t('historyTitle')}
      className="bpp-history-page"
      action={
        <Button
          type="button"
          onClick={page.refresh}
          disabled={page.busy}
          busy={page.busy}
        >
          <RefreshCw size={16} className={page.busy ? 'animate-spin' : ''} />
          {t('refresh')}
        </Button>
      }
    >
      {page.state.phase === 'initial-loading' ? (
        <LoadingPanel label={t('historyLoading')} />
      ) : page.state.phase === 'blocking-failure' ? (
        <HistoryProblemBanner
          problem={page.state.problem}
          onRetry={page.refresh}
          onEndGameProcess={page.endLeftoverGameProcess}
          endingGameProcess={page.endingGameProcess}
        />
      ) : (
        <div className="flex min-h-0 w-full flex-1 flex-col gap-4">
          <div className="bpp-history-summary-grid">
            <SummaryCard
              label={t('historySummaryRuns')}
              value={page.summary?.runs ?? '-'}
              detail={t('historySummaryRunsDescription')}
            />
            <SummaryCard
              label={t('historySummaryVideos')}
              value={page.summary?.videos ?? '-'}
              detail={t('historySummaryVideosDescription')}
            />
            <SummaryCard
              label={t('historySummaryWinRate')}
              value={page.summary?.winRate ?? '-'}
              detail={
                page.state.data.summary.win_rate === null
                  ? t('historySummaryWinRateUnavailable')
                  : t('historySummaryWinRateDescription')
              }
            />
          </div>

          <StorageCleanupCard onCompleted={page.refresh} />

          {page.state.refresh.phase === 'failed' && (
            <HistoryProblemBanner
              problem={page.state.refresh.problem}
              onRetry={page.refresh}
              onEndGameProcess={page.endLeftoverGameProcess}
              endingGameProcess={page.endingGameProcess}
            />
          )}

          {page.state.phase === 'ready-content' &&
            page.previewProblem &&
            page.state.data.runs.some((run) => run.strip_url) && (
              <HistoryPreviewProblemBanner problem={page.previewProblem} />
            )}

          <div className="flex flex-col gap-3 flex-1 min-h-0 overflow-y-auto custom-scrollbar pr-2">
            {page.state.phase === 'ready-empty' ? (
              <EmptyState
                icon={<HistoryIcon size={24} />}
                heading={t('noLocalRuns')}
                description={t('historyEmptyDescription')}
                primaryAction={
                  <Button
                    variant="primary"
                    busy={page.busy}
                    onClick={page.refresh}
                  >
                    <RefreshCw
                      size={16}
                      className={page.busy ? 'animate-spin' : undefined}
                    />
                    {t('historyEmptyRefresh')}
                  </Button>
                }
                secondaryAction={
                  <Link
                    to="/"
                    className="bpp-button bpp-ui-button bpp-ui-button-ghost bpp-link-button"
                  >
                    {t('historyEmptyInstall')}
                  </Link>
                }
              />
            ) : (
              page.state.data.runs.map((run: HistoryRunRow) => (
                <RunRow
                  key={run.run_id}
                  run={run}
                  previewUrl={page.previewUrl(run)}
                  previewProblem={page.previewProblem}
                />
              ))
            )}
          </div>
        </div>
      )}
    </PageShell>
  );
}

function SummaryCard({
  label,
  value,
  detail
}: {
  label: string;
  value: string;
  detail: string;
}) {
  return (
    <div className="bpp-history-summary-card">
      <strong className="bpp-history-stat-value">{value}</strong>
      <div className="bpp-history-stat-copy">
        <span className="bpp-history-stat-label">{label}</span>
        <span className="bpp-history-stat-detail">{detail}</span>
      </div>
    </div>
  );
}

function RunRow({
  run,
  previewUrl,
  previewProblem
}: {
  run: HistoryRunRow;
  previewUrl: string | null;
  previewProblem: HistoryPageProblem | null;
}) {
  const { locale, t } = useI18n();
  const result = formatRunResultLabel(run.result);
  const detailPath = `/history/${encodeURIComponent(run.run_id)}`;
  const fallbackLabel = previewProblem
    ? t('historyPreviewServiceOffline')
    : t('historyPreviewFallback');

  return (
    <Link to={detailPath} className="bpp-history-run-card group">
      <RunPreview
        key={previewUrl ?? 'preview-unavailable'}
        previewUrl={previewUrl}
        fallbackLabel={fallbackLabel}
      />

      <div className="bpp-history-run-data">
        <div className="bpp-history-run-heading">
          <div className="bpp-history-run-identity">
            <span className="bpp-history-run-hero cinzel">{run.hero}</span>
            <span className="bpp-history-run-date fira-code">
              {formatDateTime(run.started_at_utc, locale)}
            </span>
          </div>

          <span
            className={`bpp-history-run-result ${toneColorClass(result.tone)}`}
          >
            {t(result.key)}
          </span>

          <span className="bpp-history-run-detail">
            <span>{t('viewDetail')}</span>
            <ChevronRight size={14} />
          </span>
        </div>

        <div className="bpp-history-run-metrics">
          <Metric
            label={t('runMetricWins')}
            value={run.victories === null ? '-' : String(run.victories)}
            fira
          />
          <Metric
            label={t('runMetricDays')}
            value={run.final_day === null ? '-' : String(run.final_day)}
            fira
          />
          <Metric
            label={t('runStatRank')}
            value={run.final_player_rank ?? '-'}
            gold
          />
          <Metric
            label={t('runStatRating')}
            value={
              run.final_player_rating === null
                ? '-'
                : String(run.final_player_rating)
            }
            fira
          />
        </div>
      </div>
    </Link>
  );
}

function RunPreview({
  previewUrl,
  fallbackLabel
}: {
  previewUrl: string | null;
  fallbackLabel: string;
}) {
  const [failedUrl, setFailedUrl] = useState<string | null>(null);
  const visibleUrl = previewUrl && previewUrl !== failedUrl ? previewUrl : null;

  return (
    <div
      className="bpp-history-run-preview"
      title={visibleUrl ? undefined : fallbackLabel}
      aria-label={visibleUrl ? undefined : fallbackLabel}
    >
      {visibleUrl ? (
        // Preserve the complete server-generated strip. Its rounded crop
        // dimensions can vary by a few pixels between source resolutions.
        <img
          src={visibleUrl}
          alt=""
          loading="lazy"
          decoding="async"
          onError={() => setFailedUrl(visibleUrl)}
          className="bpp-history-run-preview-image"
        />
      ) : (
        <>
          <span className="bpp-history-run-preview-empty" />
          <ImageIcon size={19} aria-hidden="true" />
        </>
      )}
    </div>
  );
}

function HistoryProblemBanner({
  problem,
  onRetry,
  onEndGameProcess,
  endingGameProcess
}: {
  problem: HistoryPageProblem;
  onRetry: () => void;
  onEndGameProcess: () => Promise<EndGameProcessOutcome>;
  endingGameProcess: boolean;
}) {
  const { t } = useI18n();
  const { showToast } = useToast();
  const diagnostic = problem.diagnostic
    ? formatProblemDiagnostic(problem)
    : null;

  const endGameProcess = async () => {
    const outcome = await onEndGameProcess();
    showToast({
      tone: outcome === 'failed' ? 'error' : 'success',
      message: t(endGameProcessMessageKey(outcome))
    });
  };

  return (
    <ProblemBanner
      message={presentHistoryProblem(problem, t)}
      diagnostic={diagnostic}
      diagnosticLabel={t('problemDiagnostics')}
      actions={
        <>
          {problem.code === 'history_unavailable' && (
            <Link to="/" className="underline underline-offset-2">
              {t('historyOpenInstall')}
            </Link>
          )}
          {problem.code === 'history_read_blocked_by_game' && (
            <button
              type="button"
              onClick={() => void endGameProcess()}
              disabled={endingGameProcess}
              className="underline underline-offset-2 disabled:opacity-60"
            >
              {t('historyEndGameProcess')}
            </button>
          )}
          <button
            type="button"
            onClick={onRetry}
            className="underline underline-offset-2"
          >
            {t('retry')}
          </button>
        </>
      }
    />
  );
}

function endGameProcessMessageKey(outcome: EndGameProcessOutcome): MessageKey {
  switch (outcome) {
    case 'terminated':
      return 'historyEndGameProcessDone';
    case 'already-exited':
      return 'historyEndGameProcessNotFound';
    case 'failed':
      // The recovery step names a real OS surface, so it has to match the host.
      return isWindowsPlatform()
        ? 'historyEndGameProcessFailedWindows'
        : 'historyEndGameProcessFailedMac';
  }
}

function HistoryPreviewProblemBanner({
  problem
}: {
  problem: HistoryPageProblem;
}) {
  const { t } = useI18n();
  return (
    <ProblemBanner
      tone="warning"
      message={presentHistoryProblem(problem, t)}
      diagnostic={problem.diagnostic ? formatProblemDiagnostic(problem) : null}
      diagnosticLabel={t('problemDiagnostics')}
      actions={
        <Link to="/stream" className="underline underline-offset-2">
          {t('historyOpenStream')}
        </Link>
      }
    />
  );
}

function Metric({
  label,
  value,
  gold = false,
  fira = false
}: {
  label: string;
  value: string;
  gold?: boolean;
  fira?: boolean;
}) {
  return (
    <div className="bpp-history-run-metric">
      <span className="bpp-history-run-metric-label cinzel">{label}</span>
      <span
        className={`bpp-history-run-metric-value ${fira ? 'fira-code' : 'cinzel'} ${gold ? 'is-gold' : ''}`}
      >
        {value}
      </span>
    </div>
  );
}
