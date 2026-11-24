import { useState, type CSSProperties } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Badge, Button, StatusBanner, StatusPanel } from '@bpp/ui';

import {
  buildRouteHref,
  type AccountRoutePage,
  type Locale,
  type ResolvedSpaLocation
} from '../../app/router';
import { fillCopy, getSiteCopy } from '../../content/site-copy';
import HeroBadge from '../../shared/components/HeroBadge';
import PageLayout from '../../shared/components/PageLayout';
import { getHeroColor } from '../../shared/lib/heroes';
import { getIntlLocale } from '../../shared/lib/intl-locale';
import {
  AccountHttpError,
  loadRun,
  loadRunPayload
} from '../account/account-api';
import {
  ACCOUNT_QUERY,
  RunFacts,
  RunOutcome,
  heroName,
  useSignedOutRedirect
} from '../account/account-ui';
import { useCardArt, useCardNames } from './card-art';
import DecisionPath from './DecisionPath';
import { decodeRunPayload, type RunCard, type RunDetail } from './run-payload';
import {
  buildTimeline,
  type CardDiff,
  type TimelineCard,
  type TimelineStep
} from './run-timeline';

type Props = {
  location: ResolvedSpaLocation;
  replacePage: (page: AccountRoutePage) => void;
};

type ArtLookup = (templateId: string) => string | null;

const BOARD_SLOTS = 10;

const TIER_COLOR: Record<string, string> = {
  Bronze: 'var(--tier-bronze)',
  Silver: 'var(--tier-silver)',
  Gold: 'var(--accent)',
  Diamond: 'var(--tier-diamond)',
  Legendary: 'var(--tier-legendary)'
};

function tierName(locale: Locale, tier: string | null): string {
  return tier ? (getSiteCopy(locale).account.tiers[tier] ?? tier) : '';
}

function CardFace({
  card,
  art,
  locale,
  width,
  height,
  className,
  style
}: {
  card: TimelineCard;
  art: ArtLookup;
  locale: Locale;
  width: number;
  height: number;
  className: string;
  style?: CSSProperties;
}) {
  const [failed, setFailed] = useState(false);
  const url = failed ? null : art(card.templateId);
  const name = card.name ?? card.templateId;
  const tier = tierName(locale, card.tier);
  return (
    <div
      title={[name, tier, card.enchant].filter(Boolean).join(' · ')}
      className={`relative flex items-center justify-center overflow-hidden border-2 bg-hover ${className}`}
      style={{
        borderColor: TIER_COLOR[card.tier ?? ''] ?? 'var(--line-strong)',
        ...style
      }}
    >
      {url ? (
        <img
          src={url}
          alt={name}
          width={width}
          height={height}
          loading="lazy"
          decoding="async"
          onError={() => setFailed(true)}
          className="size-full object-cover"
        />
      ) : (
        <span className="line-clamp-4 px-0.5 text-center text-micro leading-tight break-words text-text-2">
          {name}
        </span>
      )}
      {tier && <span className="sr-only">{tier}</span>}
      {card.change && (
        <span
          aria-hidden="true"
          className={`absolute top-0.5 left-0.5 flex size-4 items-center justify-center rounded-inner text-micro leading-none font-semibold ${
            card.change === 'new'
              ? 'bg-success text-canvas'
              : 'bg-accent text-on-accent'
          }`}
        >
          {card.change === 'new' ? '+' : '↑'}
        </span>
      )}
    </div>
  );
}

function Board({
  diff,
  art,
  locale
}: {
  diff: CardDiff | null;
  art: ArtLookup;
  locale: Locale;
}) {
  const copy = getSiteCopy(locale).account;
  if (!diff) {
    return (
      <div className="flex aspect-[5/1] items-center justify-center rounded-control border border-dashed border-line text-small text-text-3">
        {copy.notRecorded}
      </div>
    );
  }
  return (
    <div className="relative grid aspect-[5/1] grid-cols-10 gap-1">
      {Array.from({ length: BOARD_SLOTS }, (_, slot) => (
        <div
          key={slot}
          aria-hidden="true"
          className="rounded-inner border border-dashed border-line"
          style={{ gridColumn: slot + 1, gridRow: 1 }}
        />
      ))}
      {diff.cards.map((card) => {
        const start = Math.min(
          Math.max(card.slot ?? 0, 0),
          BOARD_SLOTS - card.size
        );
        return (
          <CardFace
            key={card.instanceId}
            card={card}
            art={art}
            locale={locale}
            width={card.size * 64}
            height={128}
            className="rounded-inner"
            style={{
              gridColumn: `${start + 1} / span ${card.size}`,
              gridRow: 1
            }}
          />
        );
      })}
      {diff.cards.length === 0 && (
        <span className="absolute inset-0 flex items-center justify-center text-small text-text-3">
          {copy.emptyBoard}
        </span>
      )}
    </div>
  );
}

