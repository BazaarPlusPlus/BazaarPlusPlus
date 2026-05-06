import type { MessageKey } from '../../i18n.ts';

export type PendingSteamAction = 'install' | 'uninstall' | null;
export type LocalizedText = (zh: string, en: string) => string;
export type TranslateText = (
  key: MessageKey,
  params?: Record<string, string | number>
) => string;
