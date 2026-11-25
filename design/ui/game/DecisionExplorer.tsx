import {
  createContext,
  useContext,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode
} from 'react';
import { Board, SkillStrip } from './Board';
import { CardChip, type GameCard } from './Card';
import type { CardSize } from './types';

type Selection = {
  active: number | undefined;
  prefix: string;
  select: (seq: number) => void;
  step: (offset: number, from?: number) => void;
  inline: ReactNode;
};
const SelectionContext = createContext<Selection | null>(null);
export const useDecisionSelection = () => useContext(SelectionContext);

export function DecisionExplorer({
  ids: order,
  children,
  previous,
  next,
  render
}: {
  ids: number[];
  children: ReactNode;
  previous: string;
  next: string;
  render: (seq: number) => ReactNode;
}) {
  const [picked, setPicked] = useState<number>();
  const [wide, setWide] = useState(false);
  const container = useRef<HTMLDivElement>(null);
  const focus = useRef(false);
  const prefix = useId();

  const active =
    picked !== undefined && order.includes(picked) ? picked : order[0];
  const idx = order.indexOf(active);
  useLayoutEffect(() => {
    if (!container.current || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(([entry]) =>
      setWide(entry.contentRect.width >= 720)
    );
    observer.observe(container.current);
    return () => observer.disconnect();
  }, []);
  useLayoutEffect(() => {
    if (focus.current) {
      document.getElementById(prefix + '-' + active)?.focus();
      focus.current = false;
    }
  }, [active, prefix]);
  const { panel: detail, selection } = useMemo(() => {
    function step(offset: number, from = active) {
      const target = order[order.indexOf(from) + offset];
      if (target === active) {
        document.getElementById(prefix + '-' + target)?.focus();
        return;
      }
      if (target !== undefined) {
        focus.current = true;
        setPicked(target);
      }
    }
    const panel = active !== undefined && (
      <section
        id={prefix}
        aria-labelledby={prefix + '-' + active}
        className="bpp-moment"
      >
        <div className="bpp-moment-nav">
          <button type="button" disabled={idx === 0} onClick={() => step(-1)}>
            {previous}
          </button>
          <button
            type="button"
            disabled={idx === order.length - 1}
            onClick={() => step(1)}
          >
            {next}
          </button>
        </div>
        {render(active)}
      </section>
    );
    return {
      panel,
      selection: {
        active,
        prefix,
        select: setPicked,
        step,
        inline: wide ? null : panel
      }
    };
  }, [active, idx, next, order, prefix, previous, render, wide]);
  return (
    <SelectionContext.Provider value={selection}>
      <div className="bpp-decision-explorer" data-wide={wide} ref={container}>
        <ol>{children}</ol>
        {wide && detail}
      </div>
    </SelectionContext.Provider>
  );
}

export function MomentButton({
  seq,
  children,
  label
}: {
  seq: number;
  children: ReactNode;
  label?: string;
}) {
  const selection = useDecisionSelection();
  return (
    <button
      type="button"
      className="bpp-moment-button"
      id={selection?.prefix + '-' + seq}
      aria-label={label}
      aria-controls={selection?.prefix}
      aria-current={selection?.active === seq || undefined}
      onClick={() => selection?.select(seq)}
      onKeyDown={(e) => {
        if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
          e.preventDefault();
          selection?.step(e.key === 'ArrowUp' ? -1 : 1, seq);
        }
      }}
    >
      {children}
    </button>
  );
}

type Placed = GameCard & { id: string; size: CardSize; slot: number };
export function MomentPanel({
  view,
  labels,
  title,
  reroll
}: {
  view: {
    board: Placed[] | null;
    stash: Placed[] | null;
    skills: (GameCard & { id: string })[] | null;
    encounter: GameCard | null;
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
  reroll: boolean;
}) {
  const observation = {
    emptyLabel: labels.empty,
    missingLabel: labels.unknown
  };
  return (
    <>
      <h3 className="bpp-moment-title">{title}</h3>
      {view.encounter && (
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
                {o.price !== null && (
                  <span>
                    {labels.goldTotal} {o.price}
                  </span>
                )}
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
