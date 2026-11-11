import { useQuery } from '@tanstack/react-query';
import { useEffect, useState, type ReactNode } from 'react';

import type { Locale, ResolvedSpaLocation } from '../../app/router';
import { getSiteCopy, type DownloadPageCopy } from '../../content/site-copy';
import { AlertIcon, Badge, Button } from '@bpp/ui';
import {
  CheckIcon,
  DownloadIcon,
  ExternalLinkIcon,
  UsersIcon
} from '../../shared/components/icons';
import InfoPageShell from '../../shared/components/InfoPageShell';
import { SectionHeading } from '../../shared/components/PageLayout';
import {
  GITHUB_RELEASE_URL,
  gameReleaseNumber,
  loadAndroidGameStatus,
  loadAndroidRelease,
  loadGameStatus,
  loadLatestInstaller,
  sameAndroidGame,
  sameDesktopGame,
  type AndroidDownload,
  type AndroidGameStatus,
  type DownloadPlatform,
  type GameStatus,
  type LatestInstaller
} from './installer';
import { formatFileSize } from './file-size';
import {
  androidIconDataUri,
  macIconDataUri,
  windowsIconDataUri
} from './platform-icons';

const DOWNLOAD_PLATFORMS: DownloadPlatform[] = ['windows', 'mac'];
const PLATFORM_ICONS: Record<DownloadPlatform, string> = {
  windows: windowsIconDataUri,
  mac: macIconDataUri
};

type DownloadStatus = 'loading' | 'error' | 'ready';

function PlatformHeader({
  icon,
  title,
  arch,
  badge
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
        <p className="mt-1 text-body text-text-3">{arch}</p>
      </div>
    </div>
  );
}

/**
 * The game a release is adapted to and, while the Game Status is readable, the online game with
 * the conclusion (ADR 0016). `same` compares their game code identity.
 */
function GameFacts({
  copy,
  adapted,
  online,
  same
}: {
  copy: DownloadPageCopy;
  adapted: { gameVersion: string };
  online: { gameVersion: string } | undefined;
  same: boolean;
}) {
  return (
    <>
      <Fact label={copy.adaptedGameLabel}>
        <span className="tabular-nums">
          {gameReleaseNumber(adapted.gameVersion)}
        </span>
      </Fact>
      {online ? (
        <Fact label={copy.onlineGameLabel}>
          <span className="inline-flex flex-wrap items-center gap-x-2 gap-y-1">
            <span className="tabular-nums">
              {gameReleaseNumber(online.gameVersion)}
            </span>
            <Badge tone={same ? 'neutral' : 'warn'} dot={!same}>
              {same ? copy.adapted : copy.notAdapted}
            </Badge>
          </span>
        </Fact>
      ) : null}
    </>
  );
}

function DownloadCard({
  platform,
  copy,
  installer,
  status,
  gameStatus
}: {
  platform: DownloadPlatform;
  copy: DownloadPageCopy;
  installer: LatestInstaller | undefined;
  status: DownloadStatus;
  gameStatus: GameStatus | undefined;
}) {
  const platformCopy = copy[platform];
  const download = installer?.downloads[platform];
  const disabled = download == null;
  const mainlandDownloadUrl = download?.mainlandDownloadUrl ?? null;
  const adapted = download?.game;
  const online = gameStatus?.[platform === 'mac' ? 'macos' : 'windows'];

  return (
    <article className="panel flex flex-col gap-6 p-6">
      <PlatformHeader
        icon={PLATFORM_ICONS[platform]}
        title={platformCopy.title}
        arch={platformCopy.arch}
      />

      <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span className="text-body text-text-3">{copy.versionLabel}</span>
        {status === 'loading' ? (
          <span className="inline-block h-5 w-20 self-center rounded-control bg-hover motion-safe:animate-pulse" />
        ) : download ? (
          <span className="text-lg font-semibold text-accent tabular-nums">
            v{download.version}
          </span>
        ) : (
          <span className="text-body text-text-2">
            {copy.versionUnavailable}
          </span>
        )}
      </div>

      {adapted ? (
        <dl className="grid grid-cols-[auto_minmax(0,1fr)] items-baseline gap-x-6 gap-y-2">
          <GameFacts
            copy={copy}
            adapted={adapted}
            online={online}
            same={online ? sameDesktopGame(adapted, online) : false}
          />
        </dl>
      ) : null}

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

/** One key-value row of a card's facts. */
function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-body text-text-3">{label}</dt>
      <dd className="min-w-0 text-body text-text-1">{children}</dd>
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
        void navigator.clipboard
          ?.writeText(ANDROID_FEEDBACK_QQ_GROUP)
          .then(() => setCopied(true));
      }}
    >
      <span aria-live="polite">
        {copied ? (
          copy.feedbackCopied
        ) : (
          <>
            {copy.feedbackGroup}{' '}
            <span className="tabular-nums">{ANDROID_FEEDBACK_QQ_GROUP}</span>
          </>
        )}
      </span>
    </Button>
  );
}

