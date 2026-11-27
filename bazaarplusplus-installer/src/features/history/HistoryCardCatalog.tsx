import { useMemo, type ReactNode } from 'react';
import { convertFileSrc } from '@tauri-apps/api/core';
import { CardCatalog, type CardSource } from '@bpp/ui';
import { hasTauriRuntime } from '../../api/runtime';
import { ModalSource } from '../../components/ui/ModalCoordinator';
import { useI18n } from '../../i18n/LocaleProvider';
import { useCardCatalogStatus } from '../install/useCardCatalogStatus';

/**
 * The card catalog exported from the installed game, served as `bppcard://`. Browser Preview
 * reads the published set through the dev server's `/card-art/` proxy instead.
 */
function useCardSource() {
  const { status } = useCardCatalogStatus();
  const revision = status?.revision ?? null;
  const source = useMemo<CardSource | null>(
    () =>
      revision === null
        ? null
        : {
            base: hasTauriRuntime()
              ? convertFileSrc('', 'bppcard')
              : '/card-art/',
            revision
          },
    [revision]
  );
  return { source, progress: status?.progress ?? null };
}

function CardDialogHost({
  open,
  children
}: {
  open: boolean;
  children: ReactNode;
}) {
  return (
    <ModalSource
      id="route:card-detail"
      open={open}
      priority="informational"
      dismissalPolicy="dismissible"
    >
      {children}
    </ModalSource>
  );
}

export function HistoryCardCatalog({
  catalogKey,
  children
}: {
  catalogKey?: string;
  children: ReactNode;
}) {
  const { locale, t } = useI18n();
  const { source, progress } = useCardSource();
  return (
    <CardCatalog
      key={catalogKey}
      source={source}
      locale={locale}
      dialogHost={CardDialogHost}
    >
      {progress && (
        <p role="status" className="m-0 text-small text-text-3">
          {t('cardCatalogExporting', {
            percent: Math.floor(
              (progress.done * 100) / Math.max(progress.total, 1)
            )
          })}
        </p>
      )}
      {children}
    </CardCatalog>
  );
}
