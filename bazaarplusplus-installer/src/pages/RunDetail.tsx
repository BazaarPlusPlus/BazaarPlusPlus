import {
  ArrowLeft,
  FileQuestion,
  Image as ImageIcon,
  Loader2,
  RefreshCw,
  Trash2,
  Video
} from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';
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

// Shared 7-track grid for the battle table header + rows so columns align and
// the action/metadata column does not reflow when the hover-only delete button
// appears. Day · Result · OppHero · OppPlayer · Rank · Rating · Video.
const BATTLE_GRID =
  'grid grid-cols-[3.5rem_4.5rem_minmax(0,1fr)_minmax(0,1fr)_5rem_5rem_9rem] gap-4';

export default function RunDetail() {
  const navigate = useNavigate();
  const page = useRunDetailPage();
  const detail = page.detail;
  const { locale, t } = useI18n();
  const runResult = detail ? formatRunResultLabel(detail.run.result) : null;
  const deleteOperation = useConfirmedOperation<
    { kind: 'delete-video'; battleId: string; videoId: string },
    RunDetailProblem
  >();
  const pendingDelete = deleteOperation.state?.target ?? null;
  const screenshotAvailability = page.availability('screenshot');
  const screenshotFailure = page.problemFor('screenshot');

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
      className="h-full overflow-hidden pb-8"
      action={
        <div className="flex items-center gap-2">
          <Button type="button" onClick={() => navigate('/history')}>
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

          <div className="bpp-panel flex flex-col gap-6 p-6">
            <div className="flex justify-between items-start">
              <div className="flex flex-col gap-1 min-w-0">
                <h2 className="bpp-run-detail-title cinzel-decorative text-2xl font-bold m-0 truncate">
                  {detail.run.hero}
                  <span className={toneColorClass(runResult?.tone)}>
                    {' '}
                    · {runResult ? t(runResult.key) : '-'}
                  </span>
                </h2>
                <div className="bpp-run-detail-meta flex flex-wrap items-center gap-3 fira-code text-xs selectable">
                  <span>
                    {t('runDetailPlayer')} {detail.run.player_name ?? '-'}
                  </span>
                  <span>•</span>
                  <span>{detail.run.game_mode}</span>
                  <span>•</span>
                  <span>
                    {formatDateTime(detail.run.started_at_utc, locale)} -{' '}
                    {formatDateTime(detail.run.ended_at_utc, locale)}
                  </span>
                  <span>•</span>
                  <span>{t(formatRunStatusKey(detail.run.status))}</span>
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
                problem={screenshotFailure.problem}
                onRetry={() => void page.revealScreenshot()}
              />
            )}

            <div className="bpp-run-detail-stats flex gap-12 pt-5">
              <StatBlock
                label={t('statWinLoss')}
                value={`${detail.run.victories ?? '-'} / ${detail.run.losses ?? '-'}`}
              />
              <StatBlock
                label={t('statFinalDay')}
                value={
                  detail.run.final_day ? String(detail.run.final_day) : '-'
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

          <div className="bpp-panel flex flex-1 flex-col overflow-hidden">
            <div className="flex-1 overflow-auto custom-scrollbar">
              <div className="min-w-[640px]">
                <div
                  className={`${BATTLE_GRID} bpp-battle-table-header px-6 py-3 cinzel text-[10px] tracking-widest uppercase`}
                >
                  <div>{t('battleColDay')}</div>
                  <div>{t('battleColResult')}</div>
                  <div>{t('battleColOpponentHero')}</div>
                  <div>{t('battleColOpponentPlayer')}</div>
                  <div>{t('battleColRank')}</div>
                  <div>{t('battleColRating')}</div>
                  <div className="text-right">{t('battleColVideo')}</div>
                </div>

                {detail.battles.length === 0 ? (
                  <div className="px-6 py-6">
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
                            className={page.refreshing ? 'animate-spin' : ''}
                          />
                          {t('refresh')}
                        </Button>
                      }
                    />
                  </div>
                ) : (
                  detail.battles.map((battle) => (
                    <BattleRow
                      key={battle.battle_id}
                      battle={battle}
                      page={page}
                      onRequestDelete={(battleId, videoId) =>
                        deleteOperation.controller.request({
                          kind: 'delete-video',
                          battleId,
                          videoId
                        })
                      }
                    />
                  ))
                )}
              </div>
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
            <p className="bpp-confirm-target m-0 text-[12px] leading-relaxed fira-code selectable">
              {t('deleteVideoTarget', {
                battleId: pendingDelete.battleId,
                videoId: pendingDelete.videoId
              })}
            </p>
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
    <div className="flex flex-col gap-1">
      <span className="bpp-run-detail-stat-label cinzel text-[10px] tracking-widest uppercase">
        {label}
      </span>
      <span
        className={`bpp-run-detail-stat-value text-xl ${isText ? 'cinzel font-bold' : 'fira-code'}`}
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
  onRequestDelete: (battleId: string, videoId: string) => void;
}) {
  const { locale, t } = useI18n();
  const battleResult = formatBattleResult(battle.result);
  const videoAction = `video:${battle.battle_id}` as const;
  const deleteAction = `delete:${battle.battle_id}` as const;
  const videoAvailability = page.availability(videoAction);
  const deleteAvailability = page.availability(deleteAction);
  const failure = page.problemFor(`battle:${battle.battle_id}`);

  const retryFailure = () => {
    if (!battle.video || !failure) return;
    if (failure.action === videoAction) {
      void page.revealVideo(battle.battle_id, battle.video.video_id);
      return;
    }
    onRequestDelete(battle.battle_id, battle.video.video_id);
  };

  return (
    <div className="bpp-battle-row group transition-colors">
      <div className={`${BATTLE_GRID} px-6 py-4 items-center relative`}>
        <div
          aria-hidden="true"
          className="absolute inset-0 overflow-hidden pointer-events-none opacity-[0.02] flex items-center justify-center"
        >
          <span className="bpp-battle-watermark cinzel-decorative text-8xl font-bold">
            {battle.opponent_hero ?? '-'}
          </span>
        </div>

        <div className="bpp-battle-primary-data fira-code text-sm relative z-10">
          {battle.day === null ? '-' : String(battle.day)}
        </div>
        <div
          className={`cinzel font-bold text-sm tracking-wider relative z-10 ${toneColorClass(battleResult.tone)}`}
        >
          {t(battleResult.key)}
        </div>
        <div className="bpp-battle-hero cinzel text-sm relative z-10 min-w-0 truncate">
          {battle.opponent_hero ?? '-'}
        </div>
        <div className="bpp-battle-secondary-data fira-code text-sm relative z-10 min-w-0 truncate">
          {battle.opponent_name ?? '-'}
        </div>
        <div className="bpp-battle-rank cinzel text-sm relative z-10">
          {battle.opponent_rank ?? '-'}
        </div>
        <div className="bpp-battle-primary-data fira-code text-sm relative z-10">
          {battle.opponent_rating === null ? '-' : battle.opponent_rating}
        </div>

        <div className="flex flex-col items-end gap-1 relative z-10">
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
                  aria-label={t('openVideoLocation')}
                  className="bpp-battle-video-action flex items-center justify-center size-9 disabled:opacity-50 transition-colors"
                >
                  {videoAvailability.running ? (
                    <Loader2 size={14} className="animate-spin" />
                  ) : (
                    <Video size={14} />
                  )}
                </button>
                <button
                  type="button"
                  disabled={deleteAvailability.disabled}
                  onClick={() =>
                    battle.video &&
                    onRequestDelete(battle.battle_id, battle.video.video_id)
                  }
                  title={t('deleteVideo')}
                  aria-label={t('deleteVideo')}
                  className="bpp-battle-delete-action flex items-center justify-center size-9 opacity-0 pointer-events-none group-hover:opacity-100 group-hover:pointer-events-auto focus-visible:opacity-100 focus-visible:pointer-events-auto disabled:opacity-50 transition-opacity"
                >
                  {deleteAvailability.running ? (
                    <Loader2 size={14} className="animate-spin" />
                  ) : (
                    <Trash2 size={14} />
                  )}
                </button>
              </div>
              <span className="bpp-battle-video-meta fira-code text-[10px] whitespace-nowrap selectable">
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
      </div>

      {failure && (
        <div className="px-6 pb-4">
          <RunDetailProblemBanner
            problem={failure.problem}
            onRetry={retryFailure}
          />
        </div>
      )}
    </div>
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