/** The complete checksum stays selectable and copyable without dominating the download card. */
function AndroidChecksum({
  hash,
  copy
}: {
  hash: string;
  copy: DownloadPageCopy['android'];
}) {
  const [status, setStatus] = useState<'idle' | 'copied' | 'failed'>('idle');
  useEffect(() => {
    if (status === 'idle') return;
    const timer = window.setTimeout(() => setStatus('idle'), COPIED_RESET_MS);
    return () => window.clearTimeout(timer);
  }, [status]);

  return (
    <details className="min-w-0 text-sm text-text-2">
      <summary className="min-h-(--touch-target) cursor-pointer py-2 hover:text-text-1 md:min-h-(--control-md)">
        {copy.sha256Label}{' '}
        <code className="ml-2 font-(family-name:--font-mono)">
          {hash.slice(0, 8)}…{hash.slice(-8)}
        </code>
      </summary>
      <div className="flex min-w-0 flex-col items-start gap-2 pb-2">
        <code className="max-w-full font-(family-name:--font-mono) break-all select-all">
          {hash}
        </code>
        <Button
          size="sm"
          className="max-md:min-h-(--touch-target)"
          onClick={() => {
            void (async () => {
              try {
                await navigator.clipboard.writeText(hash);
                setStatus('copied');
              } catch {
                setStatus('failed');
              }
            })();
          }}
        >
          <span aria-live="polite">
            {status === 'copied' ? copy.checksumCopied : copy.checksumCopy}
          </span>
        </Button>
        {status === 'failed' && <p role="status">{copy.checksumFailed}</p>}
      </div>
    </details>
  );
}

/**
 * Android's own release may be unavailable independently of desktop. Keep installation guidance
 * and feedback available in that state, while disabling the APK download.
 */
