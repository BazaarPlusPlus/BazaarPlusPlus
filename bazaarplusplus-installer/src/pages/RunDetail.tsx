import {
  ArrowLeft,
  FileQuestion,
  FolderOpen,
  Image as ImageIcon,
  Loader2,
  RefreshCw,
  Trash2,
  Video
} from 'lucide-react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { Button } from '../components/ui/Button';
import { ConfirmDialog } from '../components/ui/ConfirmDialog';
import { EmptyState } from '../components/ui/EmptyState';
import { LoadingPanel } from '../components/ui/LoadingPanel';
import { PageShell } from '../components/ui/PageShell';
import { ProblemBanner } from '../components/ui/ProblemBanner';
import type { HistoryBattleRow } from '../types/backend';
import { useRunDetailPage } from '../features/history/useRunDetailPage';
import {
  formatBattleResult,
  formatBytes,
  formatDateTime,
  formatDuration,
  formatGameMode,
  formatRunResultLabel,
  formatRunStatusKey,
  toneColorClass
} from '../features/history/format';
import {
  presentRunDetailProblem,
  runDetailProblemFromError,
  type RunDetailProblem
} from '../features/history/runDetailProblems';
import { formatProblemDiagnostic } from '../features/shared/problems';
import { useConfirmedOperation } from '../features/shared/confirmedOperation';
import { useI18n } from '../i18n/LocaleProvider';
import { ModalSource } from '../components/ui/ModalCoordinator';
import {
  historyListPath,
  parseHistoryPage
} from '../features/history/pagination';
import {
  VideoDeleteSummary,
  type VideoDeleteTarget
} from '../features/history/VideoDeleteSummary';

