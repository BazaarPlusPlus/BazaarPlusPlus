import { useQuery } from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';

import type { Locale, ResolvedSpaLocation } from '../../app/router';
import { getSiteCopy, type DownloadPageCopy } from '../../content/site-copy';
import Badge from '../../shared/components/Badge';
import Button from '../../shared/components/Button';
import {
  AlertIcon,
  CheckIcon,
  DownloadIcon,
  ExternalLinkIcon,
  UsersIcon,
} from '../../shared/components/icons';
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

function PlatformHeader({
  icon,
  title,
  arch,
  badge,
}: {
  icon: string;
  title: string;
  arch: string;
  badge?: ReactNode;
}) {
  return (
    <div className="flex items-center gap-4">
      <span className="flex size-12 shrink-0 items-center justify-center rounded-panel border border-line bg-canvas">
        <img src={icon} alt="" aria-hidden="true" className="size-7" />
      </span>
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <SectionHeading>{title}</SectionHeading>
          {badge}
        </div>
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

/** One key-value row of the Android card's APK facts. */
function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-[15px] text-text-3">{label}</dt>
      <dd className="min-w-0 text-[15px] text-text-1">{children}</dd>
    </>
  );
}

/** Where Android players report problems while the build is experimental. */
const ANDROID_FEEDBACK_QQ_GROUP = '1083470305';
const COPIED_RESET_MS = 2000;

/** The Android feedback QQ group as a button that copies its number. */
function FeedbackGroupButton({ copy }: { copy: DownloadPageCopy['android'] }) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) return;
    const timer = window.setTimeout(() => setCopied(false), COPIED_RESET_MS);
    return () => window.clearTimeout(timer);
  }, [copied]);

  return (
    <Button
      title={copy.feedbackCopy}
      icon={copied ? <CheckIcon className="text-success" /> : <UsersIcon />}
      onClick={() => {
        void navigator.clipboard?.writeText(ANDROID_FEEDBACK_QQ_GROUP).then(() => setCopied(true));
      }}
    >
      <span aria-live="polite">
        {copied ? (
          copy.feedbackCopied
        ) : (
          <>
            {copy.feedbackGroup} <span className="tabular-nums">{ANDROID_FEEDBACK_QQ_GROUP}</span>
          </>
        )}
      </span>
    </Button>
  );
}

/**
 * The experimental Android App APK and the steps to install it. While Android's Platform
 * Release Manifest is missing or invalid, the card degrades like a desktop card: an unavailable
 * version and a disabled download, with its badge, notice, feedback group and steps kept.
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

  return (
    <article className="panel flex flex-col gap-6 p-6">
      <div className="flex flex-col gap-4">
        <PlatformHeader
          icon={androidIconDataUri}
          title={androidCopy.title}
          arch={androidCopy.arch}
          badge={<Badge tone="warn">{androidCopy.badge}</Badge>}
        />
        <Banner>{androidCopy.notice}</Banner>
      </div>

      <div className="grid gap-6 md:grid-cols-2 md:gap-8">
        <div className="flex min-w-0 flex-col gap-6">
          <dl className="grid grid-cols-[auto_minmax(0,1fr)] items-baseline gap-x-6 gap-y-2">
            <Fact label={copy.versionLabel}>
              {loading ? (
                <span className="inline-block h-5 w-20 rounded-control bg-hover align-middle motion-safe:animate-pulse" />
              ) : release ? (
                <span className="text-lg font-semibold text-accent tabular-nums">
                  v{release.version}
                </span>
              ) : (
                <span className="text-text-2">{copy.versionUnavailable}</span>
              )}
            </Fact>
            {release ? (
              <>
                <Fact label={androidCopy.gameVersionLabel}>
                  <span className="tabular-nums">
                    {gameReleaseNumber(release.game.gameVersion)}
                  </span>
                </Fact>
                <Fact label={androidCopy.sizeLabel}>
                  <span className="tabular-nums">{formatFileSize(release.app.size, locale)}</span>
                </Fact>
                <Fact label={androidCopy.sha256Label}>
                  <code className="font-(family-name:--font-mono) text-[13px] break-all text-text-2">
                    {release.app.sha256}
                  </code>
                </Fact>
              </>
            ) : null}
          </dl>

          <div className="mt-auto flex flex-wrap gap-2">
            <Button
              variant="primary"
              href={release?.app.url ?? '#'}
              disabled={release == null}
              icon={<DownloadIcon />}
            >
              {androidCopy.actionLabel}
            </Button>
            <FeedbackGroupButton copy={androidCopy} />
          </div>
        </div>

        <div className="flex flex-col gap-3 border-t border-line pt-6 md:border-t-0 md:border-l md:pt-0 md:pl-8">
          <h3 className="text-[15px] font-semibold text-text-1">{androidCopy.stepsTitle}</h3>
          <ol className="flex flex-col gap-2.5 text-[15px] text-text-2">
            {androidCopy.steps.map((step, index) => (
              <li key={index} className="flex items-start gap-3">
                {/* 20px mark centred on the 24px first line. */}
                <span
                  aria-hidden="true"
                  className="mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full bg-hover text-[13px] font-semibold text-text-1 tabular-nums"
                >
                  {index + 1}
                </span>
                <span>{step}</span>
              </li>
            ))}
          </ol>
        </div>
      </div>
    </article>
  );
}

/** A warning banner: icon and message share the first line (design/components/StatusBanner). */
function Banner({ children }: { children: ReactNode }) {
  return (
    <p className="flex items-start gap-2 rounded-panel border border-warning-line bg-warning-subtle px-3 py-2.5 text-[15px] leading-6 text-text-1">
      <span aria-hidden="true" className="flex h-6 shrink-0 items-center">
        <AlertIcon className="size-4 text-warning" />
      </span>
      <span>{children}</span>
    </p>
  );
}

function FailureFallback({ copy }: { copy: DownloadPageCopy }) {
  return (
    <Banner>
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
    </Banner>
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
  // Android is not in the lockstep release: its card reads its own manifest, and a missing or
  // invalid one degrades only that card, never the page-level GitHub fallback.
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
