import {
  ArrowLeft,
  FileQuestion,
  Image as ImageIcon,
  Loader2,
  Trash2,
  Video
} from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ErrorBanner } from '../components/ui/ErrorBanner';
import type { HistoryBattleRow } from '../types/backend';
import { useRunDetailPage } from '../features/history/useRunDetailPage';
import {
  formatBattleResult,
  formatDateTime,
  formatRunResultLabel,
  formatRunStatusKey
} from '../features/history/format';
import { DeleteVideoConfirmModal } from '../features/history/DeleteVideoConfirmModal';
import { useI18n } from '../i18n/LocaleProvider';

// Shared 7-track grid for the battle table header + rows so columns align and
// the action column is a fixed 5rem (no reflow when the hover-only delete
// button appears). Day · Result · OppHero · OppPlayer · Rank · Rating · Video.
const BATTLE_GRID =
  'grid grid-cols-[3.5rem_4.5rem_minmax(0,1fr)_minmax(0,1fr)_5rem_5rem_5rem] gap-4';

// One tone language for run verdict AND battle result. Neutral (undefined) is
// muted-gold, NEVER red.
function toneColorClass(tone: 'ok' | 'bad' | undefined): string {
  if (tone === 'ok') return 'text-[#6dd9a0]';
  if (tone === 'bad') return 'text-[#d96d6d]';
  return 'text-[rgba(200,170,120,0.8)]';
}

