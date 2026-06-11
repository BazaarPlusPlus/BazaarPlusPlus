import { ChevronRight, Image as ImageIcon, RefreshCw } from 'lucide-react';
import { Link } from 'react-router-dom';
import { ErrorBanner } from '../components/ui/ErrorBanner';
import { LoadingPanel } from '../components/ui/LoadingPanel';
import { PageShell } from '../components/ui/PageShell';
import {
  formatDateTime,
  formatRunResultLabel
} from '../features/history/format';
import { useHistoryPage } from '../features/history/useHistoryPage';
import { useI18n } from '../i18n/LocaleProvider';
import type { HistoryRunRow } from '../types/backend';

export default function History() {
  const page = useHistoryPage();
  const { t } = useI18n();

  return (
    <PageShell
      eyebrow="History"
      title={t('historyTitle')}
      action={
        <button
          type="button"
          onClick={page.refresh}
          disabled={page.loading}
          className="flex items-center gap-2 px-3 py-1.5 bg-[rgba(200,148,55,0.06)] border border-[rgba(180,130,48,0.2)] rounded-sm hover:bg-[rgba(200,148,55,0.12)] disabled:opacity-40 transition-colors text-xs text-[#e8dcc8]"
        >
          <RefreshCw size={14} className={page.loading ? 'animate-spin' : ''} />
          {t('refresh')}
        </button>
      }
    >
      <div className="flex flex-col gap-6 flex-1 min-h-0 w-full">
        <div className="grid grid-cols-3 gap-4 shrink-0">
          <SummaryCard
            label={t('historySummaryRuns')}
            value={page.summary.runs}
          />
          <SummaryCard
            label={t('historySummaryVideos')}
            value={page.summary.videos}
          />
          <SummaryCard
            label={t('historySummaryWinRate')}
            value={page.summary.winRate}
          />
        </div>

        {page.error && <ErrorBanner message={page.error} />}

        <div className="flex flex-col gap-3 flex-1 min-h-0 overflow-y-auto custom-scrollbar pr-2">
          {page.loading ? (
            <LoadingPanel label={t('historyLoading')} />
          ) : page.payload.runs.length === 0 ? (
            <div className="flex items-center justify-center h-48 text-[rgba(200,170,120,0.8)] border border-[rgba(180,130,48,0.12)] bg-[rgba(18,11,5,0.6)]">
              {t('noLocalRuns')}
            </div>
          ) : (
            page.payload.runs.map((run: HistoryRunRow) => (
              <RunRow
                key={run.run_id}
                run={run}
                previewUrl={page.previewUrl(run)}
              />
            ))
          )}
        </div>
      </div>
    </PageShell>
  );
}

function SummaryCard({
  label,
  value,
  isFira = false
}: {
  label: string;
  value: string;
  isFira?: boolean;
}) {
  return (
    <div className="p-4 bg-[rgba(18,11,5,0.88)] border border-[rgba(180,130,48,0.13)] rounded-sm shadow-[0_6px_28px_rgba(0,0,0,0.35)] flex flex-col items-center justify-center gap-1">
      <span className="cinzel text-[10px] tracking-widest text-[rgba(200,170,120,0.8)] uppercase">
        {label}
      </span>
      <span
        className={`text-2xl text-[#e8c87a] ${isFira ? 'fira-code' : 'cinzel font-bold'}`}
      >
        {value}
      </span>
    </div>
  );
}

function RunRow({
  run,
  previewUrl
}: {
  run: HistoryRunRow;
  previewUrl: string | null;
}) {
  const { t } = useI18n();
  const result = formatRunResultLabel(run.result);
  const detailPath = `/history/${encodeURIComponent(run.run_id)}`;

  return (
    <Link
      to={detailPath}
      className="group grid grid-cols-[14rem_minmax(0,1fr)_9rem_6.5rem_5rem_5.5rem_auto] items-center gap-6 p-3 bg-[rgba(18,11,5,0.88)] border border-[rgba(180,130,48,0.13)] rounded-sm hover:border-[rgba(200,148,55,0.4)] hover:bg-[rgba(200,148,55,0.04)] transition-all shadow-[0_4px_12px_rgba(0,0,0,0.2)] no-underline text-inherit"
    >
      <div className="w-56 aspect-[2000/470] shrink-0 bg-[#000] border border-[rgba(200,148,55,0.2)] rounded-sm flex items-center justify-center text-[rgba(200,170,120,0.3)] group-hover:border-[rgba(200,148,55,0.5)] transition-colors overflow-hidden relative">
        {previewUrl ? (
          <img
            src={previewUrl}
            alt=""
            loading="lazy"
            decoding="async"
            className="absolute inset-0 w-full h-full object-cover"
          />
        ) : (
          <>
            <div className="absolute inset-0 opacity-20 bg-gradient-to-r from-transparent via-[rgba(200,148,55,0.2)] to-transparent" />
            <ImageIcon size={20} />
          </>
        )}
      </div>

      <div className="flex flex-col gap-1 min-w-0">
        <span className="cinzel font-bold text-lg text-[#e8dcc8] truncate">
          {run.hero}
        </span>
        <span className="fira-code text-[10px] text-[rgba(200,170,120,0.8)] truncate">
          {formatDateTime(run.started_at_utc)}
        </span>
      </div>

      <span
        className={`cinzel font-bold text-lg whitespace-nowrap ${toneColorClass(
          result.tone
        )}`}
      >
        {t(result.key)}
      </span>

      <Metric
        label={t('runMetricProgress')}
        value={`${run.victories ?? 0} / ${run.final_day ?? '-'}`}
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

      <div className="flex items-center gap-1 text-[rgba(200,170,120,0.55)] group-hover:text-[#e8c87a] transition-colors whitespace-nowrap">
        <span className="cinzel text-[10px] tracking-widest uppercase">
          {t('viewDetail')}
        </span>
        <ChevronRight size={14} />
      </div>
    </Link>
  );
}

function toneColorClass(tone: 'ok' | 'bad' | undefined): string {
  if (tone === 'ok') return 'text-[#6dd9a0]';
  if (tone === 'bad') return 'text-[#d96d6d]';
  return 'text-[rgba(200,170,120,0.8)]';
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
    <div className="flex flex-col gap-1 items-end text-right">
      <span className="cinzel text-[10px] tracking-widest text-[rgba(200,170,120,0.8)] uppercase">
        {label}
      </span>
      <span
        className={`text-sm ${fira ? 'fira-code' : 'cinzel'} ${
          gold ? 'text-[#e8c87a]' : 'text-[#e8dcc8]'
        }`}
      >
        {value}
      </span>
    </div>
  );
}
