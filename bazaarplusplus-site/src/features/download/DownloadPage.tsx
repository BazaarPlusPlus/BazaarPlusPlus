import { useQuery } from '@tanstack/react-query';

import type { ResolvedSpaLocation } from '../../app/router';
import { getSiteCopy, type DownloadPageCopy } from '../../content/site-copy';
import InfoPageShell from '../../shared/components/InfoPageShell';
import {
  createInstallerManifestHttpTransport,
  GITHUB_RELEASE_URL,
  loadLatestInstaller,
  type DownloadPlatform,
  type InstallerManifestTransport,
  type LatestInstaller,
} from './installer';
import { macIconDataUri, windowsIconDataUri } from './platform-icons';

const DEFAULT_TRANSPORT = createInstallerManifestHttpTransport();
const DOWNLOAD_PLATFORMS: DownloadPlatform[] = ['windows', 'mac'];
const PLATFORM_ICONS: Record<DownloadPlatform, string> = {
  windows: windowsIconDataUri,
  mac: macIconDataUri,
};

type DownloadStatus = 'loading' | 'error' | 'ready';

function DownloadCard({
  platform,
  copy,
  installer,
  status,
}: {
  platform: DownloadPlatform;
  copy: DownloadPageCopy;
  installer: LatestInstaller | undefined;
  status: DownloadStatus;
}) {
  const platformCopy = copy[platform];
  const download = installer?.downloads[platform];
  const disabled = download == null;

  return (
    <article className="surface relative flex flex-col gap-6 p-7">
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-center gap-4">
          <span className="flex h-14 w-14 items-center justify-center rounded-2xl border border-[color:var(--color-border-soft)] bg-[rgba(232,185,74,0.04)]">
            <img
              src={PLATFORM_ICONS[platform]}
              alt=""
              aria-hidden="true"
              className="h-9 w-9 opacity-90"
            />
          </span>
          <div>
            <h2 className="font-display text-[1.55rem] font-semibold leading-tight text-[color:var(--color-text-base)]">
              {platformCopy.title}
            </h2>
            <p className="mt-1 text-xs uppercase tracking-[0.16em] text-[color:var(--color-text-muted)]">
              {platformCopy.arch}
            </p>
          </div>
        </div>
      </div>

      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span className="text-[0.7rem] uppercase tracking-[0.18em] text-[color:var(--color-text-faint)]">
          {copy.versionLabel}
        </span>
        {status === 'loading' ? (
          <span className="inline-block h-5 w-24 overflow-hidden rounded-full bg-[color:var(--color-border-soft)]">
            <span className="shimmer block h-full w-full" />
          </span>
        ) : installer ? (
          <span className="tnum text-xl font-medium text-[color:var(--color-accent-bright)]">
            v{installer.version}
          </span>
        ) : status === 'error' ? (
          <span className="text-sm text-[color:var(--color-text-muted)]">
            {copy.versionUnavailable}
          </span>
        ) : (
          <span className="text-sm text-[color:var(--color-text-muted)]">
            {copy.versionPending}
          </span>
        )}
      </div>

      <div className="mt-auto grid grid-cols-[minmax(0,3fr)_minmax(0,2fr)] gap-3">
        <a
          href={download?.downloadUrl ?? '#'}
          aria-disabled={disabled}
          tabIndex={disabled ? -1 : undefined}
          onClick={disabled ? (event) => event.preventDefault() : undefined}
          className={`group inline-flex min-w-0 items-center justify-center gap-2 whitespace-nowrap rounded-full px-3 py-3 text-sm font-medium tracking-[0.04em] transition sm:px-5 ${
            disabled
              ? 'pointer-events-none cursor-not-allowed bg-[color:var(--color-border-soft)] text-[color:var(--color-text-faint)]'
              : 'bg-[color:var(--color-accent)] text-[#1a1306] shadow-[0_18px_36px_-12px_rgba(232,185,74,0.5)] hover:bg-[color:var(--color-accent-bright)]'
          }`}
        >
          <span>{platformCopy.actionLabel}</span>
          <span aria-hidden="true" className="transition group-hover:translate-x-0.5">
            →
          </span>
        </a>

        <a
          href={download?.mainlandDownloadUrl ?? '#'}
          target="_blank"
          rel="noreferrer"
          aria-label={platformCopy.mainlandActionLabel}
          aria-disabled={disabled}
          tabIndex={disabled ? -1 : undefined}
          onClick={disabled ? (event) => event.preventDefault() : undefined}
          className={`group inline-flex min-w-0 items-center justify-center gap-2 whitespace-nowrap rounded-full border px-3 py-3 text-xs font-medium tracking-[0.04em] transition ${
            disabled
              ? 'pointer-events-none cursor-not-allowed border-[color:var(--color-border-soft)] text-[color:var(--color-text-faint)]'
              : 'border-[color:var(--color-border-bright)] bg-[rgba(232,185,74,0.04)] text-[color:var(--color-accent-bright)] hover:bg-[rgba(232,185,74,0.1)]'
          }`}
        >
          <span>{copy.mainlandButtonLabel}</span>
          <span aria-hidden="true" className="transition group-hover:translate-x-0.5">
            ↗
          </span>
        </a>
      </div>
    </article>
  );
}

function FailureFallback({ copy }: { copy: DownloadPageCopy }) {
  return (
    <p className="-mt-4 text-sm leading-6 text-[color:var(--color-text-muted)]">
      {copy.versionFailed}
      {' · '}
      {copy.releaseFallbackPrefix}
      <a
        href={GITHUB_RELEASE_URL}
        target="_blank"
        rel="noreferrer"
        className="text-[color:var(--color-accent-bright)] underline-offset-4 hover:underline"
      >
        {copy.releaseFallbackLink}
      </a>
    </p>
  );
}

export default function DownloadPage({
  location,
  transport = DEFAULT_TRANSPORT,
}: {
  location: ResolvedSpaLocation;
  transport?: InstallerManifestTransport;
}) {
  const { locale } = location;
  const copy = getSiteCopy(locale).download;
  const { data, isLoading, isError } = useQuery({
    queryKey: ['latest-installer'],
    queryFn: ({ signal }) => loadLatestInstaller(transport, { signal }),
    staleTime: 5 * 60_000,
  });
  const status: DownloadStatus = isLoading ? 'loading' : isError ? 'error' : 'ready';

  return (
    <InfoPageShell locale={locale} location={location} title={copy.title}>
      <section className="flex flex-col gap-6">
        <div className="grid gap-6 md:grid-cols-2">
          {DOWNLOAD_PLATFORMS.map((platform) => (
            <DownloadCard
              key={platform}
              platform={platform}
              copy={copy}
              installer={data}
              status={status}
            />
          ))}
        </div>
        {isError ? <FailureFallback copy={copy} /> : null}
      </section>

      <section className="surface-flat flex flex-col gap-3 px-6 py-6">
        <h2 className="font-display text-lg font-semibold tracking-tight text-[color:var(--color-text-base)]">
          {copy.noteTitle}
        </h2>
        <ul className="flex flex-col gap-2 text-sm leading-6 text-[color:var(--color-text-muted)]">
          {copy.noteParagraphs.map((paragraph, index) => (
            <li key={index} className="flex items-start gap-3">
              <span
                aria-hidden="true"
                className="mt-2 inline-block h-1 w-1 rounded-full bg-[color:var(--color-accent)]"
              />
              <span>{paragraph}</span>
            </li>
          ))}
        </ul>
      </section>
    </InfoPageShell>
  );
}
