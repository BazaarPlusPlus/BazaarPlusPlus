import type { MessageKey } from '../../i18n/messages';
import type { Locale } from '../../i18n/messages';

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

export function formatRunResultLabel(result: string): {
  key: MessageKey;
  tone: 'ok' | 'bad' | undefined;
} {
  switch (result) {
    case 'win':
      return { key: 'runResultVictory', tone: 'ok' };
    case 'loss':
      return { key: 'runResultDefeat', tone: 'bad' };
    case 'abandoned':
      return { key: 'runResultAbandoned', tone: undefined };
    default:
      return { key: 'runResultActive', tone: undefined };
  }
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

// One tone language for run verdict AND battle result. Neutral (undefined) is
// muted-gold, NEVER red.
export function toneColorClass(tone: 'ok' | 'bad' | undefined): string {
  if (tone === 'ok') return 'text-[#6dd9a0]';
  if (tone === 'bad') return 'text-[#d96d6d]';
  return 'text-[rgba(200,170,120,0.8)]';
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
