import { ChevronDown, ExternalLink } from 'lucide-react';
import { buttonClassName } from '../components/ui/Button';
import { PageShell } from '../components/ui/PageShell';
import { ProblemBanner } from '../components/ui/ProblemBanner';
import { useAppBootstrap } from '../features/about/AppBootstrapProvider';
import type { AppBootstrapSnapshot } from '../features/about/appBootstrap';
import { presentAboutProblem } from '../features/about/aboutProblems';
import { useI18n } from '../i18n/LocaleProvider';
import type { MessageKey } from '../i18n/messages';
import type { AppBootstrap, AppCredit } from '../types/backend';
import fableVerifiedBadge from '../../static/about/fable-5-verified.webp';
import brandLogo from '../../static/brand/bazaarplusplus-logo.webp';

// Credits are split into ordered groups by their `group` field so contributors
// stay separate from the external data/inspiration sources we acknowledge.
const CREDIT_GROUP_LABELS: Record<string, MessageKey> = {
  team: 'aboutContributors',
  acknowledgement: 'aboutAcknowledgements'
};

function groupCredits(
  credits: readonly AppCredit[]
): { key: string; items: AppCredit[] }[] {
  const groups: { key: string; items: AppCredit[] }[] = [];
  for (const credit of credits) {
    const existing = groups.find((group) => group.key === credit.group);
    if (existing) {
      existing.items.push(credit);
    } else {
      groups.push({ key: credit.group, items: [credit] });
    }
  }
  return groups;
}

export default function About() {
  const { resource, retry } = useAppBootstrap();

  return <AboutView resource={resource} onRetry={retry} />;
}

export function AboutView({
  resource,
  onRetry
}: {
  resource: AppBootstrapSnapshot;
  onRetry: () => void;
}) {
  const { t } = useI18n();
  const bootstrap = resource.data;

  return (
    <PageShell title={t('aboutTitle')}>
      <AboutBootstrapFeedback resource={resource} onRetry={onRetry} />
      {bootstrap ? <AboutBootstrapContent bootstrap={bootstrap} /> : null}
    </PageShell>
  );
}

function AboutBootstrapContent({ bootstrap }: { bootstrap: AppBootstrap }) {
  const { t } = useI18n();
  // Both values are versions, so both read with the same `v` prefix. The
  // bundled version comes from a file inside the payload zip, so it may or may
  // not already carry one.
  const bppVersionLabel = bootstrap.bundled_bpp_version
    ? bootstrap.bundled_bpp_version.replace(/^v?/i, 'v')
    : t('aboutUnavailableValue');

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
            <h3 className="bpp-install-status-title">BazaarPlusPlus</h3>
            <p className="bpp-install-status-description">
              {t('aboutTagline')}
            </p>
          </div>
          <a
            href={bootstrap.links.github}
            target="_blank"
            rel="noreferrer"
            className={buttonClassName()}
          >
            <GithubMark />
            GitHub
            <ExternalLink aria-hidden="true" />
          </a>
        </div>
        <div className="bpp-kv selectable">
          <span className="bpp-kv-key">{t('aboutAppLabel')}</span>
          <span
            className="bpp-kv-value tnum"
            aria-label={`${t('aboutAppLabel')} ${bootstrap.app_version}`}
          >
            v{bootstrap.app_version}
          </span>
          <span />
          <span className="bpp-kv-key">{t('aboutBppLabel')}</span>
          <span
            className="bpp-kv-value tnum"
            aria-label={`${t('aboutBppLabel')} ${bppVersionLabel}`}
          >
            {bppVersionLabel}
          </span>
          <span />
        </div>
      </section>

      <section className="flex flex-col gap-2">
        <h3 className="bpp-section-title m-0">{t('aboutCredits')}</h3>
        <div className="bpp-panel">
          {groupCredits(bootstrap.credits).map((group) => (
            <div key={group.key}>
              <h4 className="bpp-credit-group">
                {t(CREDIT_GROUP_LABELS[group.key] ?? 'aboutContributors')}
              </h4>
              <ul className="bpp-credit-list">
                {group.items.map((credit) => (
                  <ListItem
                    key={`${credit.name}:${credit.role}`}
                    name={credit.name}
                    role={credit.role}
                    href={credit.href}
                  />
                ))}
              </ul>
            </div>
          ))}
        </div>
      </section>

      <details className="bpp-panel">
        <summary className="bpp-panel-summary">
          {t('aboutLicenses')}
          <ChevronDown size={15} aria-hidden="true" />
        </summary>
        <ul className="bpp-credit-list">
          {bootstrap.licenses.map((license) => (
            <ListItem
              key={`${license.name}:${license.category}`}
              name={license.name}
              role={license.license}
            />
          ))}
        </ul>
      </details>

      <footer className="mt-2 flex flex-col items-center opacity-50">
        <img
          src={fableVerifiedBadge}
          alt={t('aboutVerifiedBadge')}
          draggable={false}
          className="h-auto w-full max-w-[260px] select-none"
        />
      </footer>
    </>
  );
}

