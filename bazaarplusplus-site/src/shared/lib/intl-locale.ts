import type { Locale } from '../../app/router';

/** The `Intl` locale each site locale formats numbers and dates with. */
export function getIntlLocale(locale: Locale): string {
  return locale === 'zh' ? 'zh-CN' : 'en-US';
}
