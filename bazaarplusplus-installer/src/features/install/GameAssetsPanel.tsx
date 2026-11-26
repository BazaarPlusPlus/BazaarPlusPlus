import { RefreshCw } from 'lucide-react';
import { Badge, Button, type BadgeTone } from '@bpp/ui';
import { useI18n } from '../../i18n/LocaleProvider';
import type { MessageKey } from '../../i18n/messages';
import { formatBytes, formatDateTime } from '../history/format';
import { useCardCatalogStatus } from './useCardCatalogStatus';
import type { CardCatalogStatus } from '../../types/backend';

function state(status: CardCatalogStatus): [BadgeTone, MessageKey] {
  if (status.progress) return ['busy', 'gameAssetsExporting'];
  if (status.failed) return ['bad', 'gameAssetsFailed'];
  if (status.current) return ['ok', 'gameAssetsCurrent'];
  if (!status.gameFound) return ['neutral', 'gameAssetsNoGame'];
  return status.revision
    ? ['warn', 'gameAssetsOutdated']
    : ['neutral', 'gameAssetsMissing'];
}

/** Card names, Card Text, art and Enchantment FX exported from the installed game for History. */
export function GameAssetsPanel() {
  const { t, locale } = useI18n();
  const { status, rebuild } = useCardCatalogStatus();
  if (!status) return null;
  const [tone, label] = state(status);
  const progress = status.progress;
  const percent = progress
    ? Math.floor((progress.done * 100) / Math.max(progress.total, 1))
    : 0;
  return (
    <section className="bpp-panel" aria-labelledby="game-assets-heading">
      <div className="bpp-row">
        <span className="bpp-row-copy">
          <span id="game-assets-heading" className="bpp-row-title">
            {t('gameAssetsHeading')}
            <Badge tone={tone} className="ml-2">
              {progress ? `${t(label)} ${percent}%` : t(label)}
            </Badge>
          </span>
          <span className="bpp-row-description">{t('gameAssetsHint')}</span>
          {progress && (
            <span
              role="progressbar"
              aria-labelledby="game-assets-heading"
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={percent}
              className="mt-2 block h-1.5 overflow-hidden rounded-full bg-hover"
            >
              <span
                className="block h-full rounded-full bg-accent transition-[width]"
                style={{ width: `${percent}%` }}
              />
            </span>
          )}
        </span>
        <Button
          size="sm"
          icon={<RefreshCw />}
          disabled={!status.gameFound || !!progress}
          busy={!!progress}
          onClick={rebuild}
        >
          {status.revision ? t('gameAssetsRebuild') : t('gameAssetsBuild')}
        </Button>
      </div>
      {status.exportedAtMs !== null && (
        <div className="bpp-kv">
          <span className="bpp-kv-key">{t('gameAssetsExportedAt')}</span>
          <span className="bpp-kv-value tnum">
            {formatDateTime(
              new Date(status.exportedAtMs).toISOString(),
              locale
            )}
          </span>
          <span />
          <span className="bpp-kv-key">{t('gameAssetsSize')}</span>
          <span className="bpp-kv-value tnum">
            {t('gameAssetsSizeValue', {
              files: status.files,
              size: formatBytes(status.bytes, locale)
            })}
          </span>
          <span />
        </div>
      )}
    </section>
  );
}
