export const V3_API_BASE_URL = 'https://mod-api-v3.bazaarplusplus.com';
export const STEAM_BAZAAR_URL = 'steam://rungameid/1617400';
export const BILIBILI_URL = 'https://space.bilibili.com/3546978457750467';

export const TUTORIAL_URLS = {
  zh: 'https://bazaarplusplus.com/tutorial',
  en: 'https://bazaarplusplus.com/tutorial?lang=en'
} as const;

export const DOTNET_DOWNLOAD_URLS = {
  zh: 'https://dotnet.microsoft.com/zh-cn/download',
  en: 'https://dotnet.microsoft.com/en-us/download'
} as const;

export function getTutorialUrl(locale: string): string {
  return locale === 'en' ? TUTORIAL_URLS.en : TUTORIAL_URLS.zh;
}
