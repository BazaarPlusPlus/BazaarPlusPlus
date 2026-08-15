import { CircleAlert, Copy, Folder } from 'lucide-react';
import { useState } from 'react';
import type { ReactNode } from 'react';
import { Button } from '../../components/ui/Button';
import { StatusBanner } from '../../components/ui/StatusBanner';
import { useI18n } from '../../i18n/LocaleProvider';
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
  const heroState = healthy
    ? t('installed')
    : needsReinstall
      ? t('modNeedsReinstall')
      : t('notInstalled');
  const heroDescription = healthy
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
    <div className="flex min-w-0 flex-col gap-3">
      <section className="bpp-install-hero">
        <div className="bpp-install-hero-summary">
          <div className="min-w-0">
            <p className="bpp-install-hero-title">
              <span className="bpp-mod-name">BazaarPlusPlus</span>
              <span className="ml-2">{heroState}</span>
            </p>
            <p
              id="install-hero-description"
              className="bpp-install-hero-description"
            >
              {heroDescription}
            </p>
          </div>
        </div>
        <div className="bpp-install-hero-divider" aria-hidden="true" />
        <div className="bpp-install-primary-slot">
          <PrimaryInstallActionButton
            snapshot={snapshot}
            intents={intents}
            descriptionId="install-hero-description"
            descriptionText={heroDescription}
          />
        </div>
      </section>

      <div className="bpp-install-info-grid">
        <InfoCard
          icon={<Folder size={19} />}
          title={t('installationDirectoryHeading')}
        >
          <div className="bpp-install-directory-field">
            <p
              className={
                selectedPath
                  ? 'selectable bpp-install-path'
                  : 'bpp-install-path is-empty'
              }
              title={selectedPath ?? undefined}
            >
              {selectedPath ?? t('gamePathEmpty')}
            </p>
            <div className="bpp-install-path-actions">
              <Button
                type="button"
                size="small"
                disabled={!selectedPath}
                onClick={() => void copyPath()}
                title={copied ? t('pathCopied') : t('copyPath')}
                aria-label={copied ? t('pathCopied') : t('copyPath')}
              >
                <Copy size={14} />
                {copied ? t('pathCopied') : t('copyPath')}
              </Button>
              <Button
                type="button"
                size="small"
                disabled={!snapshot.actions.chooseDirectory}
                onClick={() => void intents.chooseDirectory()}
                title={t('selectDirectory')}
                aria-label={t('selectDirectory')}
              >
                <Folder size={14} />
                {t('selectDirectory')}
              </Button>
            </div>
          </div>
        </InfoCard>

        <InfoCard title={t('applicationVersionHeading')}>
          <div className="mt-0.5 flex items-center gap-2.5">
            <span className="bpp-install-version-value">{appVersion}</span>
            <span className="bpp-install-build-badge">
              {import.meta.env.DEV ? t('developmentBuild') : t('stableBuild')}
            </span>
          </div>
          <p className="bpp-install-version-meta">BazaarPlusPlus Desktop</p>
        </InfoCard>
      </div>

      {state.warnings.length > 0 && (
        <StatusBanner
          tone="warning"
          message={
            <div className="bpp-install-warning-list">
              {state.warnings.map((warning) => (
                <p key={warning.code} className="m-0 flex items-start gap-2">
                  <CircleAlert size={14} className="mt-0.5 shrink-0" />
                  <span>{presentInstallWarning(warning, t)}</span>
                </p>
              ))}
            </div>
          }
        />
      )}
    </div>
  );
}

function InfoCard({
  icon,
  title,
  children
}: {
  icon?: ReactNode;
  title: string;
  children: ReactNode;
}) {
  return (
    <section className="bpp-install-info-card">
      <h3 className="bpp-install-info-title">
        {icon && <span className="bpp-install-info-icon">{icon}</span>}
        {title}
      </h3>
      {children}
    </section>
  );
}
