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
import { Button, buttonClassName } from '../components/ui/Button';
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
      title={pageTitle}
      leading={
        <Button
          variant="ghost"
          size="icon"
          onClick={() => navigate(historyListPath(historyPage))}
          title={t('runDetailBack')}
          aria-label={t('runDetailBack')}
          icon={<ArrowLeft />}
        />
      }
      meta={
        detail
          ? `${formatGameMode(detail.run.game_mode, t)} · ${t(formatRunStatusKey(detail.run.status))}`
          : undefined
      }
      action={
        <Button
          variant="ghost"
          size="sm"
          onClick={() => void page.refresh()}
          disabled={page.busy}
          busy={page.refreshing}
          icon={<RefreshCw />}
        >
          {t('refresh')}
        </Button>
      }
    >
      {page.state.phase === 'initial-loading' ? (
        <LoadingPanel label={t('runDetailLoading')} className="h-64" />
      ) : page.state.phase === 'not-found' ? (
        <div role="status" className="bpp-panel">
          <EmptyState
            icon={<FileQuestion size={22} />}
            heading={t('runDetailNotFound')}
            primaryAction={
              <Button size="sm" onClick={() => void page.refresh()}>
                {t('retry')}
              </Button>
            }
          />
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
            <p role="status" aria-live="polite" className="bpp-inline-status">
              <Loader2 size={13} className="bpp-spin" aria-hidden="true" />
              {t('runDetailRefreshing')}
            </p>
          )}

          <section className="bpp-panel">
            <div className="bpp-row">
              <span
                className="bpp-outcome"
                data-tier={runResult?.tier}
                data-state={runResult?.state}
              >
                {runResult ? t(runResult.key) : '-'}
              </span>
              <div className="bpp-row-copy selectable">
                <span className="bpp-row-title">
                  {t('runDetailPlayer')} {detail.run.player_name ?? '-'}
                </span>
                <span className="bpp-row-description tnum">
                  {formatDateTime(detail.run.started_at_utc, locale)} –{' '}
                  {formatDateTime(detail.run.ended_at_utc, locale)}
                </span>
              </div>
              <Button
                size="sm"
                disabled={
                  !detail.run.screenshot_id || screenshotAvailability.disabled
                }
                busy={screenshotAvailability.running}
                onClick={() => void page.revealScreenshot()}
                icon={<ImageIcon />}
              >
                {t('openScreenshotLocation')}
              </Button>
            </div>

            {screenshotFailure && (
              <div className="px-4 pb-3">
                <RunDetailProblemBanner
                  problem={screenshotFailure}
                  onRetry={() => void page.revealScreenshot()}
                />
              </div>
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
          </section>

          <div className="bpp-panel overflow-hidden">
            <div className="overflow-x-auto">
              <table className="bpp-table">
                <colgroup>
                  <col style={{ width: '7%' }} />
                  <col style={{ width: '9%' }} />
                  <col style={{ width: '17%' }} />
                  <col style={{ width: '21%' }} />
                  <col style={{ width: '15%' }} />
                  <col style={{ width: '10%' }} />
                  <col style={{ width: '21%' }} />
                </colgroup>
                <thead>
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
                      <td colSpan={7}>
                        <EmptyState
                          icon={<Video size={24} />}
                          heading={t('noLocalBattles')}
                          description={t('noLocalBattlesDescription')}
                          primaryAction={
                            <Button
                              size="sm"
                              onClick={() => void page.refresh()}
                              disabled={page.busy}
                              busy={page.refreshing}
                              icon={<RefreshCw />}
                            >
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
            <p>{t('deleteVideoConfirmBody')}</p>
            {deleteOperation.state?.phase === 'failed' && (
              <RunDetailProblemBanner problem={deleteOperation.state.problem} />
            )}
          </ConfirmDialog>
        )}
      </ModalSource>
    </PageShell>
  );
}

function StatBlock({ label, value }: { label: string; value: string }) {
  return (
    <div className="bpp-run-detail-stat">
      <span className="bpp-run-detail-stat-label">{label}</span>
      <span title={value} className="bpp-run-detail-stat-value">
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
      <tr>
        <td className="is-num">
          {battle.day === null ? '-' : String(battle.day)}
        </td>
        <td className={`font-semibold ${toneColorClass(battleResult.tone)}`}>
          {t(battleResult.key)}
        </td>
        <td className="font-medium" title={battle.opponent_hero ?? undefined}>
          {battle.opponent_hero ?? '-'}
        </td>
        <td className="is-muted" title={battle.opponent_name ?? undefined}>
          {battle.opponent_name ?? '-'}
        </td>
        <td className="is-muted" title={battle.opponent_rank ?? undefined}>
          {battle.opponent_rank ?? '-'}
        </td>
        <td className="is-num">
          {battle.opponent_rating === null ? '-' : battle.opponent_rating}
        </td>

        <td>
          {battle.video ? (
            <div className="flex items-center justify-end gap-2">
              <span className="text-fg-3 tnum text-xs selectable">
                {formatDuration(battle.video.duration_ms, locale)} ·{' '}
                {formatBytes(battle.video.file_size_bytes, locale)}
              </span>
              <Button
                variant="ghost"
                size="icon-sm"
                disabled={videoAvailability.disabled}
                busy={videoAvailability.running}
                onClick={() =>
                  void page.revealVideo(
                    battle.battle_id,
                    battle.video?.video_id
                  )
                }
                title={t('openVideoLocation')}
                aria-label={actionLabel(t('openVideoLocation'))}
                icon={<FolderOpen />}
              />
              <Button
                variant="ghost"
                size="icon-sm"
                disabled={deleteAvailability.disabled}
                busy={deleteAvailability.running}
                onClick={() => battle.video && onRequestDelete(battle)}
                title={t('deleteVideo')}
                aria-label={actionLabel(t('deleteVideo'))}
                icon={<Trash2 />}
              />
            </div>
          ) : (
            <span
              title={t('noVideo')}
              aria-label={t('noVideo')}
              className="flex justify-end text-fg-3"
            >
              <FileQuestion size={14} />
            </span>
          )}
        </td>
      </tr>

      {failure && (
        <tr>
          <td colSpan={7}>
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
      problem={problem}
      onRetry={onRetry}
      actions={
        problem.code === 'history_unavailable' ? (
          <Link to="/" className={buttonClassName({ size: 'sm' })}>
            {t('historyOpenInstall')}
          </Link>
        ) : undefined
      }
    />
  );
}