function cardName(card: RunCard): string {
  return card.name ?? card.templateId;
}

function Changes({ step, locale }: { step: TimelineStep; locale: Locale }) {
  const copy = getSiteCopy(locale).account;
  const cards = [...(step.board?.cards ?? []), ...(step.skills?.cards ?? [])];
  const added = cards.filter((c) => c.change === 'new');
  const upgraded = cards.filter((c) => c.change === 'tier-up');
  const left = [...(step.board?.left ?? []), ...(step.skills?.left ?? [])];
  const rows = [
    {
      mark: '+',
      label: copy.added,
      tone: 'text-success',
      values: added.map(cardName)
    },
    {
      mark: '↑',
      label: copy.tierUp,
      tone: 'text-accent',
      values: upgraded.map(
        (c) =>
          `${cardName(c)} ${tierName(locale, c.previousTier)}→${tierName(locale, c.tier)}`
      )
    },
    {
      mark: '−',
      label: copy.left,
      tone: 'text-text-3',
      values: left.map(cardName)
    }
  ].filter((row) => row.values.length > 0);
  if (rows.length === 0) return null;
  return (
    <dl className="grid grid-cols-[auto_minmax(0,1fr)] content-start gap-x-3 gap-y-1.5 text-small">
      {rows.map((row) => (
        <div key={row.label} className="contents">
          <dt className={`font-medium whitespace-nowrap ${row.tone}`}>
            <span aria-hidden="true">{row.mark} </span>
            {row.label}
          </dt>
          <dd className="text-text-2">{row.values.join(' · ')}</dd>
        </div>
      ))}
    </dl>
  );
}

function StepBody({
  step,
  art,
  locale,
  showDay = true
}: {
  step: TimelineStep;
  art: ArtLookup;
  locale: Locale;
  /** False inside the decision path, whose day group already names the day. */
  showDay?: boolean;
}) {
  const copy = getSiteCopy(locale).account;
  const { battle } = step;
  return (
    <>
      <div className="flex flex-wrap items-center gap-2">
        {showDay && (
          <span className="text-body-medium text-text-1 tabular-nums">
            {battle.day == null ? '—' : fillCopy(copy.day, { day: battle.day })}
          </span>
        )}
        <Badge dot={false}>{battle.pvp ? copy.pvp : copy.pve}</Badge>
        {battle.opponentHero && (
          <HeroBadge hero={heroName(battle.opponentHero)} label="full" />
        )}
        {battle.source === 'pve-event' && battle.encounterId && (
          <span className="min-w-0 truncate text-small text-text-3">
            {battle.encounterId}
          </span>
        )}
        {battle.outcome && (
          <span className="ml-auto">
            <Badge tone={battle.outcome === 'win' ? 'ok' : 'bad'}>
              {battle.outcome === 'win' ? copy.win : copy.loss}
            </Badge>
          </span>
        )}
      </div>
      {battle.source === 'battle' && (
        <div className="grid gap-3 lg:grid-cols-[minmax(0,48rem)_minmax(0,1fr)] lg:gap-6">
          <div className="flex min-w-0 flex-col gap-2">
            <Board diff={step.board} art={art} locale={locale} />
            {step.skills && step.skills.cards.length > 0 && (
              <ul aria-label={copy.skills} className="flex flex-wrap gap-1.5">
                {step.skills.cards.map((card) => (
                  <li key={card.instanceId}>
                    <CardFace
                      card={card}
                      art={art}
                      locale={locale}
                      width={36}
                      height={36}
                      className="size-9 rounded-control"
                    />
                  </li>
                ))}
              </ul>
            )}
          </div>
          <Changes step={step} locale={locale} />
        </div>
      )}
    </>
  );
}

function Battles({
  steps,
  art,
  locale
}: {
  steps: TimelineStep[];
  art: ArtLookup;
  locale: Locale;
}) {
  return (
    <ol className="flex flex-col gap-3">
      {steps.map((step) => (
        <li
          key={step.battle.id}
          className="bpp-panel flex flex-col gap-3 p-3 sm:p-4"
        >
          <StepBody step={step} art={art} locale={locale} />
        </li>
      ))}
    </ol>
  );
}

