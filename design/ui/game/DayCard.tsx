import { useState, type ReactNode } from 'react';
import { CardChip, type GameCard } from './Card';

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
  kind,
  encounter,
  outcome,
  remaining
}: {
  kind: string;
  encounter?: GameCard;
  outcome: ReactNode;
  remaining?: ReactNode;
}) {
  return (
    <li className="bpp-visit-row">
      <strong>{kind}</strong>
      {encounter && <CardChip card={encounter} title={encounter.name} />}
      {outcome}
      {remaining}
    </li>
  );
}
