export type Locale = 'zh' | 'en';
export interface DemoProps {
  lang: Locale;
}
export const translate = (lang: Locale, zh: string, en: string) =>
  lang === 'zh' ? zh : en;
