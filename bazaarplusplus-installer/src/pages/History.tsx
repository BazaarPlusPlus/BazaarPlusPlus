import { useEffect, useState } from 'react';
import {
  ChevronRight,
  History as HistoryIcon,
  Image as ImageIcon,
  RefreshCw
} from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';
import { Button } from '../components/ui/Button';
import { EmptyState } from '../components/ui/EmptyState';
import { LoadingPanel } from '../components/ui/LoadingPanel';
import { PageShell } from '../components/ui/PageShell';
import { ProblemBanner } from '../components/ui/ProblemBanner';
import {
  formatDateTime,
  formatGameMode,
  formatRunResultLabel
} from '../features/history/format';
import { HistoryOverview } from '../features/history/HistoryOverview';
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
import {
  HISTORY_PAGE_SIZE,
  parseHistoryPage
} from '../features/history/pagination';

export default function History() {
  const [searchParams, setSearchParams] = useSearchParams();
  const pageNumber = parseHistoryPage(searchParams.get('page'));
  const page = useHistoryPage(pageNumber);
  const { t } = useI18n();
  const totalRuns = 'data' in page.state ? page.state.data.summary.runs : null;
  const pageCount =
    totalRuns === null
      ? pageNumber
      : Math.max(1, Math.ceil(totalRuns / HISTORY_PAGE_SIZE));

  const goToPage = (number: number) => {
    setSearchParams(number === 1 ? {} : { page: String(number) });
  };

  useEffect(() => {
    if (totalRuns !== null && pageNumber > pageCount) {
      setSearchParams(pageCount === 1 ? {} : { page: String(pageCount) }, {
        replace: true
      });
    }
  }, [pageCount, pageNumber, setSearchParams, totalRuns]);

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
          <HistoryOverview
            summary={page.state.data.summary}
            onCompleted={page.refresh}
          />

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

          <div className="bpp-history-run-list flex flex-col gap-3">
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
                  pageNumber={pageNumber}
                  previewUrl={page.previewUrl(run)}
                  previewProblem={page.previewProblem}
                />
              ))
            )}
          </div>
          {page.state.phase === 'ready-content' && (
            <nav
              className="bpp-history-pagination"
              aria-label={t('historyPagination')}
            >
              <span role="status" aria-live="polite">
                {t('historyPageRange', {
                  start: (pageNumber - 1) * HISTORY_PAGE_SIZE + 1,
                  end:
                    (pageNumber - 1) * HISTORY_PAGE_SIZE +
                    page.state.data.runs.length,
                  total: page.state.data.summary.runs
                })}
              </span>
              <div className="flex items-center gap-3">
                <Button
                  disabled={page.busy || pageNumber <= 1}
                  onClick={() => goToPage(pageNumber - 1)}
                >
                  {t('historyPreviousPage')}
                </Button>
                <span>
                  {t('historyPageNumber', {
                    page: pageNumber,
                    total: pageCount
                  })}
                </span>
                <Button
                  disabled={page.busy || pageNumber >= pageCount}
                  onClick={() => goToPage(pageNumber + 1)}
                >
                  {t('historyNextPage')}
                </Button>
              </div>
            </nav>
          )}
        </div>
      )}
    </PageShell>
  );
}

function RunRow({
  run,
  pageNumber,
  previewUrl,
  previewProblem
}: {
  run: HistoryRunRow;
  pageNumber: number;
  previewUrl: string | null;
  previewProblem: HistoryPageProblem | null;
}) {
  const { locale, t } = useI18n();
  const result = formatRunResultLabel(run);
  const detailPath = `/history/${encodeURIComponent(run.run_id)}`;
  const fallbackLabel = previewProblem
    ? t('historyPreviewServiceOffline')
    : t('historyPreviewFallback');

  return (
    <Link
      to={detailPath}
      state={{ historyPage: pageNumber }}
      className="bpp-history-run-card group"
    >
      <RunPreview
        key={previewUrl ?? 'preview-unavailable'}
        previewUrl={previewUrl}
        fallbackLabel={fallbackLabel}
      />

      <div className="bpp-history-run-data">
        <div className="bpp-history-run-heading">
          <div className="bpp-history-run-identity">
            <span className="bpp-history-run-hero cinzel" title={run.hero}>
              {run.hero}
            </span>
            <span className="bpp-history-run-meta">
              <span className="bpp-history-run-date fira-code">
                {formatDateTime(run.started_at_utc, locale)}
              </span>
              <span className="bpp-history-run-mode">
                {formatGameMode(run.game_mode, t)}
              </span>
            </span>
          </div>

          <span
            className="bpp-history-run-result bpp-run-outcome"
            data-tier={result.tier}
            data-state={result.state}
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
        title={value}
        className={`bpp-history-run-metric-value ${fira ? 'fira-code' : 'cinzel'} ${gold ? 'is-gold' : ''}`}
      >
        {value}
      </span>
    </div>
  );
}
