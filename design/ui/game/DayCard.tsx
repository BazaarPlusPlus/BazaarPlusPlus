import { useState, type ReactNode } from 'react';
import { CardArt, type GameCard } from './Card';

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