function AndroidCard({
  copy,
  locale,
  release,
  loading,
  online
}: {
  copy: DownloadPageCopy;
  locale: Locale;
  release: AndroidDownload | undefined;
  loading: boolean;
  online: AndroidGameStatus | undefined;
}) {
  const androidCopy = copy.android;

  return (
    <article className="panel flex flex-col gap-4 p-6">
      <PlatformHeader
        icon={androidIconDataUri}
        title={androidCopy.title}
        arch={androidCopy.arch}
        badge={<Badge tone="warn">{androidCopy.badge}</Badge>}
      />
      <p className="flex items-start gap-2 text-sm text-text-2">
        <AlertIcon className="mt-1 size-4 shrink-0 text-warning" />
        <span>{androidCopy.notice}</span>
      </p>
      <div className="grid gap-6 md:grid-cols-2">
        <div className="flex min-w-0 flex-col items-start gap-4">
          <dl className="grid grid-cols-[auto_minmax(0,1fr)] items-baseline gap-x-4 gap-y-2">
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
            {release && (
              <>
                <GameFacts
                  copy={copy}
                  adapted={release.game}
                  online={online}
                  same={online ? sameAndroidGame(release.game, online) : false}
                />
                <Fact label={androidCopy.sizeLabel}>
                  <span className="tabular-nums">
                    {formatFileSize(release.app.size, locale)}
                  </span>
                </Fact>
              </>
            )}
          </dl>
          <div className="flex flex-wrap items-center gap-2 max-md:[&>*]:min-h-(--touch-target)">
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
          {release && (
            <AndroidChecksum hash={release.app.sha256} copy={androidCopy} />
          )}
        </div>
        <div className="min-w-0 border-t border-line pt-4 md:border-t-0 md:border-l md:pt-0 md:pl-6">
          <ol
            aria-label={androidCopy.stepsTitle}
            className="flex flex-col gap-3"
          >
            {androidCopy.steps.map((step, index) => (
              <li key={step.title} className="flex min-w-0 items-start gap-3">
                <span
                  className="flex size-6 shrink-0 items-center justify-center rounded-full bg-hover text-sm text-text-2 tabular-nums"
                  aria-hidden="true"
                >
                  {index + 1}
                </span>
                <div className="min-w-0">
                  <p className="font-medium text-text-1">{step.title}</p>
                  <p className="mt-1 text-sm text-text-2">{step.body}</p>
                </div>
              </li>
            ))}
          </ol>
          <details className="mt-3 text-sm">
            <summary className="min-h-(--touch-target) cursor-pointer py-2 text-text-2 hover:text-text-1 md:min-h-(--control-md)">
              {androidCopy.limitsTitle}
            </summary>
            <ul className="flex list-disc flex-col gap-2 pt-2 pl-5 text-text-2">
              {androidCopy.limits.map((limit) => (
                <li key={limit}>{limit}</li>
              ))}
            </ul>
          </details>
        </div>
      </div>
    </article>
  );
}

/** A warning banner: icon and message share the first line (design/components/StatusBanner). */
function Banner({ children }: { children: ReactNode }) {
  return (
    <p className="flex items-start gap-2 rounded-panel border border-warning-line bg-warning-subtle px-3 py-2.5 text-body leading-6 text-text-1">
      <span aria-hidden="true" className="flex h-6 shrink-0 items-center">
        <AlertIcon className="size-4 shrink-0 text-warning" />
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

export default function DownloadPage({
  location
}: {
  location: ResolvedSpaLocation;
}) {
  const { locale } = location;
  const copy = getSiteCopy(locale).download;
  const { data, isLoading, isError } = useQuery({
    queryKey: ['latest-installer'],
    queryFn: ({ signal }) => loadLatestInstaller(signal)
  });
  const status: DownloadStatus = isLoading
    ? 'loading'
    : isError
      ? 'error'
      : 'ready';
  // Android is not in the lockstep release: its card reads its own manifest, and a missing or
  // invalid one degrades only that card, never the page-level GitHub fallback.
  const { data: android, isLoading: androidLoading } = useQuery({
    queryKey: ['android-release'],
    queryFn: ({ signal }) => loadAndroidRelease(signal)
  });
  // Extra information: a missing Game Status costs only the online game and the conclusion.
  const { data: gameStatus } = useQuery({
    queryKey: ['game-status'],
    queryFn: ({ signal }) => loadGameStatus(signal)
  });
  const { data: androidGameStatus } = useQuery({
    queryKey: ['android-game-status'],
    queryFn: ({ signal }) => loadAndroidGameStatus(signal)
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
              gameStatus={gameStatus}
            />
          ))}
        </div>
        <AndroidCard
          copy={copy}
          locale={locale}
          release={android}
          loading={androidLoading}
          online={androidGameStatus}
        />
      </section>

      <section className="panel flex flex-col gap-3 p-6">
        <h2 className="text-body font-semibold text-text-1">
          {copy.desktopNoteTitle}
        </h2>
        <ul className="flex flex-col gap-2 text-body text-text-2">
          {copy.noteParagraphs.map((paragraph, index) => (
            <li key={index} className="flex items-start gap-3">
              <span
                aria-hidden="true"
                className="mt-2.5 size-1 shrink-0 rounded-full bg-text-3"
              />
              <span>{paragraph}</span>
            </li>
          ))}
        </ul>
      </section>
    </InfoPageShell>
  );
}
