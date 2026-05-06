import type { LocalizedText, TranslateText } from './types.ts';
import { DOTNET_DOWNLOAD_URLS } from '../../config/endpoints.ts';

export interface ModeLabelsSelection {
  modeTitle: string;
  modeToggleLabel: string;
  dotnetDownloadUrl: string;
  localeBadge: string;
  localeButtonLabel: string;
}

export function selectModeLabels(input: {
  showStreamMode: boolean;
  locale: string;
  localized: LocalizedText;
  t: TranslateText;
}): ModeLabelsSelection {
  return {
    modeTitle: input.showStreamMode ? input.t('streamTitle') : input.t('subtitle'),
    modeToggleLabel: input.showStreamMode
      ? input.localized('安装模式', 'Install Mode')
      : input.localized('直播模式', 'Stream Mode'),
    dotnetDownloadUrl:
      input.locale === 'zh' ? DOTNET_DOWNLOAD_URLS.zh : DOTNET_DOWNLOAD_URLS.en,
    localeBadge: input.locale === 'zh' ? '中' : 'EN',
    localeButtonLabel:
      input.locale === 'zh' ? 'Switch to English' : '切换到中文'
  };
}
