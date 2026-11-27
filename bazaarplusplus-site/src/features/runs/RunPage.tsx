import { useCallback, useMemo } from 'react';
import { decisionDays } from '@bpp/runs/decisions';
import { useQuery } from '@tanstack/react-query';
import { Badge, Button, StatusPanel } from '@bazaarplusplus/ui';
import {
  Board,
  CardCatalog,
  DayCard,
  DecisionReplay,
  SkillStrip,
  StreamFrames,
  fillTerm,
  gameTerms,
  useCardCatalog,
  type CardInput,
  type GameCard
} from '@bazaarplusplus/ui/game';

import {
  buildRouteHref,
  type AccountRoutePage,
  type Locale,
  type ResolvedSpaLocation
} from '../../app/router';
import { getSiteCopy } from '../../content/site-copy';
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

/** The site Worker serves the card catalog same-origin from cdn.bazaarplusplus.com/card-art/. */
const CARD_SOURCE = { base: '/card-art/' };

function tierName(locale: Locale, tier: string | null): string {
  return tier
    ? ((gameTerms[locale].tiers as Record<string, string>)[tier] ?? tier)
    : '';
}

function tiles(diff: CardDiff | null, card: (input: CardInput) => GameCard) {
  return (
    diff?.cards.map((c) => ({
      ...card({ ...c, type: null }),
      id: c.instanceId,
      slot: c.slot,
      size: c.size,
      change: c.change
    })) ?? null
  );
}

function Changes({ step, locale }: { step: TimelineStep; locale: Locale }) {
  const copy = getSiteCopy(locale).account;
  const { name } = useCardCatalog();
  const cardName = (c: RunCard) => name(c.templateId) ?? c.name ?? c.templateId;
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

function StepBody({ step, locale }: { step: TimelineStep; locale: Locale }) {
  const copy = getSiteCopy(locale).account;
  const { card, terms } = useCardCatalog();
  const { battle } = step;
  return (
    <>
      <div className="flex flex-wrap items-center gap-2">
        <Badge dot={false}>{battle.pvp ? terms.pvp : copy.pve}</Badge>
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
              {terms[battle.outcome]}
            </Badge>
          </span>
        )}
      </div>
      {battle.source === 'battle' && (
        <div className="grid gap-3 lg:grid-cols-[minmax(0,48rem)_minmax(0,1fr)] lg:gap-6">
          <div className="flex min-w-0 flex-col gap-2">
            <Board
              cards={tiles(step.board, card)}
              missingLabel={terms.missingBoard}
              emptyLabel={terms.emptyBoard}
            />
            <SkillStrip
              cards={tiles(step.skills, card)}
              missingLabel={terms.missingSkills}
              emptyLabel={terms.emptySkills}
              label={terms.skills}
            />
          </div>
          <Changes step={step} locale={locale} />
        </div>
      )}
    </>
  );
}

/** Battle day cards plus the Decision path replay decoded lazily from the Run Message Stream. */
function RunBody({
  detail,
  steps,
  locale
}: {
  detail: RunDetail;
  steps: TimelineStep[];
  locale: Locale;
}) {
  const copy = getSiteCopy(locale).account;
  const terms = gameTerms[locale];
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
    () =>
      new Map(
        steps.map(({ battle }) => [
          battle.id,
          {
            ...battle,
            opponentHero: battle.opponentHero && heroName(battle.opponentHero)
          }
        ])
      ),
    [steps]
  );
  const names = useCallback(
    (id: string) => detail.cardNames.get(id) ?? null,
    [detail.cardNames]
  );
  const status = detail.streamOmitted
    ? copy.streamOmitted
    : path.isError
      ? terms.replay.error
      : path.isPending
        ? copy.loading
        : terms.replay.noVisits;
  return (
    <>
      <ol className="flex flex-col gap-3">
        {days.map(({ day, battles: daySteps }) => (
          <DayCard
            key={day ?? 'unknown'}
            title={
              day === null
                ? terms.replay.unknownDay
                : fillTerm(terms.day, { n: day })
            }
            battles={daySteps.map((step) => (
              <div key={step.battle.id} className="flex flex-col gap-3">
                <StepBody step={step} locale={locale} />
              </div>
            ))}
          />
        ))}
      </ol>
      {(detail.stream || detail.streamOmitted) && (
        <section className="bpp-panel flex flex-col gap-3 p-4">
          <h2 className="m-0 text-base">{terms.replay.decisions}</h2>
          {path.data?.replay.length ? (
            <DecisionReplay
              replay={path.data.replay}
              battles={battles}
              names={names}
            />
          ) : (
            <p>{status}</p>
          )}
        </section>
      )}
      {path.data && <StreamFrames stream={path.data.stream} />}
    </>
  );
}

function Timeline({ runId, locale }: { runId: string; locale: Locale }) {
  const copy = getSiteCopy(locale).account;
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
  return <RunBody detail={run} steps={steps} locale={locale} />;
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
              <span className="text-text-3">
                {date.format(run.ended_at_ms)}
              </span>
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
        <CardCatalog source={CARD_SOURCE} locale={locale} key={runId}>
          <Timeline runId={runId} locale={locale} />
        </CardCatalog>
      )}
    </PageLayout>
  );
}
