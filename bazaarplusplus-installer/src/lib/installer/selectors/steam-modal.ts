import type { PendingSteamAction, TranslateText } from './types.ts';

export interface SteamModalSelection {
  title: string;
  body: string;
  cancelText: string;
}

export function selectSteamModal(input: {
  pendingSteamAction: PendingSteamAction;
  t: TranslateText;
}): SteamModalSelection {
  return {
    title:
      input.pendingSteamAction === 'install'
        ? input.t('installRiskTitle')
        : input.t('steamQuitTitle'),
    body:
      input.pendingSteamAction === 'install'
        ? `${input.t('installRiskSteamDetected')}\n\n${input.t('installRiskBody')}`
        : input.t('steamQuitBody'),
    cancelText:
      input.pendingSteamAction === 'install'
        ? input.t('actionContinueInstall')
        : input.t('actionClose')
  };
}
