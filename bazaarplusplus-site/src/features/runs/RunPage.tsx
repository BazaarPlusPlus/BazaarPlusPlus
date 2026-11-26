import { useCallback, useMemo } from 'react';
import { decisionDays } from '@bpp/runs/decisions';
import { useQuery } from '@tanstack/react-query';
import {
  Badge,
  Board,
  SkillStrip,
  Button,
  DayCard,
  StatusPanel
} from '@bpp/ui';

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
import { gameCard } from './game-card';
import { CardDetails, useOpenCard } from './CardDetails';
import { SiteReplay, StreamDebug } from './DayDecisions';
import { decodeRunPayload, type RunCard, type RunDetail } from './run-payload';
import {
  buildTimeline,
  type CardDiff,
  type TimelineStep
} from './run-timeline';

type Props = {
  location: ResolvedSpaLocation;
  replacePage: (page: AccountRoutePage) => void;
};

type ArtLookup = (templateId: string) => string | null;

function tierName(locale: Locale, tier: string | null): string {
  return tier ? (getSiteCopy(locale).account.tiers[tier] ?? tier) : '';
}

function tiles(
  diff: CardDiff | null,
  art: ArtLookup,
  locale: Locale,
  open: ReturnType<typeof useOpenCard>
) {
  return (
    diff?.cards.map((card) => ({
      ...gameCard(card, cardName(card), art, locale, open),
      id: card.instanceId,
      slot: card.slot,
      size: card.size,
      change: card.change
    })) ?? null
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
  locale
}: {
  step: TimelineStep;
  art: ArtLookup;
  locale: Locale;
}) {
  const copy = getSiteCopy(locale).account;
  const open = useOpenCard();
  const { battle } = step;
  return (
    <>
      <div className="flex flex-wrap items-center gap-2">
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
            <Board
              cards={tiles(step.board, art, locale, open)}
              missingLabel={copy.notRecorded}
              emptyLabel={copy.emptyBoard}
            />
            <SkillStrip
              cards={tiles(step.skills, art, locale, open)}
              missingLabel={copy.missingSkills}
              emptyLabel={copy.emptySkills}
              label={copy.skills}
            />
          </div>
          <Changes step={step} locale={locale} />
        </div>
      )}
    </>
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
    enabled: !!detail.stream,
    queryFn: async () =>
      (await import('@bpp/runs/decision-path')).buildDecisionPath(detail),
    staleTime: Infinity,
    retry: false
  });
  const days = decisionDays(steps, (s) => s.battle.day);
  const battles = useMemo(
    () => new Map(steps.map((s) => [s.battle.id, s.battle])),
    [steps]
  );
  const names = useCallback(
    (id: string) => gameNames(id) ?? detail.cardNames.get(id) ?? null,
    [gameNames, detail.cardNames]
  );
  const status = detail.streamOmitted
    ? copy.path.omitted
    : path.isError
      ? copy.path.error
      : path.isPending
        ? copy.loading
        : copy.path.noVisits;
  return (
    <>
      <ol className="flex flex-col gap-3">
        {days.map(({ day, battles: daySteps }) => (
          <DayCard
            key={day ?? 'unknown'}
            title={
              day === null ? copy.path.unknownDay : fillCopy(copy.day, { day })
            }
            battles={daySteps.map((step) => (
              <div key={step.battle.id} className="flex flex-col gap-3">
                <StepBody step={step} art={art} locale={locale} />
              </div>
            ))}
          />
        ))}
      </ol>
      {(detail.stream || detail.streamOmitted) && (
        <section className="bpp-panel flex flex-col gap-3 p-4">
          <h2 className="m-0 text-base">{copy.path.decisions}</h2>
          {path.data?.visits.length ? (
            <SiteReplay
              visits={path.data.visits}
              index={path.data}
              battles={battles}
              art={art}
              names={names}
              locale={locale}
            />
          ) : (
            <p>{status}</p>
          )}
        </section>
      )}
      {path.data && <StreamDebug stream={path.data.stream} locale={locale} />}
    </>
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
  return <Decisions detail={run} steps={steps} art={art} locale={locale} />;
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
      {run && (
        <CardDetails locale={locale} key={runId}>
          <Timeline runId={runId} locale={locale} />
        </CardDetails>
      )}
    </PageLayout>
  );
}
