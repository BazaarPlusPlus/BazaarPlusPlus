import { CardTile, type GameCard } from './Card';
import type { CardSize } from './types';

type Skill = GameCard & { id: string };
type BoardCard = Skill & { slot: number | null; size: CardSize };
type Observation<T> = {
  cards: readonly T[] | null;
  missingLabel: string;
  emptyLabel: string;
};

export function Board({
  cards,
  emptyLabel,
  missingLabel
}: Observation<BoardCard>) {
  if (!cards) return <div className="bpp-board-state">{missingLabel}</div>;
  return (
    <div className="bpp-board">
      {Array.from({ length: 10 }, (_, slot) => (
        <span
          key={slot}
          aria-hidden="true"
          className="bpp-board-slot"
          style={{ gridColumn: slot + 1 }}
        />
      ))}
      {cards.map((card) => {
        const span = Math.min(3, Math.max(1, card.size));
        const start = Math.min(10 - span, Math.max(0, card.slot ?? 0));
        return (
          <div
            className="bpp-board-card"
            key={card.id}
            style={{ gridColumn: `${start + 1} / span ${span}` }}
          >
            <CardTile card={card} />
          </div>
        );
      })}
      {!cards.length && <span className="bpp-board-empty">{emptyLabel}</span>}
    </div>
  );
}

export function SkillStrip({
  cards,
  emptyLabel,
  missingLabel,
  label
}: Observation<Skill> & { label: string }) {
  if (!cards?.length)
    return (
      <span className="bpp-skill-state">
        {cards ? emptyLabel : missingLabel}
      </span>
    );
  return (
    <ul aria-label={label} className="bpp-skill-strip">
      {cards.map((card) => (
        <li key={card.id} className="bpp-skill">
          <CardTile card={card} caption />
        </li>
      ))}
    </ul>
  );
}