export default function RunDetail() {
  const navigate = useNavigate();
  const page = useRunDetailPage();
  const detail = page.detail;
  const { t } = useI18n();
  const runResult = detail ? formatRunResultLabel(detail.run.result) : null;
  const [pendingDelete, setPendingDelete] = useState<{
    battleId: string;
    videoId: string;
  } | null>(null);

  const confirmDelete = async () => {
    if (!pendingDelete) return;
    const deleted = await page.deleteVideo(
      pendingDelete.battleId,
      pendingDelete.videoId
    );
    if (deleted) {
      setPendingDelete(null);
    }
  };

  return (
    <div className="flex flex-col gap-6 h-full overflow-hidden pb-8 max-w-5xl mx-auto w-full">
      <button
        type="button"
        onClick={() => navigate('/history')}
        className="flex items-center gap-2 text-[rgba(200,170,120,0.8)] hover:text-[#e8c87a] transition-colors w-fit cinzel text-sm tracking-wider uppercase"
      >
        <ArrowLeft size={16} />
        {t('runDetailBack')}
      </button>

      {page.loading ? (
        <div className="flex items-center justify-center h-64 text-[rgba(200,170,120,0.8)] gap-2">
          <Loader2 size={18} className="animate-spin" />
          <span>{t('runDetailLoading')}</span>
        </div>
      ) : !detail ? (
        <div className="p-6 bg-[rgba(18,11,5,0.88)] border border-[rgba(180,130,48,0.13)] rounded-sm text-[rgba(200,170,120,0.8)]">
          {page.error ?? t('runDetailNotFound')}
        </div>
      ) : (
        <>
          {page.error && <ErrorBanner message={page.error} />}

          <div className="p-6 bg-[rgba(18,11,5,0.88)] border border-[rgba(180,130,48,0.13)] rounded-sm shadow-[0_6px_28px_rgba(0,0,0,0.35)] flex flex-col gap-6">
            <div className="flex justify-between items-start">
              <div className="flex flex-col gap-1 min-w-0">
                <h2 className="cinzel-decorative text-2xl font-bold text-[#e8dcc8] m-0 truncate">
                  {detail.run.hero}
                  <span className={toneColorClass(runResult?.tone)}>
                    {' '}
                    · {runResult ? t(runResult.key) : '-'}
                  </span>
                </h2>
                <div className="flex flex-wrap items-center gap-3 fira-code text-xs text-[rgba(200,170,120,0.8)] selectable">
                  <span>
                    {t('runDetailPlayer')} {detail.run.player_name ?? '-'}
                  </span>
                  <span>•</span>
                  <span>{detail.run.game_mode}</span>
                  <span>•</span>
                  <span>
                    {formatDateTime(detail.run.started_at_utc)} -{' '}
                    {formatDateTime(detail.run.ended_at_utc)}
                  </span>
                  <span>•</span>
                  <span className="text-[rgba(200,170,120,0.8)]">
                    {t(formatRunStatusKey(detail.run.status))}
                  </span>
                </div>
              </div>

              <button
                type="button"
                disabled={
                  !detail.run.screenshot_id || page.action === 'screenshot'
                }
                onClick={page.revealScreenshot}
                className="flex items-center gap-2 px-4 py-2 bg-[rgba(200,148,55,0.06)] border border-[rgba(180,130,48,0.2)] rounded-sm hover:bg-[rgba(200,148,55,0.12)] disabled:opacity-40 transition-colors text-sm text-[#e8dcc8]"
              >
                <ImageIcon size={16} /> {t('openScreenshotLocation')}
              </button>
            </div>

            <div className="flex gap-12 border-t border-[rgba(200,148,55,0.1)] pt-5">
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

          <div className="flex-1 flex flex-col bg-[rgba(18,11,5,0.88)] border border-[rgba(180,130,48,0.13)] rounded-sm shadow-[0_6px_28px_rgba(0,0,0,0.35)] overflow-hidden">
            <div className="flex-1 overflow-auto custom-scrollbar">
              <div className="min-w-[640px]">
                <div
                  className={`${BATTLE_GRID} px-6 py-3 border-b border-[rgba(200,148,55,0.15)] bg-[rgba(200,148,55,0.02)] cinzel text-[10px] tracking-widest text-[rgba(200,170,120,0.8)] uppercase`}
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
                  <div className="px-6 py-8 text-sm text-[rgba(200,170,120,0.8)]">
                    {t('noLocalBattles')}
                  </div>
                ) : (
                  detail.battles.map((battle) => (
                    <BattleRow
                      key={battle.battle_id}
                      battle={battle}
                      page={page}
                      onRequestDelete={(battleId, videoId) =>
                        setPendingDelete({ battleId, videoId })
                      }
                    />
                  ))
                )}
              </div>
            </div>
          </div>
        </>
      )}

      {pendingDelete && (
        <DeleteVideoConfirmModal
          busy={page.action === `delete:${pendingDelete.battleId}`}
          onClose={() => setPendingDelete(null)}
          onConfirm={confirmDelete}
        />
      )}
    </div>
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
      <span className="cinzel text-[10px] tracking-widest text-[rgba(200,170,120,0.8)] uppercase">
        {label}
      </span>
      <span
        className={`text-xl text-[#e8c87a] ${isText ? 'cinzel font-bold' : 'fira-code'}`}
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
  const { t } = useI18n();
  const battleResult = formatBattleResult(battle.result);
  const videoAction = page.action === `video:${battle.battle_id}`;
  const deleteAction = page.action === `delete:${battle.battle_id}`;

  return (
    <div
      className={`${BATTLE_GRID} px-6 py-4 border-b border-[rgba(200,148,55,0.05)] items-center relative group hover:bg-[rgba(200,148,55,0.03)] transition-colors`}
    >
      <div
        aria-hidden="true"
        className="absolute inset-0 overflow-hidden pointer-events-none opacity-[0.02] flex items-center justify-center"
      >
        <span className="cinzel-decorative text-8xl font-bold text-[#e8c87a]">
          {battle.opponent_hero ?? '-'}
        </span>
      </div>

      <div className="fira-code text-sm text-[rgba(228,216,191,0.8)] relative z-10">
        {battle.day === null ? '-' : String(battle.day)}
      </div>
      <div
        className={`cinzel font-bold text-sm tracking-wider relative z-10 ${toneColorClass(battleResult.tone)}`}
      >
        {t(battleResult.key)}
      </div>
      <div className="cinzel text-sm text-[#e8dcc8] relative z-10 min-w-0 truncate">
        {battle.opponent_hero ?? '-'}
      </div>
      <div className="fira-code text-sm text-[rgba(200,170,120,0.8)] relative z-10 min-w-0 truncate">
        {battle.opponent_name ?? '-'}
      </div>
      <div className="cinzel text-sm text-[#e8c87a] relative z-10">
        {battle.opponent_rank ?? '-'}
      </div>
      <div className="fira-code text-sm text-[rgba(228,216,191,0.8)] relative z-10">
        {battle.opponent_rating === null ? '-' : battle.opponent_rating}
      </div>

      <div className="flex items-center justify-end gap-1.5 relative z-10">
        {battle.video ? (
          <>
            <button
              type="button"
              disabled={videoAction}
              onClick={() =>
                page.revealVideo(battle.battle_id, battle.video?.video_id)
              }
              title={t('openVideoLocation')}
              aria-label={t('openVideoLocation')}
              className="flex items-center justify-center size-8 rounded-sm bg-[rgba(200,148,55,0.06)] border border-[rgba(180,130,48,0.2)] hover:bg-[rgba(200,148,55,0.12)] disabled:opacity-40 transition-colors text-[#e8dcc8]"
            >
              {videoAction ? (
                <Loader2 size={14} className="animate-spin" />
              ) : (
                <Video size={14} />
              )}
            </button>
            <button
              type="button"
              disabled={deleteAction}
              onClick={() =>
                battle.video &&
                onRequestDelete(battle.battle_id, battle.video.video_id)
              }
              title={t('deleteVideo')}
              aria-label={t('deleteVideo')}
              className="flex items-center justify-center size-8 rounded-sm text-[rgba(200,170,120,0.72)] hover:text-[#ff4444] hover:bg-[rgba(255,68,68,0.1)] opacity-0 group-hover:opacity-100 focus-visible:opacity-100 disabled:opacity-40 transition-all"
            >
              {deleteAction ? (
                <Loader2 size={14} className="animate-spin" />
              ) : (
                <Trash2 size={14} />
              )}
            </button>
          </>
        ) : (
          <span
            title={t('noVideo')}
            aria-label={t('noVideo')}
            className="flex items-center justify-center size-8 text-[rgba(200,170,120,0.6)]"
          >
            <FileQuestion size={14} />
          </span>
        )}
      </div>
    </div>
  );
}
