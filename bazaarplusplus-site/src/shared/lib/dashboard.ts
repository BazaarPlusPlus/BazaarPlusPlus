import type { Locale } from '../../app/router';
import type { MetricWindow } from '../../features/heroes/hero-analysis';

export const WINDOW_LABELS: Record<MetricWindow, string> = {
  '1d': '1D',
  '3d': '3D',
  '7d': '7D',
};

function getIntlLocale(locale: Locale): string {
  return locale === 'zh' ? 'zh-CN' : 'en-US';
}

export function formatPercent(value: number): string {
  return `${(value * 100).toFixed(1)}%`;
}

// Null/zero-denominator rates render as an em dash; never let NaN reach formatPercent.
export function formatNullablePercent(value: number | null): string {
  return value == null || !Number.isFinite(value) ? '—' : formatPercent(value);
}

export function formatInteger(value: number, locale: Locale): string {
  return new Intl.NumberFormat(getIntlLocale(locale)).format(value);
}

const FILE_SIZE_UNITS = ['byte', 'kilobyte', 'megabyte', 'gigabyte', 'terabyte'] as const;

/** Decimal units, as download sizes are quoted: 2_834_000_000 → `2.8 GB`. */
export function formatFileSize(bytes: number, locale: Locale): string {
  let value = bytes;
  let unit = 0;
  while (value >= 1000 && unit < FILE_SIZE_UNITS.length - 1) {
    value /= 1000;
    unit += 1;
  }
  return new Intl.NumberFormat(getIntlLocale(locale), {
    style: 'unit',
    unit: FILE_SIZE_UNITS[unit],
    maximumFractionDigits: 1,
  }).format(value);
}

export function formatDateTime(value: string, locale: Locale): string {
  return new Intl.DateTimeFormat(getIntlLocale(locale), {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    timeZoneName: 'short',
  }).format(new Date(value));
}

export function formatShortDate(value: string, locale: Locale): string {
  return new Intl.DateTimeFormat(getIntlLocale(locale), {
    month: 'short',
    day: 'numeric',
  }).format(new Date(value));
}
