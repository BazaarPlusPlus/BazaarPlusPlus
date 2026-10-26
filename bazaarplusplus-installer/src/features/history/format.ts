import type { MessageKey } from '../../i18n/messages';
import type { Locale } from '../../i18n/messages';
import type { HistoryRunRow } from '../../types/backend';
import type { Translate } from '../../i18n/LocaleProvider';

export function formatGameMode(mode: string, t: Translate): string {
  if (mode === 'Ranked') return t('runModeRanked');
  if (mode === 'Unranked') return t('runModeUnranked');
  if (mode === 'Normal') return t('runModeNormal');
  return mode;
}

// Keyed by the hero id with spaces removed and lowercased, so the canonical id
// (`TheDragons`) and its display name (`The Dragons`) both resolve.
const heroKeys: Record<string, MessageKey> = {
  vanessa: 'heroVanessa',
  pygmalien: 'heroPygmalien',
  dooley: 'heroDooley',
  mak: 'heroMak',
  jules: 'heroJules',
  stelle: 'heroStelle',
  karnok: 'heroKarnok',
  thedragons: 'heroTheDragons'
};

// Unknown heroes (a newly released one) fall back to the stored English name.
export function formatHero(hero: string, t: Translate): string {
  const key = heroKeys[hero.replace(/\s+/g, '').toLowerCase()];
  return key ? t(key) : hero;
}

const rankKeys: Record<string, MessageKey> = {
  bronze: 'rankBronze',
  silver: 'rankSilver',
  gold: 'rankGold',
  diamond: 'rankDiamond',
  master: 'rankMaster',
  masters: 'rankMaster',
  legendary: 'rankLegendary'
};

// Ranks arrive as the game's tier name with an optional division
// (`Gold 1`, `Diamond III`, `Legendary`); only the tier word is translated.
export function formatRank(rank: string, t: Translate): string {
  const match = /^\s*([A-Za-z]+)(.*)$/.exec(rank);
  const key = match ? rankKeys[match[1].toLowerCase()] : undefined;
  return key && match ? `${t(key)}${match[2].trimEnd()}` : rank;
}

// A Run's rating change as a signed number; null when the change is unknown, so the
// caller shows nothing instead of a misleading +0.
export function formatRatingDelta(delta: number | null): string | null {
  if (delta === null) return null;
  if (delta > 0) return `+${delta}`;
  if (delta < 0) return `${delta}`;
  return '±0';
}

export function ratingDeltaTrend(delta: number): 'up' | 'down' | 'flat' {
  return delta > 0 ? 'up' : delta < 0 ? 'down' : 'flat';
}

const dateTimeFormatters: Record<Locale, Intl.DateTimeFormat> = {
  zh: new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  }),
  en: new Intl.DateTimeFormat('en-US', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  })
};

const numberFormatters: Record<Locale, Intl.NumberFormat> = {
  zh: new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 1 }),
  en: new Intl.NumberFormat('en-US', { maximumFractionDigits: 1 })
};

const durationFormatters: Record<
  Locale,
  { minutes: Intl.NumberFormat; seconds: Intl.NumberFormat }
> = {
  zh: {
    minutes: new Intl.NumberFormat('zh-CN', {
      style: 'unit',
      unit: 'minute',
      unitDisplay: 'short'
    }),
    seconds: new Intl.NumberFormat('zh-CN', {
      style: 'unit',
      unit: 'second',
      unitDisplay: 'short'
    })
  },
  en: {
    minutes: new Intl.NumberFormat('en-US', {
      style: 'unit',
      unit: 'minute',
      unitDisplay: 'short'
    }),
    seconds: new Intl.NumberFormat('en-US', {
      style: 'unit',
      unit: 'second',
      unitDisplay: 'short'
    })
  }
};

export function formatDateTime(
  value: string | null | undefined,
  locale: Locale
) {
  if (!value) return '-';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return dateTimeFormatters[locale].format(date);
}

type RunOutcomeTier = 'misfortune' | 'bronze' | 'silver' | 'gold' | 'diamond';

export function formatRunResultLabel(
  run: Pick<HistoryRunRow, 'result' | 'victories' | 'losses'>
): {
  key: MessageKey;
  tier: RunOutcomeTier | undefined;
  state?: 'active' | 'abandoned';
} {
  if (run.result === 'abandoned') {
    return { key: 'runResultAbandoned', tier: undefined, state: 'abandoned' };
  }
  if (run.result !== 'win' && run.result !== 'loss') {
    return { key: 'runResultActive', tier: undefined, state: 'active' };
  }

  // Match the mod's HistoryPanelFormatter.GetRunOutcomeTier, including null counts.
  const wins = run.victories ?? 0;
  const totalBattles = wins + (run.losses ?? 0);
  if (wins === 10 && totalBattles === 10) {
    return { key: 'runResultDiamond', tier: 'diamond' };
  }
  if (wins >= 10 && totalBattles > 10) {
    return { key: 'runResultGold', tier: 'gold' };
  }
  if (wins >= 7) {
    return { key: 'runResultSilver', tier: 'silver' };
  }
  if (wins >= 4) {
    return { key: 'runResultBronze', tier: 'bronze' };
  }
  return { key: 'runResultMisfortune', tier: 'misfortune' };
}

export function formatBattleResult(result: string): {
  key: MessageKey;
  tone: 'ok' | 'bad' | undefined;
} {
  switch (result) {
    case 'win':
      return { key: 'battleResultWin', tone: 'ok' };
    case 'loss':
      return { key: 'battleResultLoss', tone: 'bad' };
    default:
      return { key: 'battleResultNeutral', tone: undefined };
  }
}

// Unresolved battle results use a neutral tone.
export function toneColorClass(tone: 'ok' | 'bad' | undefined): string {
  if (tone === 'ok') return 'text-success';
  if (tone === 'bad') return 'text-danger';
  return 'text-fg-3';
}

export function formatRunStatusKey(status: string): MessageKey {
  switch (status) {
    case 'completed':
      return 'runStatusCompleted';
    case 'abandoned':
      return 'runStatusAbandoned';
    default:
      return 'runStatusActive';
  }
}

export function formatDuration(
  durationMs: number | null | undefined,
  locale: Locale
): string {
  if (durationMs === null || durationMs === undefined || durationMs < 0) {
    return '-';
  }
  const totalSeconds = Math.round(durationMs / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (minutes === 0) {
    return durationFormatters[locale].seconds.format(seconds);
  }
  if (seconds === 0) {
    return durationFormatters[locale].minutes.format(minutes);
  }
  return `${durationFormatters[locale].minutes.format(minutes)} ${durationFormatters[locale].seconds.format(seconds)}`;
}

export function formatBytes(
  bytes: number | null | undefined,
  locale: Locale
): string {
  if (bytes === null || bytes === undefined) {
    return '-';
  }
  if (bytes <= 0) {
    return '0 MB';
  }
  const mb = bytes / (1024 * 1024);
  if (mb >= 1024) {
    return `${numberFormatters[locale].format(mb / 1024)} GB`;
  }
  if (mb >= 1) {
    return `${numberFormatters[locale].format(mb)} MB`;
  }
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}
