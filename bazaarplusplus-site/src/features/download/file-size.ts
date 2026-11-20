import type { Locale } from '../../app/router';
import { getIntlLocale } from '../../shared/lib/intl-locale';

const FILE_SIZE_UNITS = [
  'byte',
  'kilobyte',
  'megabyte',
  'gigabyte',
  'terabyte'
] as const;

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
    maximumFractionDigits: 1
  }).format(value);
}
