import { useState } from 'react';
import {
  ChevronRight,
  History as HistoryIcon,
  Image as ImageIcon,
  ImageOff,
  RefreshCw
} from 'lucide-react';
import { Link } from 'react-router-dom';
import { Button, buttonClassName } from '../components/ui/Button';
import { EmptyState } from '../components/ui/EmptyState';
import { LoadingPanel } from '../components/ui/LoadingPanel';
import { PageShell } from '../components/ui/PageShell';
import { ProblemBanner } from '../components/ui/ProblemBanner';
import { StatusBanner } from '../components/ui/StatusBanner';
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
import { useHistoryPage } from '../features/history/useHistoryPage';
import type {
  EndGameProcessOutcome,
  HistoryThumbnails
} from '../features/history/historyListWorkflow';
import { hostPlatform } from '../features/shared/platform';
import { useToast } from '../components/ui/Toast';
import { useI18n } from '../i18n/LocaleProvider';
import type { MessageKey } from '../i18n/messages';
import type { HistoryRunRow } from '../types/backend';

export default function History() {
  const page = useHistoryPage();
  const { t } = useI18n();
  const { pagination } = page;

  return (
    <PageShell
      title={t('historyTitle')}
      action={
        <Button
          variant="ghost"
          size="sm"
          onClick={page.refresh}
          busy={page.busy}
          icon={<RefreshCw />}
        >
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
        <>
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
            page.thumbnailsUnavailable && (
              <StatusBanner
                tone="warning"
                icon={<ImageOff size={16} />}
                message={
                  <p className="m-0">
                    {t('historyProblemThumbnailsUnavailable')}
                  </p>
                }
              />
            )}

          <div className="bpp-history-run-list">
            {page.state.phase === 'ready-empty' ? (
              <div className="bpp-panel">
                <EmptyState
                  icon={<HistoryIcon size={24} />}
                  heading={t('noLocalRuns')}
                  description={t('historyEmptyDescription')}
                  primaryAction={
                    <Button
                      size="sm"
                      busy={page.busy}
                      onClick={page.refresh}
                      icon={<RefreshCw />}
                    >
                      {t('historyEmptyRefresh')}
                    </Button>
                  }
                  secondaryAction={
                    <Link
                      to="/"
                      className={buttonClassName({
                        variant: 'ghost',
                        size: 'sm'
                      })}
                    >
                      {t('historyEmptyInstall')}
                    </Link>
                  }
                />
              </div>
            ) : (
              page.state.data.runs.map((run: HistoryRunRow) => (
                <RunRow
                  key={run.run_id}
                  run={run}
                  pageNumber={pagination.page}
                  thumbnailUrl={page.thumbnailUrl(run)}
                  thumbnailAttempt={page.thumbnailAttempt}
                  thumbnails={page.thumbnails}
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
                  start: pagination.start,
                  end: pagination.end,
                  total: pagination.total ?? 0
                })}
              </span>
              <div className="flex items-center gap-3">
                <Button
                  size="sm"
                  disabled={pagination.previousDisabled}
                  onClick={() => page.goToPage(pagination.page - 1)}
                >
                  {t('historyPreviousPage')}
                </Button>
                <span>
                  {t('historyPageNumber', {
                    page: pagination.page,
                    total: pagination.pageCount
                  })}
                </span>
                <Button
                  size="sm"
                  disabled={pagination.nextDisabled}
                  onClick={() => page.goToPage(pagination.page + 1)}
                >
                  {t('historyNextPage')}
                </Button>
              </div>
            </nav>
          )}
        </>
      )}
    </PageShell>
  );
}

