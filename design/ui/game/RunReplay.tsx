import {
  memo,
  useId,
  useLayoutEffect,
  useMemo,
  useState,
  useSyncExternalStore,
  type ReactNode
} from 'react';
import { Badge } from '../Badge';
import { Button } from '../Button';
import { Board, SkillStrip } from './Board';
import { CardArt, CardChip, type GameCard } from './Card';
import type { CardSize } from './types';

type Delta = { positive: boolean; text: string };
export type ReplayStepView = {
  verb: string;
  card: { card: GameCard; title: string } | null;
  result: ReactNode;
  status: string | null;
  amount: string | null;
};
export type ReplayVisitView = {
  key: string | number;
  /** The day heading, shown where it changes; null when the day is unknown. */
  day: string | null;
  title: string;
  hour: string | null;
  encounter?: GameCard;
  outcome?: ReactNode;
  gold: Delta | null;
  health: Delta | null;
  gap: string | null;
  steps: readonly ReplayStepView[];
};

type Store = {
  get: () => number;
  set: (index: number, moved?: boolean) => void;
  subscribe: (listener: () => void) => () => void;
  /** Whether the last change came from the step keys or the panel buttons; reading clears it. */
  takeMoved: () => boolean;
};
function createStore(): Store {
  let active = 0;
  let movedLast = false;
  const listeners = new Set<() => void>();
  const store: Store = {
    get: () => active,
    set(index, moved = false) {
      movedLast = moved;
      if (index === active) return;
      active = index;
      for (const listener of listeners) listener();
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    takeMoved() {
      const moved = movedLast;
      movedLast = false;
      return moved;
    }
  };
  return store;
}

/**
 * One Run as a sequence of steps: every encounter choice, buy, sell, move and battle in
 * stream order, grouped under the visit it happened in. The panel shows the board, the
 * skills and the stash after the selected step.
 */
export function RunReplay({
  visits,
  render,
  previous,
  next
}: {
  visits: readonly ReplayVisitView[];
  /** The panel for one step, by visit and step position. */
  render: (visit: number, step: number) => ReactNode;
  previous: string;
  next: string;
}) {
  const [store] = useState(createStore);
  const prefix = useId();
  const flat = useMemo(
    () => visits.flatMap((v, vi) => v.steps.map((_, si) => [vi, si] as const)),
    [visits]
  );
  const active = Math.min(
    useSyncExternalStore(store.subscribe, store.get, store.get),
    Math.max(flat.length - 1, 0)
  );
  useLayoutEffect(() => {
    if (!store.takeMoved()) return;
    const button = document.getElementById(prefix + '-' + active);
    button?.scrollIntoView({ block: 'nearest' });
    button?.focus({ preventScroll: true });
  }, [active, prefix, store]);
  const at = flat[active];
  return (
    <div className="bpp-replay">
      <div className="bpp-replay-grid">
        {at && (
          <section
            id={prefix}
            aria-labelledby={prefix + '-' + active}
            className="bpp-moment"
          >
            <div className="bpp-moment-nav">
              <Button
                variant="ghost"
                size="sm"
                type="button"
                disabled={active === 0}
                onClick={() => store.set(active - 1, true)}
              >
                {previous}
              </Button>
              <span className="bpp-replay-position">
                {active + 1} / {flat.length}
              </span>
              <Button
                variant="ghost"
                size="sm"
                type="button"
                disabled={active === flat.length - 1}
                onClick={() => store.set(active + 1, true)}
              >
                {next}
              </Button>
            </div>
            {render(at[0], at[1])}
          </section>
        )}
        <Timeline
          visits={visits}
          store={store}
          prefix={prefix}
          last={flat.length - 1}
        />
      </div>
    </div>
  );
}

/** Each visit's first flat step and whether its day heading shows. */
function placeVisits(visits: readonly ReplayVisitView[]) {
  const out: { first: number; heading: boolean }[] = [];
  let first = 0;
  let day: string | null = null;
  for (const visit of visits) {
    out.push({ first, heading: visit.day !== null && visit.day !== day });
    first += visit.steps.length;
    if (visit.day !== null) day = visit.day;
  }
  return out;
}

const Timeline = memo(function Timeline({
  visits,
  store,
  prefix,
  last
}: {
  visits: readonly ReplayVisitView[];
  store: Store;
  prefix: string;
  last: number;
}) {
  const layout = useMemo(() => placeVisits(visits), [visits]);
  return (
    <ol className="bpp-replay-steps">
      {visits.map((visit, vi) => {
        const { first, heading } = layout[vi];
        return (
          <li key={visit.key}>
            {heading && <h3 className="bpp-replay-day">{visit.day}</h3>}
            <Visit visit={visit} />
            {visit.gap && <p className="bpp-replay-gap">{visit.gap}</p>}
            {visit.steps.map((step, si) => (
              <Step
                // The flat position is stable for a given list of visits.
                // eslint-disable-next-line react/no-array-index-key
                key={si}
                step={step}
                index={first + si}
                last={last}
                store={store}
                prefix={prefix}
              />
            ))}
          </li>
        );
      })}
    </ol>
  );
});

function Visit({ visit }: { visit: ReplayVisitView }) {
  const encounter = visit.encounter;
  const Art = encounter?.onOpen ? 'button' : 'span';
  return (
    <header className="bpp-visit-head">
      <h4 className="bpp-visit-identity">
        <Art
          type={encounter?.onOpen ? 'button' : undefined}
          className="bpp-visit-art"
          onClick={encounter?.onOpen}
          aria-label={encounter?.name}
        >
          <CardArt src={encounter?.art ?? null} alt="" fallback="" />
        </Art>
        <span>
          {visit.hour && <span className="bpp-visit-hour">{visit.hour}</span>}
          <strong>{visit.title}</strong>
        </span>
      </h4>
      <div className="bpp-visit-totals">
        {visit.outcome}
        {[visit.gold, visit.health].map(
          (delta, i) =>
            delta && (
              <span
                // gold first, then health
                // eslint-disable-next-line react/no-array-index-key
                key={i}
                className={
                  delta.positive ? 'bpp-decision-gain' : 'bpp-decision-amount'
                }
              >
                {delta.text}
              </span>
            )
        )}
      </div>
    </header>
  );
}

const Step = memo(function Step({
  step,
  index,
  last,
  store,
  prefix
}: {
  step: ReplayStepView;
  index: number;
  last: number;
  store: Store;
  prefix: string;
}) {
  const current = useSyncExternalStore(
    store.subscribe,
    () => store.get() === index,
    () => store.get() === index
  );
  return (
    <div className="bpp-decision-row">
      <button
        type="button"
        className="bpp-moment-button"
        id={prefix + '-' + index}
        aria-label={[step.verb, step.card?.card.name]
          .filter(Boolean)
          .join(' · ')}
        aria-controls={prefix}
        aria-current={current || undefined}
        tabIndex={current ? 0 : -1}
        onClick={() => store.set(index)}
        onKeyDown={(e) => {
          if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
          e.preventDefault();
          const to = index + (e.key === 'ArrowUp' ? -1 : 1);
          if (to >= 0 && to <= last) store.set(to, true);
        }}
      >
        {step.verb}
      </button>
      <div className="bpp-decision-card">
        {step.card && (
          <CardChip card={step.card.card} title={step.card.title} placeholder />
        )}
        {step.result && <span>→ {step.result}</span>}
        {step.status && <Badge tone="warn">{step.status}</Badge>}
      </div>
      {step.amount && (
        <span className="bpp-decision-amount">{step.amount}</span>
      )}
    </div>
  );
});

type Placed = GameCard & { id: string; size: CardSize; slot: number };
export function MomentPanel({
  view,
  labels,
  title,
  formatPrice,
  reroll
}: {
  view: {
    board: Placed[] | null;
    stash: Placed[] | null;
    skills: (GameCard & { id: string })[] | null;
    encounter: GameCard | null;
    encounterId: string | null;
    offer:
      | { card: GameCard; id: string; chosen: boolean; price: number | null }[]
      | null
      | undefined;
    gold: number | null;
    health: number | null;
    level: number | null;
  };
  labels: Record<
    | 'board'
    | 'stash'
    | 'skills'
    | 'unknown'
    | 'empty'
    | 'pickedFrom'
    | 'chosen'
    | 'goldTotal'
    | 'healthTotal'
    | 'level'
    | 'resultOffer',
    string
  >;
  title: string;
  formatPrice: (price: number) => string;
  reroll: boolean;
}) {
  const observation = {
    emptyLabel: labels.empty,
    missingLabel: labels.unknown
  };
  return (
    <>
      <h3 className="bpp-moment-title">{title}</h3>
      {view.encounter &&
        !view.offer?.some((o) => o.chosen && o.id === view.encounterId) && (
          <CardChip
            card={view.encounter}
            title={view.encounter.name}
            placeholder
          />
        )}
      {view.offer !== undefined && (
        <div>
          <h4>{reroll ? labels.resultOffer : labels.pickedFrom}</h4>
          <div className="bpp-moment-offers">
            {view.offer?.length === 0 && labels.empty}
            {view.offer?.map((o) => (
              <div key={o.id} data-chosen={o.chosen}>
                <CardChip card={o.card} title={o.card.name} placeholder />
                {o.price !== null && <span>{formatPrice(o.price)}</span>}
                {o.chosen && <strong>{labels.chosen}</strong>}
              </div>
            )) ?? labels.unknown}
          </div>
        </div>
      )}
      <div>
        <h4>{labels.board}</h4>
        <Board {...observation} cards={view.board} />
      </div>
      <div>
        <h4>{labels.skills}</h4>
        <SkillStrip
          {...observation}
          cards={view.skills}
          label={labels.skills}
        />
      </div>
      <div>
        <h4>{labels.stash}</h4>
        <Board {...observation} cards={view.stash} />
      </div>
      <dl className="bpp-moment-totals">
        {(['gold', 'health', 'level'] as const).map((k) => (
          <div key={k}>
            <dt>
              {
                labels[
                  k === 'level' ? k : k === 'gold' ? 'goldTotal' : 'healthTotal'
                ]
              }
            </dt>
            <dd>{view[k] ?? labels.unknown}</dd>
          </div>
        ))}
      </dl>
    </>
  );
}
