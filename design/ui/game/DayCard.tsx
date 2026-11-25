import { Badge } from '../Badge';
import { useState, type ReactNode } from 'react';
import { CardArt, CardChip, type GameCard } from './Card';

export function DayCard({
  title,
  battles,
  decisionsLabel,
  decisions
}: {
  title: string;
  battles: ReactNode;
  decisionsLabel: string;
  decisions?: ReactNode;
}) {
  const [expanded, setExpanded] = useState(false);
  return (
    <li className="bpp-panel bpp-day-card">
      <h2 className="bpp-day-title">{title}</h2>
      {battles}
      {decisions && (
        <details
          className="bpp-day-decisions"
          onToggle={(e) => setExpanded(e.currentTarget.open)}
        >
          <summary>{decisionsLabel}</summary>
          {expanded && decisions}
        </details>
      )}
    </li>
  );
}

export function VisitRow({
  hour,
  title,
  encounter,
  totals,
  children
}: {
  hour: string | null;
  title: string;
  encounter?: GameCard;
  totals: ReactNode;
  children: ReactNode;
}) {
  const Element = encounter?.onOpen ? 'button' : 'span';
  return (
    <li className="bpp-visit-row">
      <header className="bpp-visit-head">
        <h3>
          <Element className="bpp-visit-identity" onClick={encounter?.onOpen}>
            <span className="bpp-visit-art">
              <CardArt src={encounter?.art ?? null} alt="" fallback="" />
            </span>
            <span>
              {hour && <span className="bpp-visit-hour">{hour}</span>}
              <strong>{title}</strong>
            </span>
          </Element>
        </h3>
        <div className="bpp-visit-totals">{totals}</div>
      </header>
      {children}
    </li>
  );
}

type Delta = { positive: boolean; text: string };
function VisitDelta({ delta }: { delta: Delta | null }) {
  return (
    delta && (
      <span
        className={delta.positive ? 'bpp-decision-gain' : 'bpp-decision-amount'}
      >
        {delta.text}
      </span>
    )
  );
}

type Chip = { card: GameCard; title: string; id: string };
export function DecisionVisitRow({
  view,
  outcome,
  renderResult
}: {
  view: {
    title: string;
    encounter?: GameCard;
    hour: string | null;
    gold: Delta | null;
    health: Delta | null;
    gap: string | null;
    rows: readonly {
      id: number;
      verb: string;
      chip: Chip | null;
      result: string | null;
      amount: string | null;
      status: string | null;
    }[];
    remainingLabel: string;
    remaining: readonly Chip[];
  };
  outcome: ReactNode;
  renderResult: (value: string) => ReactNode;
}) {
  return (
    <VisitRow
      title={view.title}
      hour={view.hour}
      encounter={view.encounter}
      totals={
        <>
          {outcome}
          <VisitDelta delta={view.gold} />
          <VisitDelta delta={view.health} />
        </>
      }
    >
      {view.gap}
      {view.rows.map((row) => (
        <div className="bpp-decision-row" key={row.id}>
          <span>{row.verb}</span>
          <div className="bpp-decision-card">
            {row.chip && (
              <CardChip
                card={row.chip.card}
                title={row.chip.title}
                placeholder
              />
            )}
            {row.result && <span>→ {renderResult(row.result)}</span>}
            {row.status && <Badge tone="warn">{row.status}</Badge>}
          </div>
          {row.amount && (
            <span className="bpp-decision-amount">{row.amount}</span>
          )}
        </div>
      ))}
      {!!view.remaining.length && (
        <details className="bpp-visit-options">
          <summary>{view.remainingLabel}</summary>
          {view.remaining.map((chip) => (
            <div className="bpp-decision-row" key={chip.id}>
              <div className="bpp-decision-card">
                <CardChip card={chip.card} title={chip.title} placeholder />
              </div>
            </div>
          ))}
        </details>
      )}
    </VisitRow>
  );
}