function RunRow({
  run,
  pageNumber,
  thumbnailUrl,
  thumbnailAttempt,
  thumbnails
}: {
  run: HistoryRunRow;
  pageNumber: number;
  thumbnailUrl: string | null;
  thumbnailAttempt: number;
  thumbnails: HistoryThumbnails;
}) {
  const { locale, t } = useI18n();
  const result = formatRunResultLabel(run);
  const detailPath = `/history/${encodeURIComponent(run.run_id)}`;
  const fallbackLabel = t(
    !run.screenshot_id
      ? 'historyPreviewEmpty'
      : thumbnails === 'pending'
        ? 'historyPreviewLoading'
        : 'historyPreviewFallback'
  );

  return (
    <Link
      to={detailPath}
      state={{ historyPage: pageNumber }}
      className="bpp-history-run-card"
    >
      <RunThumbnail
        key={thumbnailUrl ?? 'thumbnail-unavailable'}
        thumbnailUrl={thumbnailUrl}
        thumbnailAttempt={thumbnailAttempt}
        fallbackLabel={fallbackLabel}
      />
      <span className="bpp-history-run-info">
        <span className="bpp-history-run-title">
          <span className="bpp-history-run-hero">{run.hero}</span>
          <ChevronRight
            size={16}
            className="bpp-history-run-chevron"
            aria-label={t('viewDetail')}
          />
        </span>
        <span className="bpp-history-run-metrics">
          <span className="bpp-history-run-metric">
            <strong className="bpp-history-run-metric-value bpp-history-run-wins">
              {run.victories ?? '—'}
            </strong>
            <span className="bpp-history-run-metric-label">
              {t('runMetricWins')}
            </span>
            <span
              className="bpp-outcome"
              data-tier={result.tier}
              data-state={result.state}
            >
              {t(result.key)}
            </span>
          </span>
          <span
            className="bpp-history-run-metric"
            title={`${t('runStatRank')} ${run.final_player_rank ?? '—'} · ${t('runStatRating')} ${run.final_player_rating ?? '—'}`}
          >
            <strong className="bpp-history-run-metric-value">
              {run.final_player_rating ?? '—'}
            </strong>
            <span className="bpp-history-run-metric-label">
              {run.final_player_rank ?? t('runStatRating')}
            </span>
            <span className="bpp-history-run-days">
              {t('runMetricDays')} {run.final_day ?? '—'}
            </span>
          </span>
        </span>
        <span className="bpp-history-run-footer">
          <time className="bpp-history-run-when" dateTime={run.started_at_utc}>
            {formatDateTime(run.started_at_utc, locale)}
          </time>
          <span>{formatGameMode(run.game_mode, t)}</span>
        </span>
      </span>
    </Link>
  );
}

function RunThumbnail({
  thumbnailUrl,
  thumbnailAttempt,
  fallbackLabel
}: {
  thumbnailUrl: string | null;
  thumbnailAttempt: number;
  fallbackLabel: string;
}) {
  // A failed image retries after the next thumbnail preparation, not on every render.
  const [failed, setFailed] = useState<{
    url: string;
    attempt: number;
  } | null>(null);
  const visibleUrl =
    thumbnailUrl &&
    !(failed?.url === thumbnailUrl && failed.attempt === thumbnailAttempt)
      ? thumbnailUrl
      : null;

  return (
    <div className="bpp-history-run-preview">
      {visibleUrl ? (
        // Preserve the complete server-generated strip. Its rounded crop
        // dimensions can vary by a few pixels between source resolutions.
        <img
          src={visibleUrl}
          alt=""
          loading="lazy"
          decoding="async"
          onError={() =>
            setFailed({ url: visibleUrl, attempt: thumbnailAttempt })
          }
          className="bpp-history-run-preview-image"
        />
      ) : (
        <span className="bpp-history-run-preview-empty">
          <ImageIcon size={18} aria-hidden="true" />
          <span>{fallbackLabel}</span>
        </span>
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
      problem={problem}
      onRetry={onRetry}
      actions={
        <>
          {problem.code === 'history_unavailable' && (
            <Link to="/" className={buttonClassName({ size: 'sm' })}>
              {t('historyOpenInstall')}
            </Link>
          )}
          {problem.code === 'history_read_blocked_by_game' && (
            <Button
              size="sm"
              variant="danger"
              onClick={() => void endGameProcess()}
              busy={endingGameProcess}
            >
              {t('historyEndGameProcess')}
            </Button>
          )}
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
      return endGameProcessFailedMessageKey();
  }
}

function endGameProcessFailedMessageKey(): MessageKey {
  switch (hostPlatform()) {
    case 'windows':
      return 'historyEndGameProcessFailedWindows';
    case 'macos':
      return 'historyEndGameProcessFailedMac';
    case 'linux':
      return 'historyEndGameProcessFailedLinux';
  }
}
