import { useQuery } from '@tanstack/react-query';

import type { ResolvedSpaLocation } from '../../app/router';
import { getSiteCopy, type DownloadPageCopy } from '../../content/site-copy';
import Button from '../../shared/components/Button';
import { AlertIcon, DownloadIcon, ExternalLinkIcon } from '../../shared/components/icons';
import InfoPageShell from '../../shared/components/InfoPageShell';
import { SectionHeading } from '../../shared/components/PageLayout';
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
    <article className="panel flex flex-col gap-6 p-6">
      <div className="flex items-center gap-4">
        <span className="flex size-12 shrink-0 items-center justify-center rounded-panel border border-line bg-canvas">
          <img src={PLATFORM_ICONS[platform]} alt="" aria-hidden="true" className="size-7" />
        </span>
        <div className="min-w-0">
          <SectionHeading>{platformCopy.title}</SectionHeading>
          <p className="mt-1 text-[13px] text-text-3">{platformCopy.arch}</p>
        </div>
      </div>

      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span className="text-[13px] text-text-3">{copy.versionLabel}</span>
        {status === 'loading' ? (
          <span className="inline-block h-5 w-20 self-center rounded-control bg-hover motion-safe:animate-pulse" />
        ) : installer ? (
          <span className="text-lg font-semibold text-accent tabular-nums">
            v{installer.version}
          </span>
        ) : (
          <span className="text-sm text-text-2">
            {status === 'error' ? copy.versionUnavailable : copy.versionPending}
          </span>
        )}
      </div>

      <div className="mt-auto flex flex-wrap gap-2">
        <Button
          variant="primary"
          href={download?.downloadUrl ?? '#'}
          disabled={disabled}
          icon={<DownloadIcon />}
        >
          {platformCopy.actionLabel}
        </Button>
        <Button
          href={download?.mainlandDownloadUrl ?? '#'}
          target="_blank"
          rel="noreferrer"
          aria-label={platformCopy.mainlandActionLabel}
          disabled={disabled}
          icon={<ExternalLinkIcon />}
        >
          {copy.mainlandButtonLabel}
        </Button>
      </div>
    </article>
  );
}

function FailureFallback({ copy }: { copy: DownloadPageCopy }) {
  return (
    <p className="flex items-start gap-2.5 rounded-panel border border-warning/30 bg-warning-subtle px-3 py-2.5 text-[13px] text-text-1">
      <AlertIcon className="mt-0.5 size-4 text-warning" />
      <span>
        {copy.versionFailed}
        {' · '}
        {copy.releaseFallbackPrefix}
        <a
          href={GITHUB_RELEASE_URL}
          target="_blank"
          rel="noreferrer"
          className="text-accent underline-offset-4 hover:underline"
        >
          {copy.releaseFallbackLink}
        </a>
      </span>
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
    <InfoPageShell location={location} title={copy.title}>
      <section className="flex flex-col gap-4">
        {isError ? <FailureFallback copy={copy} /> : null}
        <div className="grid gap-4 md:grid-cols-2">
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
      </section>

      <section className="panel flex flex-col gap-3 p-6">
        <h2 className="text-[15px] font-semibold text-text-1">{copy.noteTitle}</h2>
        <ul className="flex flex-col gap-2 text-sm text-text-2">
          {copy.noteParagraphs.map((paragraph, index) => (
            <li key={index} className="flex items-start gap-3">
              <span aria-hidden="true" className="mt-2.5 size-1 shrink-0 rounded-full bg-text-3" />
              <span>{paragraph}</span>
            </li>
          ))}
        </ul>
      </section>
    </InfoPageShell>
  );
}