export default function RunDetail() {
  const navigate = useNavigate();
  const location = useLocation();
  const historyPage = parseHistoryPage(
    String(location.state?.historyPage ?? 1)
  );
  const page = useRunDetailPage();
  const detail = page.detail;
  const { locale, t } = useI18n();
  const runResult = detail ? formatRunResultLabel(detail.run) : null;
  const deleteOperation = useConfirmedOperation<
    VideoDeleteTarget,
    RunDetailProblem
  >();
  const pendingDelete = deleteOperation.state?.target ?? null;
  const screenshotAvailability = page.screenshot;
  const screenshotFailure = page.screenshot.problem;

  const confirmDelete = () =>
    deleteOperation.controller.run(
      (target) => page.deleteVideo(target.battleId, target.videoId),
      runDetailProblemFromError
    );

  // Only the loading phase may claim to be loading; every terminal phase
  // without a run needs a heading that matches what the body actually says.
  const pageTitle =
    detail?.run.hero ??
    (page.state.phase === 'initial-loading'
      ? t('runDetailLoading')
      : page.state.phase === 'not-found'
        ? t('runDetailNotFound')
        : t('runDetailUnavailable'));

  return (
    <PageShell
      eyebrow={t('runDetailEyebrow')}
      title={pageTitle}
      className="bpp-run-detail-page pb-8"
      action={
        <div className="flex items-center gap-2">
          <Button
            type="button"
            onClick={() => navigate(historyListPath(historyPage))}
          >
            <ArrowLeft size={16} />
            {t('runDetailBack')}
          </Button>
          <Button
            type="button"
            onClick={() => void page.refresh()}
            disabled={page.busy}
            busy={page.refreshing}
          >
            <RefreshCw
              size={14}
              className={page.refreshing ? 'animate-spin' : ''}
            />
            {t('refresh')}
          </Button>
        </div>
      }
    >
      {page.state.phase === 'initial-loading' ? (
        <LoadingPanel label={t('runDetailLoading')} className="h-64" />
      ) : page.state.phase === 'not-found' ? (
        <div
          role="status"
          className="bpp-run-detail-not-found p-6 flex items-center justify-between gap-4"
        >
          <span>{t('runDetailNotFound')}</span>
          <button
            type="button"
            onClick={() => void page.refresh()}
            className="bpp-run-detail-inline-link underline underline-offset-2"
          >
            {t('retry')}
          </button>
        </div>
      ) : page.state.phase === 'blocking-failure' ? (
        <RunDetailProblemBanner
          problem={page.state.problem}
          onRetry={() => void page.refresh()}
        />
      ) : detail ? (
        <>
          {page.state.refresh.phase === 'failed' && (
            <RunDetailProblemBanner
              problem={page.state.refresh.problem}
              onRetry={() => void page.refresh()}
            />
          )}

          {page.state.refresh.phase === 'refreshing' && (
            <div
              role="status"
              aria-live="polite"
              className="bpp-run-detail-refreshing flex items-center gap-2 text-xs"
            >
              <Loader2 size={14} className="animate-spin" />
              {t('runDetailRefreshing')}
            </div>
          )}

          <div className="bpp-panel bpp-run-detail-summary">
            <div className="bpp-run-detail-overview">
              <div className="flex flex-col gap-3 min-w-0">
                <div className="flex flex-wrap items-center gap-3">
                  <span
                    className="bpp-run-outcome"
                    data-tier={runResult?.tier}
                    data-state={runResult?.state}
                  >
                    {runResult ? t(runResult.key) : '-'}
                  </span>
                  <span className="bpp-run-detail-meta text-xs">
                    {formatGameMode(detail.run.game_mode, t)} ·{' '}
                    {t(formatRunStatusKey(detail.run.status))}
                  </span>
                </div>
                <div className="bpp-run-detail-meta flex flex-wrap items-center gap-x-4 gap-y-1 text-xs selectable">
                  <span className="break-words">
                    {t('runDetailPlayer')} {detail.run.player_name ?? '-'}
                  </span>
                  <span className="fira-code">
                    {formatDateTime(detail.run.started_at_utc, locale)} -{' '}
                    {formatDateTime(detail.run.ended_at_utc, locale)}
                  </span>
                </div>
              </div>

              <button
                type="button"
                disabled={
                  !detail.run.screenshot_id || screenshotAvailability.disabled
                }
                onClick={() => void page.revealScreenshot()}
                className="bpp-button"
              >
                {screenshotAvailability.running ? (
                  <Loader2 size={16} className="animate-spin" />
                ) : (
                  <ImageIcon size={16} />
                )}{' '}
                {t('openScreenshotLocation')}
              </button>
            </div>

            {screenshotFailure && (
              <RunDetailProblemBanner
                problem={screenshotFailure}
                onRetry={() => void page.revealScreenshot()}
              />
            )}

            <div className="bpp-run-detail-stats">
              <StatBlock
                label={t('statWinLoss')}
                value={`${detail.run.victories ?? '-'} / ${detail.run.losses ?? '-'}`}
              />
              <StatBlock
                label={t('statFinalDay')}
                value={
                  detail.run.final_day === null
                    ? '-'
                    : String(detail.run.final_day)
                }
              />
              <StatBlock
                label={t('statFinalRank')}
                value={detail.run.final_player_rank ?? '-'}
                isText
              />
              <StatBlock
                label={t('statFinalRating')}
                value={
                  detail.run.final_player_rating === null
                    ? '-'
                    : String(detail.run.final_player_rating)
                }
              />
            </div>
          </div>

          <div className="bpp-panel overflow-hidden">
            <div className="overflow-x-auto custom-scrollbar">
              <table className="bpp-battle-table">
                <colgroup>
                  <col style={{ width: '7%' }} />
                  <col style={{ width: '9%' }} />
                  <col style={{ width: '17%' }} />
                  <col style={{ width: '21%' }} />
                  <col style={{ width: '15%' }} />
                  <col style={{ width: '10%' }} />
                  <col style={{ width: '21%' }} />
                </colgroup>
                <thead className="bpp-battle-table-header cinzel">
                  <tr>
                    <th scope="col">{t('battleColDay')}</th>
                    <th scope="col">{t('battleColResult')}</th>
                    <th scope="col">{t('battleColOpponentHero')}</th>
                    <th scope="col">{t('battleColOpponentPlayer')}</th>
                    <th scope="col">{t('battleColRank')}</th>
                    <th scope="col">{t('battleColRating')}</th>
                    <th scope="col" className="text-right">
                      {t('battleColVideo')}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {detail.battles.length === 0 ? (
                    <tr>
                      <td colSpan={7} className="px-6 py-6">
                        <EmptyState
                          icon={<Video size={24} />}
                          heading={t('noLocalBattles')}
                          description={t('noLocalBattlesDescription')}
                          primaryAction={
                            <Button
                              type="button"
                              onClick={() => void page.refresh()}
                              disabled={page.busy}
                              busy={page.busy}
                            >
                              <RefreshCw
                                size={16}
                                className={
                                  page.refreshing ? 'animate-spin' : ''
                                }
                              />
                              {t('refresh')}
                            </Button>
                          }
                        />
                      </td>
                    </tr>
                  ) : (
                    detail.battles.map((battle) => (
                      <BattleRow
                        key={battle.battle_id}
                        battle={battle}
                        page={page}
                        onRequestDelete={(target) => {
                          if (!target.video) return;
                          deleteOperation.controller.request({
                            kind: 'delete-video',
                            battleId: target.battle_id,
                            videoId: target.video.video_id,
                            runStartedAt: detail.run.started_at_utc,
                            day: target.day,
                            opponent:
                              [target.opponent_name, target.opponent_hero]
                                .filter(Boolean)
                                .join(' · ') || '-',
                            durationMs: target.video.duration_ms
                          });
                        }}
                      />
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </>
      ) : null}

      <ModalSource
        id="route:delete-video"
        open={pendingDelete !== null}
        priority={
          deleteOperation.state?.phase === 'running'
            ? 'critical'
            : 'confirmation'
        }
        dismissalPolicy={
          deleteOperation.state?.phase === 'running' ? 'blocked' : 'dismissible'
        }
      >
        {pendingDelete && (
          <ConfirmDialog
            titleId="delete-video-modal-title"
            title={t('deleteVideoConfirmTitle')}
            tone="danger"
            confirmLabel={
              deleteOperation.state?.phase === 'failed'
                ? t('retry')
                : t('deleteVideoConfirmAction')
            }
            busyLabel={t('deleteVideoRunning')}
            busy={deleteOperation.state?.phase === 'running'}
            activeDismissalPolicy={{ kind: 'blocked' }}
            dismissLabel={
              deleteOperation.state?.phase === 'failed' ? t('close') : undefined
            }
            onConfirm={confirmDelete}
            onClose={deleteOperation.controller.dismiss}
          >
            <VideoDeleteSummary target={pendingDelete} />
            <p className="bpp-confirm-danger-copy m-0 text-[13px] leading-relaxed">
              {t('deleteVideoConfirmBody')}
            </p>
            {deleteOperation.state?.phase === 'failed' && (
              <RunDetailProblemBanner problem={deleteOperation.state.problem} />
            )}
          </ConfirmDialog>
        )}
      </ModalSource>
    </PageShell>
  );
}

function StatBlock({
  label,
  value,
  isText = false
}: {
  label: string;
  value: string;
  isText?: boolean;
}) {
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <span className="bpp-run-detail-stat-label cinzel text-[11px] tracking-wide uppercase">
        {label}
      </span>
      <span
        title={value}
        className={`bpp-run-detail-stat-value text-lg break-words ${isText ? 'cinzel font-bold' : 'fira-code'}`}
      >
        {value}
      </span>
    </div>
  );
}

function BattleRow({
  battle,
  page,
  onRequestDelete
}: {
  battle: HistoryBattleRow;
  page: ReturnType<typeof useRunDetailPage>;
  onRequestDelete: (battle: HistoryBattleRow) => void;
}) {
  const { locale, t } = useI18n();
  const battleResult = formatBattleResult(battle.result);
  const {
    reveal: videoAvailability,
    delete: deleteAvailability,
    failure
  } = page.battles[battle.battle_id];
  const actionLabel = (action: string) =>
    t('battleVideoActionLabel', {
      action,
      day: battle.day ?? '-',
      opponent: battle.opponent_name ?? battle.opponent_hero ?? '-'
    });

  const retryFailure = () => {
    if (!battle.video || !failure) return;
    if (failure.action === 'reveal') {
      void page.revealVideo(battle.battle_id, battle.video.video_id);
      return;
    }
    onRequestDelete(battle);
  };

  return (
    <>
      <tr className="bpp-battle-row transition-colors">
        <td className="bpp-battle-primary-data fira-code">
          {battle.day === null ? '-' : String(battle.day)}
        </td>
        <td className={`cinzel font-bold ${toneColorClass(battleResult.tone)}`}>
          {t(battleResult.key)}
        </td>
        <td
          className="bpp-battle-hero cinzel"
          title={battle.opponent_hero ?? undefined}
        >
          {battle.opponent_hero ?? '-'}
        </td>
        <td
          className="bpp-battle-secondary-data fira-code"
          title={battle.opponent_name ?? undefined}
        >
          {battle.opponent_name ?? '-'}
        </td>
        <td
          className="bpp-battle-rank cinzel"
          title={battle.opponent_rank ?? undefined}
        >
          {battle.opponent_rank ?? '-'}
        </td>
        <td className="bpp-battle-primary-data fira-code">
          {battle.opponent_rating === null ? '-' : battle.opponent_rating}
        </td>

        <td>
          <div className="flex flex-col items-end gap-1">
            {battle.video ? (
              <>
                <div className="flex items-center justify-end gap-1.5">
                  <button
                    type="button"
                    disabled={videoAvailability.disabled}
                    onClick={() =>
                      void page.revealVideo(
                        battle.battle_id,
                        battle.video?.video_id
                      )
                    }
                    title={t('openVideoLocation')}
                    aria-label={actionLabel(t('openVideoLocation'))}
                    className="bpp-battle-video-action flex items-center justify-center size-9 disabled:opacity-50 transition-colors"
                  >
                    {videoAvailability.running ? (
                      <Loader2 size={14} className="animate-spin" />
                    ) : (
                      <FolderOpen size={14} />
                    )}
                  </button>
                  <button
                    type="button"
                    disabled={deleteAvailability.disabled}
                    onClick={() => battle.video && onRequestDelete(battle)}
                    title={t('deleteVideo')}
                    aria-label={actionLabel(t('deleteVideo'))}
                    className="bpp-battle-delete-action flex items-center justify-center size-9 disabled:opacity-50 transition-colors"
                  >
                    {deleteAvailability.running ? (
                      <Loader2 size={14} className="animate-spin" />
                    ) : (
                      <Trash2 size={14} />
                    )}
                  </button>
                </div>
                <span className="bpp-battle-video-meta fira-code text-[11px] text-right selectable">
                  {formatDuration(battle.video.duration_ms, locale)} ·{' '}
                  {formatBytes(battle.video.file_size_bytes, locale)}
                </span>
              </>
            ) : (
              <span
                title={t('noVideo')}
                aria-label={t('noVideo')}
                className="bpp-battle-no-video flex items-center justify-center size-9"
              >
                <FileQuestion size={14} />
              </span>
            )}
          </div>
        </td>
      </tr>

      {failure && (
        <tr>
          <td colSpan={7} className="px-6 pb-4">
            <RunDetailProblemBanner
              problem={failure.problem}
              onRetry={retryFailure}
            />
          </td>
        </tr>
      )}
    </>
  );
}

function RunDetailProblemBanner({
  problem,
  onRetry
}: {
  problem: RunDetailProblem;
  onRetry?: () => void;
}) {
  const { t } = useI18n();
  return (
    <ProblemBanner
      message={presentRunDetailProblem(problem, t)}
      diagnostic={problem.diagnostic ? formatProblemDiagnostic(problem) : null}
      diagnosticLabel={t('problemDiagnostics')}
      actions={
        problem.code === 'history_unavailable' || onRetry ? (
          <>
            {problem.code === 'history_unavailable' && (
              <Link to="/" className="underline underline-offset-2">
                {t('historyOpenInstall')}
              </Link>
            )}
            {onRetry && (
              <button
                type="button"
                onClick={onRetry}
                className="underline underline-offset-2"
              >
                {t('retry')}
              </button>
            )}
          </>
        ) : undefined
      }
    />
  );
}
