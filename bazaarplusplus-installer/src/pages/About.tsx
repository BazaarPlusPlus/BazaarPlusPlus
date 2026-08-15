import { ExternalLink } from 'lucide-react';
import { Button } from '../components/ui/Button';
import { PageHeader } from '../components/ui/PageHeader';
import { ProblemBanner } from '../components/ui/ProblemBanner';
import { useAppBootstrap } from '../features/about/AppBootstrapProvider';
import type { AppBootstrapSnapshot } from '../features/about/appBootstrap';
import { presentAboutProblem } from '../features/about/aboutProblems';
import { formatProblemDiagnostic } from '../features/shared/problems';
import { useI18n } from '../i18n/LocaleProvider';
import type { MessageKey } from '../i18n/messages';
import type { AppBootstrap, AppCredit } from '../types/backend';
import fableVerifiedBadge from '../../static/about/fable-5-verified.webp';

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
    <div className="bpp-page pb-8">
      <PageHeader title={t('aboutTitle')} />

      <div className="flex min-h-0 w-full flex-1 flex-col gap-4">
        <AboutBootstrapFeedback resource={resource} onRetry={onRetry} />

        {bootstrap ? <AboutBootstrapContent bootstrap={bootstrap} /> : null}
      </div>
    </div>
  );
}

function AboutBootstrapContent({ bootstrap }: { bootstrap: AppBootstrap }) {
  const { t } = useI18n();
  // Both chips are version chips, so both read with the same `v` prefix. The
  // bundled version comes from a file inside the payload zip, so it may or may
  // not already carry one.
  const bppVersionLabel = bootstrap.bundled_bpp_version
    ? bootstrap.bundled_bpp_version.replace(/^v?/i, 'v')
    : t('aboutUnavailableValue');

  return (
    <>
      <section className="bpp-panel bpp-card-pad relative overflow-hidden">
        <div className="bpp-about-brand-watermark">B++</div>
        <div className="flex items-center gap-5">
          <div className="min-w-0 flex-1">
            <h3 className="bpp-mod-name bpp-about-product-name">
              BazaarPlusPlus
            </h3>
            <p className="bpp-about-tagline">{t('aboutTagline')}</p>
            <div className="bpp-about-version-row mt-5 selectable">
              <span className="bpp-about-version-label">
                {t('aboutAppLabel')}
              </span>
              <span
                aria-label={`${t('aboutAppLabel')} ${bootstrap.app_version}`}
                className="bpp-version-chip w-fit"
              >
                v{bootstrap.app_version}
              </span>
              <span
                className="bpp-about-version-separator"
                aria-hidden="true"
              />
              <span className="bpp-about-version-label">
                {t('aboutBppLabel')}
              </span>
              <span
                aria-label={`${t('aboutBppLabel')} ${bppVersionLabel}`}
                className="bpp-version-chip w-fit"
              >
                {bppVersionLabel}
              </span>
            </div>
          </div>
          <a
            href={bootstrap.links.github}
            target="_blank"
            rel="noreferrer"
            className="bpp-about-github-button"
          >
            <GithubMark />
            <span>GitHub</span>
            <ExternalLink
              size={12}
              className="bpp-about-github-external"
              aria-hidden="true"
            />
          </a>
        </div>
      </section>

      <section className="bpp-panel bpp-card-pad">
        <h3 className="bpp-section-label">{t('aboutCredits')}</h3>
        <div className="flex flex-col gap-5">
          {groupCredits(bootstrap.credits).map((group) => (
            <div key={group.key} className="flex flex-col gap-3">
              <h4 className="bpp-about-group-heading">
                {t(CREDIT_GROUP_LABELS[group.key] ?? 'aboutContributors')}
              </h4>
              <ul className="m-0 grid list-none grid-cols-2 gap-1 p-0 max-[900px]:grid-cols-1">
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

      <details className="bpp-panel bpp-card-pad group">
        <summary className="bpp-about-license-summary">
          {t('aboutLicenses')}
          <span className="bpp-about-license-symbol">+</span>
        </summary>
        <ul className="m-0 mt-4 grid list-none grid-cols-2 gap-1 p-0">
          {bootstrap.licenses.map((license) => (
            <ListItem
              key={`${license.name}:${license.category}`}
              name={license.name}
              role={license.license}
              isLicense
            />
          ))}
        </ul>
      </details>

      <footer className="mt-1 flex flex-col items-center opacity-55">
        <img
          src={fableVerifiedBadge}
          alt={t('aboutVerifiedBadge')}
          draggable={false}
          className="h-auto w-full max-w-[300px] select-none"
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

  const diagnostic = resource.problem?.diagnostic
    ? formatProblemDiagnostic(resource.problem)
    : null;
  const retryAction = resource.problem ? (
    <Button
      type="button"
      size="small"
      variant="ghost"
      onClick={onRetry}
      disabled={resource.retrying}
      busy={resource.retrying}
      busyLabel={t('aboutRetrying')}
    >
      {t('retry')}
    </Button>
  ) : null;

  if (resource.phase === 'blocking-failure') {
    return (
      <ProblemBanner
        message={t('aboutBlockingFailure')}
        diagnostic={diagnostic}
        diagnosticLabel={t('problemDiagnostics')}
        actions={retryAction}
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
      diagnostic={diagnostic}
      diagnosticLabel={t('problemDiagnostics')}
      actions={retryAction}
    />
  );
}

function ListItem({
  name,
  role,
  href,
  isLicense = false
}: {
  name: string;
  role?: string;
  href?: string | null;
  isLicense?: boolean;
}) {
  const nameClassName = 'bpp-about-list-name fira-code';

  return (
    <li className="bpp-about-list-item">
      {href ? (
        <a
          href={href}
          target="_blank"
          rel="noopener noreferrer"
          aria-label={`${name} GitHub`}
          className={`${nameClassName} bpp-about-list-link`}
        >
          {name}
          <ExternalLink size={11} aria-hidden="true" />
        </a>
      ) : (
        <span className={nameClassName}>{name}</span>
      )}
      {role && (
        <span
          className={`bpp-about-list-role ${isLicense ? 'fira-code' : 'cinzel is-credit'}`}
        >
          {role}
        </span>
      )}
    </li>
  );
}

function GithubMark() {
  return (
    <svg
      viewBox="0 0 24 24"
      width="18"
      height="18"
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
