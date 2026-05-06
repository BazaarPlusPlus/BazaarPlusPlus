import { createProgressLabel, type UpdaterSnapshot } from '../../updater.ts';
import type { TranslateText } from './types.ts';

export interface UpdaterButtonSelection {
  progressLabel: string | null;
  label: string;
  title: string;
  disabled: boolean;
  highlighted: boolean;
}

export function selectUpdaterButton(input: {
  snapshot: UpdaterSnapshot;
  hasPendingUpdate: boolean;
  t: TranslateText;
}): UpdaterButtonSelection {
  const progressLabel = createProgressLabel(input.snapshot.progress);
  const label =
    input.snapshot.status === 'checking'
      ? input.t('updaterChecking')
      : input.snapshot.status === 'available'
        ? input.t('updaterReady', {
            version: input.snapshot.availableVersion ?? '...'
          })
        : input.snapshot.status === 'downloading'
          ? input.t('updaterDownloading', {
              progress: progressLabel ?? '...'
            })
          : input.snapshot.status === 'installed'
            ? input.t('updaterInstallReady', {
                version: input.snapshot.availableVersion ?? '...'
              })
            : input.snapshot.status === 'error'
              ? input.hasPendingUpdate
                ? input.t('updaterRetry')
                : input.t('updaterErrorState')
              : input.snapshot.status === 'unsupported'
                ? input.t('updaterUnsupported')
                : input.t('updaterCurrent');
  const title =
    input.snapshot.status === 'available'
      ? input.t('updaterReadyTitle')
      : input.snapshot.status === 'downloading'
        ? input.t('updaterInstalling')
        : input.snapshot.status === 'installed'
          ? input.t('updaterInstalledTitle')
          : input.snapshot.status === 'error'
            ? input.t('updaterErrorTitle')
            : label;

  return {
    progressLabel,
    label,
    title,
    disabled:
      input.snapshot.status === 'checking' ||
      input.snapshot.status === 'downloading',
    highlighted:
      input.snapshot.status === 'available' ||
      input.snapshot.status === 'installed'
  };
}
