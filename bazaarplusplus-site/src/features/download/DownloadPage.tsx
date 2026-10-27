import { useQuery } from '@tanstack/react-query';

import type { Locale, ResolvedSpaLocation } from '../../app/router';
import { getSiteCopy, type DownloadPageCopy } from '../../content/site-copy';
import Button from '../../shared/components/Button';
import { AlertIcon, DownloadIcon, ExternalLinkIcon } from '../../shared/components/icons';
import InfoPageShell from '../../shared/components/InfoPageShell';
import { SectionHeading } from '../../shared/components/PageLayout';
import {
  GITHUB_RELEASE_URL,
  gameReleaseNumber,
  loadAndroidRelease,
  loadLatestInstaller,
  type AndroidDownload,
  type DownloadPlatform,
  type LatestInstaller,
} from './installer';
import { formatFileSize } from './file-size';
import { androidIconDataUri, macIconDataUri, windowsIconDataUri } from './platform-icons';

const DOWNLOAD_PLATFORMS: DownloadPlatform[] = ['windows', 'mac'];
const PLATFORM_ICONS: Record<DownloadPlatform, string> = {
  windows: windowsIconDataUri,
  mac: macIconDataUri,
};

type DownloadStatus = 'loading' | 'error' | 'ready';

function PlatformHeader({ icon, title, arch }: { icon: string; title: string; arch: string }) {
  return (
    <div className="flex items-center gap-4">
      <span className="flex size-12 shrink-0 items-center justify-center rounded-panel border border-line bg-canvas">
        <img src={icon} alt="" aria-hidden="true" className="size-7" />
      </span>
      <div className="min-w-0">
        <SectionHeading>{title}</SectionHeading>
        <p className="mt-1 text-[15px] text-text-3">{arch}</p>
      </div>
    </div>
  );
}

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
  const mainlandDownloadUrl = download?.mainlandDownloadUrl ?? null;

  return (
    <article className="panel flex flex-col gap-6 p-6">
      <PlatformHeader
        icon={PLATFORM_ICONS[platform]}
        title={platformCopy.title}
        arch={platformCopy.arch}
      />

      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span className="text-[15px] text-text-3">{copy.versionLabel}</span>
        {status === 'loading' ? (
          <span className="inline-block h-5 w-20 self-center rounded-control bg-hover motion-safe:animate-pulse" />
        ) : download ? (
          <span className="text-lg font-semibold text-accent tabular-nums">
            v{download.version}
          </span>
        ) : (
          <span className="text-[15px] text-text-2">{copy.versionUnavailable}</span>
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
          href={mainlandDownloadUrl ?? '#'}
          target="_blank"
          rel="noreferrer"
          aria-label={platformCopy.mainlandActionLabel}
          disabled={mainlandDownloadUrl == null}
          icon={<ExternalLinkIcon />}
        >
          {copy.mainlandButtonLabel}
        </Button>
      </div>
    </article>
  );
}

/**
 * The Android Release APK and the steps to install it. Until Android's Platform Release Manifest
 * loads (or while it is missing or invalid), the card stays with its steps, a pending version, a
 * disabled download, and a pointer to the GitHub release page.
 */
function AndroidCard({
  copy,
  locale,
  release,
  loading,
}: {
  copy: DownloadPageCopy;
  locale: Locale;
  release: AndroidDownload | undefined;
  loading: boolean;
}) {
  const androidCopy = copy.android;
  const apkName = release
    ? androidCopy.apkName.replace('{gameVersion}', gameReleaseNumber(release.game.gameVersion))
    : null;

  return (
    <article className="panel grid gap-6 p-6 md:grid-cols-2 md:gap-8">
      <div className="flex min-w-0 flex-col gap-6">
        <PlatformHeader
          icon={androidIconDataUri}
          title={androidCopy.title}
          arch={androidCopy.arch}
        />

        <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
          <span className="text-[15px] text-text-3">{copy.versionLabel}</span>
          {loading ? (
            <span className="inline-block h-5 w-20 self-center rounded-control bg-hover motion-safe:animate-pulse" />
          ) : release ? (
            <span className="text-lg font-semibold text-accent tabular-nums">
              v{release.version}
            </span>
          ) : (
            <span className="text-[15px] text-text-2">{androidCopy.pendingVersion}</span>
          )}
        </div>

        {release && apkName ? (
          <div className="flex min-w-0 flex-col gap-1.5">
            <p className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
              <span className="text-[15px] font-semibold text-text-1">{apkName}</span>
              <span className="text-[15px] text-text-3 tabular-nums">
                {formatFileSize(release.apk.size, locale)}
              </span>
            </p>
            <p className="text-sm text-text-3">
              {androidCopy.sha256Label}{' '}
              <code className="font-(family-name:--font-mono) break-all text-text-2">
                {release.apk.sha256}
              </code>
            </p>
          </div>
        ) : loading ? null : (
          <p className="text-[15px] text-text-2">
            {androidCopy.pendingNote}
            <a
              href={GITHUB_RELEASE_URL}
              target="_blank"
              rel="noreferrer"
              className="text-accent underline-offset-4 hover:underline"
            >
              {copy.releaseFallbackLink}
            </a>
          </p>
        )}

        <div className="mt-auto flex flex-wrap gap-2">
          <Button
            variant="primary"
            href={release?.apk.url ?? '#'}
            disabled={release == null}
            icon={<DownloadIcon />}
          >
            {androidCopy.actionLabel}
          </Button>
        </div>
      </div>

      <div className="flex flex-col gap-3 border-t border-line pt-6 md:border-t-0 md:border-l md:pt-0 md:pl-8">
        <h3 className="text-[15px] font-semibold text-text-1">{androidCopy.stepsTitle}</h3>
        <ol className="flex flex-col gap-2.5 text-[15px] text-text-2">
          {androidCopy.steps.map((step, index) => (
            <li key={index} className="flex items-start gap-3">
              <span
                aria-hidden="true"
                className="flex size-5 shrink-0 items-center justify-center rounded-full bg-hover text-sm font-semibold text-text-1 tabular-nums"
              >
                {index + 1}
              </span>
              <span>{step}</span>
            </li>
          ))}
        </ol>
      </div>
    </article>
  );
}

function FailureFallback({ copy }: { copy: DownloadPageCopy }) {
  return (
    <p className="flex items-start gap-2.5 rounded-panel border border-warning/30 bg-warning-subtle px-3 py-2.5 text-[15px] text-text-1">
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

export default function DownloadPage({ location }: { location: ResolvedSpaLocation }) {
  const { locale } = location;
  const copy = getSiteCopy(locale).download;
  const { data, isLoading, isError } = useQuery({
    queryKey: ['latest-installer'],
    queryFn: ({ signal }) => loadLatestInstaller(signal),
  });
  const status: DownloadStatus = isLoading ? 'loading' : isError ? 'error' : 'ready';
  // Android is not in the lockstep release: its card shows a pending state until its own
  // manifest loads, and whenever that manifest is missing or invalid.
  const { data: android, isLoading: androidLoading } = useQuery({
    queryKey: ['android-release'],
    queryFn: ({ signal }) => loadAndroidRelease(signal),
  });

  return (
    <InfoPageShell location={location} title={copy.title} intro={copy.intro}>
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
        <AndroidCard copy={copy} locale={locale} release={android} loading={androidLoading} />
      </section>

      <section className="panel flex flex-col gap-3 p-6">
        <h2 className="text-[15px] font-semibold text-text-1">{copy.desktopNoteTitle}</h2>
        <ul className="flex flex-col gap-2 text-[15px] text-text-2">
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