/** The decision path, decoded from the Run Message Stream after the page shows. */
function Decisions({
  detail,
  steps,
  art,
  locale
}: {
  detail: RunDetail;
  steps: TimelineStep[];
  art: ArtLookup;
  locale: Locale;
}) {
  const copy = getSiteCopy(locale).account;
  // GameData titles first; the Run's own Battle snapshots name a card the index does not.
  const gameNames = useCardNames();
  const path = useQuery({
    queryKey: [...ACCOUNT_QUERY, 'run-decisions', detail.runId],
    queryFn: async () =>
      (await import('./message-stream/decision-path')).buildDecisionPath(
        detail
      ),
    staleTime: Infinity,
    retry: false
  });
  if (path.isPending) {
    return <StatusPanel kind="loading" compact title={copy.loading} />;
  }
  if (path.isError || !path.data) {
    return (
      <>
        <StatusBanner tone="warning" message={copy.path.error} />
        <Battles steps={steps} art={art} locale={locale} />
      </>
    );
  }
  const byId = new Map(steps.map((step) => [step.battle.id, step]));
  return (
    <DecisionPath
      path={path.data}
      names={(templateId) =>
        gameNames(templateId) ?? detail.cardNames.get(templateId) ?? null
      }
      art={art}
      locale={locale}
      renderBattle={(id) => {
        const step = byId.get(id);
        return step ? (
          <div className="flex flex-col gap-3 rounded-control border border-line p-3">
            <StepBody step={step} art={art} locale={locale} showDay={false} />
          </div>
        ) : null;
      }}
    />
  );
}

function Timeline({ runId, locale }: { runId: string; locale: Locale }) {
  const copy = getSiteCopy(locale).account;
  const art = useCardArt();
  const detail = useQuery({
    queryKey: [...ACCOUNT_QUERY, 'run-detail', runId],
    queryFn: async ({ signal }) => {
      const run = await decodeRunPayload(
        await loadRunPayload(runId, signal),
        runId
      );
      return { run, steps: buildTimeline(run.battles) };
    },
    staleTime: Infinity,
    retry: false
  });
  if (detail.isPending) {
    return <StatusPanel kind="loading" compact title={copy.loading} />;
  }
  if (detail.isError) {
    return (
      <StatusPanel
        kind="error"
        title={copy.detailError}
        actions={
          <Button onClick={() => void detail.refetch()}>{copy.retry}</Button>
        }
      />
    );
  }
  const { run, steps } = detail.data;
  if (run.stream) {
    return <Decisions detail={run} steps={steps} art={art} locale={locale} />;
  }
  return (
    <>
      {run.streamOmitted && (
        <StatusBanner tone="info" message={copy.path.omitted} />
      )}
      <Battles steps={steps} art={art} locale={locale} />
    </>
  );
}

export default function RunPage({ location, replacePage }: Props) {
  const { locale } = location;
  const copy = getSiteCopy(locale).account;
  const runId = location.route.page === 'run' ? location.route.runId : '';
  const summary = useQuery({
    queryKey: [...ACCOUNT_QUERY, 'run', runId],
    queryFn: ({ signal }) => loadRun(runId, signal),
    staleTime: 0,
    retry: false
  });
  const signedOut = useSignedOutRedirect([summary.error], replacePage);
  if (signedOut) return null;
  const notFound =
    summary.error instanceof AccountHttpError && summary.error.status === 404;
  // A Run that is gone (unlinked or deleted elsewhere) never shows its cached data.
  const run = notFound ? undefined : summary.data;
  const hero = heroName(run?.hero ?? '—');
  const recordsHref = buildRouteHref('/records', locale);
  const date = new Intl.DateTimeFormat(getIntlLocale(locale), {
    dateStyle: 'medium',
    timeStyle: 'short'
  });
  return (
    <PageLayout location={location} width="wide" busy={summary.isPending}>
      <div className="flex flex-col gap-3">
        <a
          href={recordsHref}
          className="w-fit text-small text-text-2 no-underline hover:text-text-1"
        >
          ← {copy.allRecords}
        </a>
        {run && (
          <>
            <div className="flex flex-wrap items-center gap-3">
              <span
                aria-hidden="true"
                className="size-3 shrink-0 rounded-pill"
                style={{ backgroundColor: getHeroColor(hero) }}
              />
              <h1 className="text-display leading-[1.3] font-semibold text-text-1">
                {hero}
              </h1>
              <RunOutcome
                locale={locale}
                victories={run.victories}
                losses={run.losses}
              />
            </div>
            <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1 text-small tabular-nums">
              <RunFacts run={run} locale={locale} />
              {run.ended_at_ms != null && (
                <span className="text-text-3">
                  {date.format(run.ended_at_ms)}
                </span>
              )}
            </div>
          </>
        )}
      </div>
      {summary.isPending && (
        <StatusPanel kind="loading" compact title={copy.loading} />
      )}
      {summary.isError &&
        (notFound ? (
          <StatusPanel kind="not-found" title={copy.notFound} />
        ) : (
          <StatusPanel
            kind="error"
            title={copy.loadError}
            actions={
              <Button onClick={() => void summary.refetch()}>
                {copy.retry}
              </Button>
            }
          />
        ))}
      {run &&
        (run.has_detail ? (
          <Timeline runId={runId} locale={locale} />
        ) : (
          <StatusPanel kind="empty" title={copy.noDetail} />
        ))}
    </PageLayout>
  );
}
