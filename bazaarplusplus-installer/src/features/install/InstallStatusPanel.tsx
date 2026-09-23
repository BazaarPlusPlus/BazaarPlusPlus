import { Copy, Check, Folder } from 'lucide-react';
import { useState } from 'react';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { StatusBanner } from '../../components/ui/StatusBanner';
import { useI18n } from '../../i18n/LocaleProvider';
import brandLogo from '../../../static/brand/bazaarplusplus-logo.webp';
import { PrimaryInstallActionButton } from './PrimaryInstallActionButton';
import { presentInstallWarning } from './installProblems';
import type {
  InstallPageSnapshot,
  InstallWorkflowIntents
} from './installWorkflow';

export function InstallStatusPanel({
  snapshot,
  intents,
  appVersion
}: {
  snapshot: Extract<InstallPageSnapshot, { phase: 'ready' }>;
  intents: InstallWorkflowIntents;
  appVersion: string;
}) {
  const { t } = useI18n();
  const [copied, setCopied] = useState(false);
  const state = snapshot.data;
  const installed = state.mod_state.installed;
  const healthy = state.mod_state.ready;
  const needsReinstall = installed && !state.mod_state.ready;
  const status = healthy
    ? { tone: 'ok' as const, label: t('installed') }
    : needsReinstall
      ? { tone: 'warn' as const, label: t('modNeedsReinstall') }
      : { tone: 'neutral' as const, label: t('notInstalled') };
  const description = healthy
    ? t('installOverviewHealthyShort')
    : needsReinstall
      ? t('installOverviewUpdateDescription')
      : t('installOverviewNotInstalledDescription');
  const selectedPath = state.selected_game_path;

  const copyPath = async () => {
    if (!selectedPath) return;
    await navigator.clipboard.writeText(selectedPath);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1600);
  };

  return (
    <>
      <section className="bpp-panel">
        <div className="bpp-install-status-top">
          <img
            src={brandLogo}
            alt=""
            className="bpp-install-status-logo"
            draggable={false}
          />
          <div className="bpp-install-status-name">
            <p className="bpp-install-status-title">
              BazaarPlusPlus
              <StatusBadge tone={status.tone}>{status.label}</StatusBadge>
            </p>
            <p
              id="install-hero-description"
              className="bpp-install-status-description"
            >
              {description}
            </p>
          </div>
          <PrimaryInstallActionButton
            snapshot={snapshot}
            intents={intents}
            descriptionId="install-hero-description"
            descriptionText={description}
          />
        </div>

        <div className="bpp-kv">
          <span className="bpp-kv-key">
            {t('installationDirectoryHeading')}
          </span>
          <span
            className={
              selectedPath
                ? 'bpp-kv-value mono selectable'
                : 'bpp-kv-value is-empty'
            }
            title={selectedPath ?? undefined}
          >
            {selectedPath ?? t('gamePathEmpty')}
          </span>
          <span className="bpp-kv-actions">
            <Button
              variant="ghost"
              size="icon-sm"
              disabled={!selectedPath}
              onClick={() => void copyPath()}
              title={copied ? t('pathCopied') : t('copyPath')}
              aria-label={copied ? t('pathCopied') : t('copyPath')}
              icon={copied ? <Check /> : <Copy />}
            />
            <Button
              variant="ghost"
              size="sm"
              disabled={!snapshot.actions.chooseDirectory}
              onClick={() => void intents.chooseDirectory()}
              icon={<Folder />}
            >
              {t('selectDirectory')}
            </Button>
          </span>

          <span className="bpp-kv-key">{t('applicationVersionHeading')}</span>
          <span className="bpp-kv-value tnum">
            {appVersion}
            <StatusBadge tone="neutral" dot={false} className="ml-2">
              {import.meta.env.DEV ? t('developmentBuild') : t('stableBuild')}
            </StatusBadge>
          </span>
          <span />
        </div>
      </section>

      {state.warnings.length > 0 && (
        <StatusBanner
          tone="warning"
          message={
            <ul className="m-0 flex list-none flex-col gap-1 p-0">
              {state.warnings.map((warning) => (
                <li key={warning.code}>{presentInstallWarning(warning, t)}</li>
              ))}
            </ul>
          }
        />
      )}
    </>
  );
}