function AboutBootstrapFeedback({
  resource,
  onRetry
}: {
  resource: AppBootstrapSnapshot;
  onRetry: () => void;
}) {
  const { t } = useI18n();
  if (resource.phase === 'authoritative') return null;

  if (resource.phase === 'initial-loading') {
    return (
      <ProblemBanner
        tone="warning"
        message={t(
          resource.data ? 'aboutLoadingBootstrap' : 'aboutLoadingBootstrapOnly'
        )}
      />
    );
  }

  const retry = resource.problem ? onRetry : undefined;

  if (resource.phase === 'blocking-failure') {
    return (
      <ProblemBanner
        message={t('aboutBlockingFailure')}
        problem={resource.problem}
        onRetry={retry}
        retryBusy={resource.retrying}
        retryBusyLabel={t('aboutRetrying')}
      />
    );
  }

  return (
    <ProblemBanner
      tone={resource.problem ? 'error' : 'warning'}
      message={
        resource.problem
          ? presentAboutProblem(resource.problem, t)
          : t('aboutFallbackPreview')
      }
      problem={resource.problem}
      onRetry={retry}
      retryBusy={resource.retrying}
      retryBusyLabel={t('aboutRetrying')}
    />
  );
}

function ListItem({
  name,
  role,
  href
}: {
  name: string;
  role?: string;
  href?: string | null;
}) {
  return (
    <li className="bpp-credit-item">
      {href ? (
        <a
          href={href}
          target="_blank"
          rel="noopener noreferrer"
          aria-label={`${name} GitHub`}
          className="bpp-credit-name"
        >
          {name}
          <ExternalLink size={11} aria-hidden="true" />
        </a>
      ) : (
        <span className="bpp-credit-name">{name}</span>
      )}
      {role && <span className="bpp-credit-role">{role}</span>}
    </li>
  );
}

function GithubMark() {
  return (
    <svg
      viewBox="0 0 24 24"
      width="15"
      height="15"
      aria-hidden="true"
      focusable="false"
    >
      <path
        fill="currentColor"
        d="M12 1.5a10.5 10.5 0 0 0-3.32 20.46c.53.1.72-.23.72-.5v-1.96c-2.94.64-3.56-1.42-3.56-1.42-.48-1.22-1.18-1.54-1.18-1.54-.96-.66.08-.65.08-.65 1.06.08 1.62 1.09 1.62 1.09.94 1.62 2.47 1.15 3.07.88.1-.69.37-1.15.67-1.42-2.35-.27-4.82-1.17-4.82-5.22 0-1.15.41-2.08 1.08-2.82-.11-.27-.47-1.34.1-2.79 0 0 .88-.28 2.89 1.08A10 10 0 0 1 12 7.5c.9 0 1.8.12 2.65.36 2.01-1.36 2.89-1.08 2.89-1.08.57 1.45.21 2.52.1 2.79.67.74 1.08 1.67 1.08 2.82 0 4.06-2.48 4.94-4.84 5.21.38.33.72.98.72 1.98v2.38c0 .27.19.6.73.5A10.5 10.5 0 0 0 12 1.5Z"
      />
    </svg>
  );
}
